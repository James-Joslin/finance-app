namespace financesApi.utilities;

public sealed record SafetyResult(decimal SafeToSpend, decimal Shortfall);
public sealed record GoalAllocationResult(decimal Allocated, decimal RemainingPool);
public sealed record BudgetResult(decimal RolloverIn, decimal Available, decimal Remaining, decimal ProgressPercent);
public sealed record GoalPaceResult(int? DaysRemaining, decimal? Weekly, decimal? Monthly);

public static class FinanceMath
{
    public static SafetyResult CalculateSafety(decimal balance, decimal buffer, decimal upcomingBills)
    {
        var raw = balance - Math.Max(0, buffer) - Math.Max(0, upcomingBills);
        return new(Math.Max(0, raw), Math.Max(0, -raw));
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
}
