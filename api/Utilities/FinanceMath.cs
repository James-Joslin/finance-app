namespace financesApi.utilities;

public sealed record SafetyResult(decimal SafeToSpend, decimal Shortfall);
public sealed record CreditPositionResult(decimal DebtBalance, decimal CreditBalance, decimal? AvailableCredit, decimal? UtilizationPercent);
public sealed record HouseholdPositionResult(decimal Assets, decimal Debt, decimal NetPosition);
public sealed record GoalAllocationResult(decimal Allocated, decimal RemainingPool);
public sealed record BudgetResult(decimal RolloverIn, decimal Available, decimal Remaining, decimal ProgressPercent);
public sealed record GoalPaceResult(int? DaysRemaining, decimal? Weekly, decimal? Monthly);
public sealed record ImportBalanceEntry(int Ordinal, DateTime Date, decimal Amount, bool Included);

public static class FinanceMath
{
    public static SafetyResult CalculateSafety(decimal balance, decimal buffer, decimal upcomingBills)
    {
        var raw = balance - Math.Max(0, buffer) - Math.Max(0, upcomingBills);
        return new(Math.Max(0, raw), Math.Max(0, -raw));
    }

    public static CreditPositionResult CalculateCreditPosition(decimal balance, decimal? creditLimit)
    {
        var debt = Math.Max(0, -balance);
        var creditBalance = Math.Max(0, balance);
        var validLimit = creditLimit is > 0 ? creditLimit : null;
        decimal? available = validLimit.HasValue ? Math.Max(0, validLimit.Value - debt) : null;
        decimal? utilization = validLimit.HasValue ? Math.Round(debt / validLimit.Value * 100, 1) : null;
        return new(debt, creditBalance, available, utilization);
    }

    public static HouseholdPositionResult CalculateHouseholdPosition(IEnumerable<decimal> balances)
    {
        var assets = 0m;
        var debt = 0m;
        foreach (var balance in balances)
        {
            assets += Math.Max(0, balance);
            debt += Math.Max(0, -balance);
        }
        return new(assets, debt, assets - debt);
    }

    public static GoalAllocationResult AllocateGoal(decimal availablePool, decimal targetAmount)
    {
        var safePool = Math.Max(0, availablePool);
        var target = Math.Max(0, targetAmount);
        var allocated = Math.Min(safePool, target);
        return new(allocated, safePool - allocated);
    }

    public static BudgetResult CalculateBudget(decimal baseAmount, bool rolloverEnabled, decimal priorRemaining, decimal spent)
    {
        var rollover = rolloverEnabled ? Math.Max(0, priorRemaining) : 0;
        var available = Math.Max(0, baseAmount) + rollover;
        var remaining = available - Math.Max(0, spent);
        var progress = available == 0 ? 0 : Math.Round(Math.Max(0, spent) / available * 100, 1);
        return new(rollover, available, remaining, progress);
    }

    public static GoalPaceResult CalculateGoalPace(decimal remaining, DateOnly? targetDate, DateOnly today)
    {
        if (!targetDate.HasValue) return new(null, null, null);
        var days = targetDate.Value.DayNumber - today.DayNumber;
        if (remaining <= 0) return new(days, 0, 0);
        var divisor = Math.Max(1, days);
        return new(days, Math.Round(remaining / divisor * 7, 2), Math.Round(remaining / divisor * 30.4375m, 2));
    }

    public static IReadOnlyDictionary<int, decimal> CalculateImportBalances(
        decimal startingBalance, IEnumerable<ImportBalanceEntry> entries)
    {
        var sourceRows = entries.ToList();
        if (sourceRows.Count == 0) return new Dictionary<int, decimal>();

        var sourceDescending = sourceRows[0].Date > sourceRows[^1].Date;
        var chronologicalRows = sourceDescending
            ? sourceRows.OrderBy(row => row.Date).ThenByDescending(row => row.Ordinal)
            : sourceRows.OrderBy(row => row.Date).ThenBy(row => row.Ordinal);

        var balances = new Dictionary<int, decimal>(sourceRows.Count);
        var balance = startingBalance;
        foreach (var row in chronologicalRows)
        {
            if (row.Included) balance += row.Amount;
            balances[row.Ordinal] = balance;
        }
        return balances;
    }
}
