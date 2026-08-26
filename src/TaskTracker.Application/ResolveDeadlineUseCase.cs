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
    void Delete(string logicalRowKey, string rawDeadlineFingerprint);
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
                    if (row.ExcelCandidate == null)
                    {
                        return Fail($"Dòng này không có ngày gốc hợp lệ để giữ (raw: '{row.DeadlineRaw}')");
                    }
                    startDate = row.ExcelCandidate;
                    endDate = row.ExcelCandidate;
                    source = ResolutionSource.KeepExcelDate;
                    requiresReview = false;
                    break;

                case DeadlineReviewAction.UseSwappedDate:
                    if (row.SwappedCandidate == null)
                    {
                        return Fail("Dòng này không có ứng viên ngày đảo (chỉ áp dụng cho lỗi nghi ngờ đảo ngày/tháng)");
                    }
                    startDate = row.SwappedCandidate;
                    endDate = row.SwappedCandidate;
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
                row.DeadlineKind,
                row.DeadlineRaw,
                row.ExcelCandidate,
                row.SwappedCandidate,
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
                row.DeadlineKind, startDate, endDate,
                request.Action == DeadlineReviewAction.ManualDate ? request.ManualTime : spec.TimeOfDay,
                source);
            var newStatus = _statusCalculator.CalculateStatus(isCompleted, requiresReview, alertDate);
            var daysRemaining = _statusCalculator.CalculateDaysRemaining(alertDate);
            var newSnapshotId = $"correction-{Guid.NewGuid():N}";

            _taskRepository.UpdateDeadlineForCorrection(request.SourceFileId, row.LogicalRowKey,
                new DeadlineCorrectionUpdate(
                    newVersion,
                    row.DeadlineKind,
                    row.ExcelCandidate,
                    row.SwappedCandidate,
                    startDate,
                    endDate,
                    request.Action == DeadlineReviewAction.ManualDate ? request.ManualTime : spec.TimeOfDay,
                    source,
                    requiresReview,
                    newStatus,
                    daysRemaining,
                    newSnapshotId));

            var updatedRow = row with
            {
                DeadlineVersion = newVersion,
                ResolvedStartDate = startDate,
                ResolvedEndDate = endDate,
                ResolvedTime = request.Action == DeadlineReviewAction.ManualDate ? request.ManualTime : spec.TimeOfDay,
                ResolutionSource = source,
                RequiresReview = requiresReview,
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

    public ResolveDeadlineResult Reset(string sourceFileId, string logicalRowKey)
    {
        try
        {
            var row = _taskRepository.GetCurrentRows(sourceFileId)
                .FirstOrDefault(r => r.LogicalRowKey == logicalRowKey);
            if (row == null) return Fail($"Không tìm thấy dòng với key: {logicalRowKey}");

            var fingerprint = _identityService.GenerateRawDeadlineFingerprint(row.DeadlineRaw);
            _resolutionRepository.Delete(logicalRowKey, fingerprint);

            var spec = BuildOriginalSpec(row);
            var version = _identityService.GenerateDeadlineVersion(
                spec.Kind, spec.StartDate, spec.EndDate, spec.TimeOfDay, ResolutionSource.Parser);
            var status = _statusCalculator.CalculateStatus(row.IsCompleted, spec.RequiresReview, spec.AlertDate);
            var daysRemaining = _statusCalculator.CalculateDaysRemaining(spec.AlertDate);
            var snapshotId = $"correction-{Guid.NewGuid():N}";

            _taskRepository.UpdateDeadlineForCorrection(sourceFileId, logicalRowKey,
                new DeadlineCorrectionUpdate(
                    version,
                    spec.Kind,
                    row.ExcelCandidate,
                    row.SwappedCandidate,
                    spec.StartDate,
                    spec.EndDate,
                    spec.TimeOfDay,
                    ResolutionSource.Parser,
                    spec.RequiresReview,
                    status,
                    daysRemaining,
                    snapshotId));

            return new ResolveDeadlineResult
            {
                Success = true,
                UpdatedRow = row with
                {
                    DeadlineVersion = version,
                    DeadlineKind = spec.Kind,
                    ResolvedStartDate = spec.StartDate,
                    ResolvedEndDate = spec.EndDate,
                    ResolvedTime = spec.TimeOfDay,
                    ResolutionSource = ResolutionSource.Parser,
                    RequiresReview = spec.RequiresReview,
                    CurrentStatus = status,
                    DaysRemaining = daysRemaining,
                    SnapshotId = snapshotId
                }
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private DeadlineSpec ParseCurrentDeadline(TaskRow row)
    {
        return new DeadlineSpec(
            row.DeadlineKind,
            row.DeadlineRaw,
            row.ResolvedStartDate,
            row.ResolvedEndDate,
            row.ResolvedTime,
            row.RequiresReview ? null : row.ResolvedStartDate,
            row.RequiresReview,
            row.RequiresReview ? "RequiresReview" : null,
            new[] { row.ExcelCandidate, row.SwappedCandidate }.OfType<DateOnly>().ToArray());
    }

    private DeadlineSpec BuildOriginalSpec(TaskRow row)
    {
        if (row.DeadlineCellKind is "Number" or "DateTime" && row.ExcelCandidate != null)
        {
            var ambiguous = row.SwappedCandidate != null;
            var candidates = new[] { row.ExcelCandidate, row.SwappedCandidate }.OfType<DateOnly>().ToArray();
            return new DeadlineSpec(
                ambiguous ? DeadlineParserKind.ExcelDateAmbiguous : DeadlineParserKind.ExcelDateConfirmed,
                row.DeadlineRaw,
                ambiguous ? null : row.ExcelCandidate,
                ambiguous ? null : row.ExcelCandidate,
                null,
                ambiguous ? null : row.ExcelCandidate,
                ambiguous,
                ambiguous ? "AmbiguousDayMonth" : null,
                candidates);
        }

        return _parser.ParseText(row.DeadlineRaw);
    }

    private static ResolveDeadlineResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
