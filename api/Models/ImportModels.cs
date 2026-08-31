namespace financesApi.models;

public sealed record ParsedFinancialRow(
    int Ordinal,
    string SourceLabel,
    TransactionDto? Transaction,
    string? DisplayDate,
    string? DisplayAmount,
    string? Payee,
    string? Memo,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record FinancialImportParseResult(
    string FileType,
    IReadOnlyList<ParsedFinancialRow> Rows);

public sealed record ImportBatchSummary(
    long Id,
    int AccountId,
    string AccountName,
    string FileName,
    string FileType,
    long FileSize,
    string FileSha256,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UndoneAt,
    int Total,
    int Importable,
    int Imported,
    int Skipped,
    int Rejected,
    bool CanUndo);

public sealed record ImportRowResult(
    long Id,
    int Ordinal,
    string SourceLabel,
    DateOnly? Date,
    string? DisplayDate,
    decimal? Amount,
    string? DisplayAmount,
    string? Payee,
    string? Memo,
    string Outcome,
    string? ReasonCode,
    string? ReasonMessage);

public sealed record PagedImportBatches(
    IReadOnlyList<ImportBatchSummary> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record PagedImportRows(
    IReadOnlyList<ImportRowResult> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record ImportUndoResult(long BatchId, int AccountId, int Deleted);

public sealed class ImportBatchConflictException(string message) : Exception(message);
public sealed class ImportBatchExpiredException(string message) : Exception(message);
