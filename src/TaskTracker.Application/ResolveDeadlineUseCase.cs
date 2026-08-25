using System;
using System.Collections.Generic;
using System.Linq;
using TaskTracker.Domain;

namespace TaskTracker.Application;

public enum DeadlineReviewAction
{
    KeepExcelDate,
    UseSwappedDate,
    ManualDate,
    MarkUnresolved
}

public class ResolveDeadlineRequest
{
    public string SourceFileId { get; set; } = "";
    public string LogicalRowKey { get; set; } = "";
    public DeadlineReviewAction Action { get; set; }
    public DateOnly? ManualDate { get; set; }
    public DateOnly? ManualEndDate { get; set; }
    public TimeSpan? ManualTime { get; set; }
}

public class ResolveDeadlineResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TaskRow? UpdatedRow { get; init; }
}

/// <summary>
/// Port for persisting user deadline corrections, implemented by Infrastructure (SQLite).
/// </summary>
public interface IResolutionStore
{
    void Upsert(DeadlineResolution resolution);
    IReadOnlyList<DeadlineResolution> GetAll();
    DeadlineResolution? FindByKey(string logicalRowKey, string rawDeadlineFingerprint);
}

public class ResolveDeadlineUseCase
{
    private readonly ITaskRowStore _taskRepository;
    private readonly IResolutionStore _resolutionRepository;
    private readonly RowIdentityService _identityService;
    private readonly TaskStatusCalculator _statusCalculator;
    private readonly IClock _clock;
    private readonly DeadlineParser _parser;

    public ResolveDeadlineUseCase(
        ITaskRowStore taskRepository,
        IResolutionStore resolutionRepository,
        RowIdentityService identityService,
        TaskStatusCalculator statusCalculator,
        IClock clock)
    {
        _taskRepository = taskRepository;
        _resolutionRepository = resolutionRepository;
        _identityService = identityService;
        _statusCalculator = statusCalculator;
        _clock = clock;
        _parser = new DeadlineParser();
    }

    public ResolveDeadlineResult Execute(ResolveDeadlineRequest request)
    {
        try
        {
            var currentRows = _taskRepository.GetCurrentRows(request.SourceFileId);
            var row = currentRows.FirstOrDefault(r => r.LogicalRowKey == request.LogicalRowKey);

            if (row == null)
            {
                return new ResolveDeadlineResult
                {
                    Success = false,
                    ErrorMessage = $"Không tìm thấy dòng với key: {request.LogicalRowKey}"
                };
            }

            // Parse the current raw deadline to recover candidates
            var spec = ParseCurrentDeadline(row);
            var fingerprint = _identityService.GenerateRawDeadlineFingerprint(row.DeadlineRaw);

            DateOnly? startDate;
            DateOnly? endDate;
            ResolutionSource source;
            bool requiresReview;

            switch (request.Action)
            {
                case DeadlineReviewAction.KeepExcelDate:
                    if (spec.StartDate == null)
                    {
                        return Fail($"Dòng này không có ngày gốc hợp lệ để giữ (raw: '{row.DeadlineRaw}')");
                    }
                    startDate = spec.StartDate;
                    endDate = spec.EndDate ?? spec.StartDate;
                    source = ResolutionSource.KeepExcelDate;
                    requiresReview = false;
                    break;

                case DeadlineReviewAction.UseSwappedDate:
                    if (spec.AmbiguousCandidates == null || spec.AmbiguousCandidates.Count < 2)
                    {
                        return Fail("Dòng này không có ứng viên ngày đảo (chỉ áp dụng cho lỗi nghi ngờ đảo ngày/tháng)");
                    }
                    startDate = spec.AmbiguousCandidates[1];
                    endDate = spec.AmbiguousCandidates[1];
                    source = ResolutionSource.UseSwappedDate;
                    requiresReview = false;
                    break;

                case DeadlineReviewAction.ManualDate:
                    if (request.ManualDate == null)
                    {
                        return Fail("Phải cung cấp ngày thủ công (ManualDate)");
                    }
                    startDate = request.ManualDate;
                    endDate = request.ManualEndDate ?? request.ManualDate;
                    source = ResolutionSource.ManualDate;
                    requiresReview = false;
                    break;

                case DeadlineReviewAction.MarkUnresolved:
                default:
                    startDate = null;
                    endDate = null;
                    source = ResolutionSource.UnresolvedByUser;
                    requiresReview = true;
                    break;
            }

            var now = _clock.UtcNow;

            // Persist the resolution keyed by (row, raw fingerprint).
            // If the user later edits the Excel cell, the fingerprint changes
            // and this resolution stops applying automatically.
            _resolutionRepository.Upsert(new DeadlineResolution(
                row.LogicalRowKey,
                fingerprint,
                spec.Kind,
                row.DeadlineRaw,
                spec.StartDate,
                spec.AmbiguousCandidates?.Count > 1 ? spec.AmbiguousCandidates[1] : null,
                startDate,
                endDate,
                request.Action == DeadlineReviewAction.ManualDate ? request.ManualTime : spec.TimeOfDay,
                source,
                requiresReview,
                now
            ));

            // Recompute derived fields and persist to the current row
            var alertDate = startDate;
            var isCompleted = row.IsCompleted;
            var newVersion = _identityService.GenerateDeadlineVersion(
                spec.Kind, startDate, endDate,
                request.Action == DeadlineReviewAction.ManualDate ? request.ManualTime : spec.TimeOfDay,
                source);
            var newStatus = _statusCalculator.CalculateStatus(isCompleted, requiresReview, alertDate);
            var daysRemaining = _statusCalculator.CalculateDaysRemaining(alertDate);
            var newSnapshotId = $"correction-{now:yyyyMMddHHmmss}";

            _taskRepository.UpdateDeadlineForCorrection(
                request.SourceFileId, row.LogicalRowKey,
                newVersion, alertDate, isCompleted, newStatus, daysRemaining, newSnapshotId);

            var updatedRow = row with
            {
                DeadlineVersion = newVersion,
                CurrentStatus = newStatus,
                DaysRemaining = daysRemaining,
                SnapshotId = newSnapshotId
            };

            return new ResolveDeadlineResult { Success = true, UpdatedRow = updatedRow };
        }
        catch (Exception ex)
        {
            return new ResolveDeadlineResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Re-applies a stored user resolution during import, when the raw cell
    /// fingerprint is unchanged. Returns null if no resolution applies.
    /// </summary>
    public DeadlineResolution? FindApplicableResolution(string logicalRowKey, string? rawDeadline)
    {
        var fingerprint = _identityService.GenerateRawDeadlineFingerprint(rawDeadline);
        return _resolutionRepository.FindByKey(logicalRowKey, fingerprint);
    }

    private DeadlineSpec ParseCurrentDeadline(TaskRow row)
    {
        // Recover the spec from the raw text using the text parser.
        // Excel numeric dates were already resolved at import time; for the
        // review flow the raw text representation is what the user saw.
        var spec = _parser.ParseText(row.DeadlineRaw);

        if (spec.Kind == DeadlineParserKind.Unrecognized || spec.Kind == DeadlineParserKind.Invalid)
        {
            // The row may have been imported as a numeric Excel date.
            // Fall back to an unresolved spec so the UI shows manual entry.
            return new DeadlineSpec(
                DeadlineParserKind.Invalid, row.DeadlineRaw,
                null, null, null, null, true, "CannotReparseRawValue");
        }

        return spec;
    }

    private static ResolveDeadlineResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
