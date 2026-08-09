using System.Text.Json.Serialization;

namespace financesApi.models;

public sealed record UserProfileDto(
    int Id,
    string FirstName,
    string LastName,
    string HouseholdName);

public sealed record EnrollmentStatusDto(
    bool IsEnrolled,
    UserProfileDto? Profile);

public sealed record SaveEnrollmentRequest(
    string FirstName,
    string LastName,
    string HouseholdName);

public sealed record HouseholdSettingsDto(
    string HouseholdName,
    string CurrencyCode,
    string Locale,
    string Timezone);

public sealed record UpdateHouseholdSettingsRequest(
    string HouseholdName,
    string CurrencyCode,
    string Locale,
    string Timezone);

public sealed record AccountDto(
    int Id,
    string Name,
    string OwnerName,
    bool IsShared,
    string PrimaryHolderName,
    string? SecondaryHolderName,
    string AccountType,
    string? Institution,
    string? LastFour,
    decimal? CreditLimit,
    decimal Balance,
    decimal DebtBalance,
    decimal CreditBalance,
    decimal? AvailableCredit,
    decimal? CreditUtilizationPercent,
    decimal SafeZoneAmount,
    bool IncludeInSafeToSpend,
    bool IsArchived);

public sealed record CreateAccountRequest(
    string Name,
    string? FirstName,
    string? LastName,
    bool IsShared,
    string AccountType,
    string? Institution,
    string? LastFour,
    decimal OpeningBalance,
    DateOnly OpeningDate,
    decimal SafeZoneAmount,
    bool IncludeInSafeToSpend,
    string? PrimaryHolderName = null,
    string? SecondaryHolderName = null,
    decimal? CreditLimit = null);

public sealed record UpdateAccountRequest(
    string Name,
    bool IsShared,
    string AccountType,
    string? Institution,
    string? LastFour,
    decimal SafeZoneAmount,
    bool IncludeInSafeToSpend,
    bool IsArchived,
    string? PrimaryHolderName = null,
    string? SecondaryHolderName = null,
    decimal? CreditLimit = null);

public sealed record CategoryDto(
    int Id,
    string Name,
    string Kind,
    string IconKey,
    string ColorKey,
    bool IsSystem);

public sealed record TransactionTypeCodeDto(
    string Code,
    string Meaning,
    string Institution);

public sealed record TransactionDtoV2(
    int Id,
    int AccountId,
    string AccountName,
    string AccountType,
    DateOnly Date,
    decimal Amount,
    string? Payee,
    string? Memo,
    string? TransactionTypeCode,
    string? TransactionTypeMeaning,
    int? CategoryId,
    string CategoryName,
    string Status,
    bool IsTransfer,
    string? SourceFileType,
    decimal RunningBalance,
    int? RecurringItemId);

