namespace financesApi.models;

public sealed record ReconciliationTransactionDto(
    int Id,
    DateOnly Date,
    decimal Amount,
    string? Payee,
    string? Memo,
    string CategoryName,
    string Status,
    bool IsCleared,
    bool IsReconciliationAdjustment,
    decimal RunningBalance);

public sealed record StatementSessionDto(
    int Id,
    int AccountId,
    string AccountName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal StatementOpeningBalance,
    decimal StatementClosingBalance,
    decimal ExpectedOpeningBalance,
    decimal OpeningDiscrepancy,
    decimal ClearedBalance,
    decimal ClosingDiscrepancy,
    int ClearedTransactionCount,
    int TransactionCount,
    string Status,
    bool CanClose,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

public sealed record StatementSessionDetailDto(
    StatementSessionDto Session,
    IReadOnlyList<ReconciliationTransactionDto> Transactions);

public sealed record CreateStatementSessionRequest(
    int AccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal StatementOpeningBalance,
    decimal StatementClosingBalance);

public sealed record UpdateStatementTransactionClearedRequest(bool Cleared);
