using System;
using System.Collections.Generic;
using System.Linq;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Application;

public class ImportDiagnostics
{
    public int TotalRowsFound { get; set; }
    public int ValidRowsImported { get; set; }
    public int ParseErrors { get; set; }
    public int AmbiguousDatesDetected { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ImportWorkbookUseCase
{
    private readonly ExcelReader _excelReader;
    private readonly RowIdentityService _identityService;
    private readonly DeadlineParser _deadlineParser;
    private readonly ExcelDateResolver _excelResolver;
    private readonly TaskStatusCalculator _statusCalculator;
    private readonly SqliteTaskRepository _repository;
    private readonly IClock _clock;

    public ImportWorkbookUseCase(
        ExcelReader excelReader,
        RowIdentityService identityService,
        DeadlineParser deadlineParser,
        ExcelDateResolver excelResolver,
        TaskStatusCalculator statusCalculator,
        SqliteTaskRepository repository,
        IClock clock)
    {
        _excelReader = excelReader;
        _identityService = identityService;
        _deadlineParser = deadlineParser;
        _excelResolver = excelResolver;
        _statusCalculator = statusCalculator;
        _repository = repository;
        _clock = clock;
    }

    public ImportDiagnostics Execute(string sourceFileId, System.IO.Stream excelStream)
    {
        var diagnostics = new ImportDiagnostics();
        var snapshotId = Guid.NewGuid().ToString("N");

        try
        {
            // 1. Read Excel
            var rawRows = _excelReader.ReadWorkbook(excelStream);
            diagnostics.TotalRowsFound = rawRows.Count;

            // 2. Identity Assignment
            var dtoList = rawRows.Select(r => new TaskRowDto
            {
                SheetName = r.SheetName,
                DocumentNumber = r.DocumentNumber,
                TaskContent = r.TaskContent,
                PrimaryHandler = r.PrimaryHandler
            }).ToList();

            _identityService.AssignIdentities(sourceFileId, dtoList);

            // 3. Process each row
            var processedRows = new List<TaskRow>();

            for (int i = 0; i < rawRows.Count; i++)
            {
                var raw = rawRows[i];
                var identity = dtoList[i];

                // Parse Deadline
                DeadlineSpec deadlineSpec;
                if (raw.DeadlineCell?.NumericValue.HasValue == true)
                {
                    deadlineSpec = _excelResolver.Resolve(
                        raw.DeadlineCell.NumericValue.Value,
                        raw.DateSystem,
                        raw.DeadlineCell.TextValue);
                }
                else
                {
                    deadlineSpec = _deadlineParser.ParseText(raw.DeadlineCell?.TextValue);
                }

                if (deadlineSpec.Kind == DeadlineParserKind.Invalid || deadlineSpec.Kind == DeadlineParserKind.Unrecognized)
                {
                    diagnostics.ParseErrors++;
                }
                if (deadlineSpec.Kind == DeadlineParserKind.ExcelDateAmbiguous)
                {
                    diagnostics.AmbiguousDatesDetected++;
                }

                // Deadline version based on parsed spec (assuming parser source initially)
                var deadlineVersion = _identityService.GenerateDeadlineVersion(
                    deadlineSpec.Kind,
                    deadlineSpec.StartDate,
                    deadlineSpec.EndDate,
                    deadlineSpec.TimeOfDay,
                    ResolutionSource.Parser);

                // Calculate status
                var isCompleted = _statusCalculator.IsCompleted(raw.Result);
                var currentStatus = _statusCalculator.CalculateStatus(
                    isCompleted,
                    deadlineSpec.RequiresReview,
                    deadlineSpec.AlertDate);

                var daysRemaining = _statusCalculator.CalculateDaysRemaining(deadlineSpec.AlertDate);

                // Build TaskRow
                processedRows.Add(new TaskRow
                {
                    SourceFileId = sourceFileId,
                    LogicalRowKey = identity.LogicalRowKey,
                    SnapshotId = snapshotId,
                    IsCurrent = true,
                    SheetName = raw.SheetName,
                    SheetWeekNumber = raw.SheetWeekNumber,
                    SourceRowNumber = raw.SourceRowNumber,
                    Stt = raw.Stt,
                    DocumentNumber = raw.DocumentNumber,
                    TaskContent = raw.TaskContent,
                    ExecutingUnit = raw.ExecutingUnit,
                    PrimaryHandler = raw.PrimaryHandler,
                    DeadlineRaw = raw.DeadlineCell?.TextValue,
                    Progress = raw.Progress,
                    Result = raw.Result,
                    Note = raw.Note,
                    IsCompleted = isCompleted,
                    DeadlineVersion = deadlineVersion,
                    CurrentStatus = currentStatus,
                    DaysRemaining = daysRemaining
                });
            }

            // 4. Save to Repository (Transactional)
            _repository.CommitSnapshot(snapshotId, sourceFileId, processedRows);

            diagnostics.ValidRowsImported = processedRows.Count;
            return diagnostics;
        }
        catch (Exception ex)
        {
            diagnostics.ErrorMessage = ex.Message;
            // The previous snapshot remains current in the DB because CommitSnapshot failed or wasn't called.
            return diagnostics;
        }
    }
}