public sealed record TransactionPageDto(
    IReadOnlyList<TransactionDtoV2> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record UpdateTransactionCategoryRequest(int CategoryId, bool SaveRule = false);

public sealed record TransactionRuleDto(
    int Id,
    string ReferenceText,
    string Direction,
    int CategoryId,
    string CategoryName,
    int Priority,
    bool IsActive);

public sealed record GoalDto(
    int Id,
    string Name,
    string? Description,
    decimal TargetAmount,
    DateOnly? TargetDate,
    int AccountId,
    string AccountName,
    int PriorityOrder,
    string IconKey,
    string ColorKey,
    int? ImageId,
    string? ImageUrl,
    string Status,
    decimal AllocatedAmount,
    decimal RemainingAmount,
    decimal ProgressPercent,
    int? DaysRemaining,
    decimal? RequiredWeekly,
    decimal? RequiredMonthly,
    bool IsFunded);

public sealed record GoalSummaryDto(
    IReadOnlyList<GoalDto> Items,
    decimal AllocatedTotal,
    decimal TargetTotal,
    decimal ProgressPercent);

public sealed record SaveGoalRequest(
    string Name,
    string? Description,
    decimal TargetAmount,
    DateOnly? TargetDate,
    int AccountId,
    int PriorityOrder,
    string IconKey,
    string ColorKey,
    int? ImageId,
    string Status = "active");

public sealed record ReorderGoalsRequest(IReadOnlyList<int> OrderedIds);

public sealed record RecurringItemDto(
    int Id,
    string Name,
    string Kind,
    int AccountId,
    string AccountName,
    int? CategoryId,
    string? CategoryName,
    decimal Amount,
    string Frequency,
    DateOnly NextDate,
    string Source,
    bool IsActive,
    string? MatchText,
    decimal AmountTolerance,
    int DateWindowDays,
    string NextStatus,
    DateOnly? LastMatchedDate);

public sealed record SaveRecurringItemRequest(
    string Name,
    string Kind,
    int AccountId,
    int? CategoryId,
    decimal Amount,
    string Frequency,
    DateOnly NextDate,
    string Source = "manual",
    bool IsActive = true,
    string? MatchText = null,
    decimal AmountTolerance = 5m,
    int DateWindowDays = 5,
    int? SourceTransactionId = null);

public sealed record MarkTransactionRecurringRequest(
    string? Name,
    int? CategoryId,
    decimal? Amount,
    string Frequency,
    DateOnly NextDate,
    decimal AmountTolerance = 5m,
    int DateWindowDays = 5);

public sealed record RecurringOccurrenceDto(
    int Id,
    int RecurringItemId,
    string ItemName,
    string Kind,
    int AccountId,
    string AccountName,
    DateOnly DueDate,
    decimal ExpectedAmount,
    string Status,
    int? TransactionId,
    decimal? ActualAmount,
    string? Note);

public sealed record UpdateRecurringOccurrenceRequest(
    DateOnly DueDate,
    decimal ExpectedAmount,
    string Status,
    string? Note = null);

public sealed record RecurringSuggestionDto(
    string Name,
    string Kind,
    int AccountId,
    string AccountName,
    decimal Amount,
    string Frequency,
    DateOnly NextDate,
    int Occurrences,
    decimal Confidence);

public sealed record BudgetDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string IconKey,
    string ColorKey,
    decimal MonthlyAmount,
    bool RolloverEnabled,
    decimal RolloverIn,
    decimal AvailableAmount,
    decimal SpentAmount,
    decimal ScheduledAmount,
    decimal RemainingAfterScheduled,
    decimal RemainingAmount,
    decimal ProgressPercent);

public sealed record SaveBudgetRequest(
    int CategoryId,
    decimal MonthlyAmount,
    bool RolloverEnabled);

public sealed record AccountSafetyDto(
    int AccountId,
    string AccountName,
    string AccountType,
    decimal Balance,
    decimal DebtBalance,
    decimal? CreditLimit,
    decimal? AvailableCredit,
    decimal? CreditUtilizationPercent,
    decimal BufferAmount,
    decimal UpcomingBills,
    DateOnly HorizonDate,
    decimal SafeToSpend,
    decimal Shortfall);

public sealed record DashboardDto(
    string HouseholdName,
    decimal TotalBalance,
    decimal TotalAssets,
    decimal TotalDebt,
    decimal SafeToSpend,
    decimal TotalProtected,
    decimal UpcomingCommitments,
    decimal Shortfall,
    DateOnly? NextPayday,
    IReadOnlyList<AccountSafetyDto> Accounts,
    IReadOnlyList<TransactionDtoV2> RecentTransactions,
    GoalDto? PriorityGoal,
    IReadOnlyList<BudgetDto> BudgetWarnings,
    IReadOnlyList<string> Alerts);

public sealed record TrendPointDto(DateOnly Date, decimal Value);
public sealed record CategorySpendDto(int? CategoryId, string Name, string ColorKey, decimal Amount, decimal Percent);

public sealed record InsightsDto(
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalBalance,
    decimal Income,
    decimal Spending,
    decimal NetSavings,
    decimal SavingsRate,
    IReadOnlyList<TrendPointDto> BalanceTrend,
    IReadOnlyList<CategorySpendDto> CategorySpending,
    IReadOnlyList<TrendPointDto> IncomeTrend,
    IReadOnlyList<TrendPointDto> SpendingTrend,
    decimal GoalProgressPercent,
    decimal UncategorisedSpending);

public sealed record SearchResultDto(string Type, int Id, string Title, string Subtitle, string Route);
