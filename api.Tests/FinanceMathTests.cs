using financesApi.utilities;
using Xunit;

namespace financesApi.Tests;

public sealed class FinanceMathTests
{
    [Fact]
    public void SafetyProtectsBufferAndBills()
    {
        var result = FinanceMath.CalculateSafety(2400m, 500m, 700m);
        Assert.Equal(1200m, result.SafeToSpend);
        Assert.Equal(0m, result.Shortfall);
    }

    [Fact]
    public void SafetyReportsShortfallWithoutNegativeSpend()
    {
        var result = FinanceMath.CalculateSafety(600m, 500m, 250m);
        Assert.Equal(0m, result.SafeToSpend);
        Assert.Equal(150m, result.Shortfall);
    }

    [Fact]
    public void GoalWaterfallNeverCountsTheSameMoneyTwice()
    {
        var first = FinanceMath.AllocateGoal(3000m, 2000m);
        var second = FinanceMath.AllocateGoal(first.RemainingPool, 4000m);
        Assert.Equal(2000m, first.Allocated);
        Assert.Equal(1000m, second.Allocated);
        Assert.Equal(0m, second.RemainingPool);
    }

    [Fact]
    public void BudgetOnlyRollsPositiveUnusedMoney()
    {
        var positive = FinanceMath.CalculateBudget(500m, true, 80m, 200m);
        var overspent = FinanceMath.CalculateBudget(500m, true, -60m, 100m);
        Assert.Equal(80m, positive.RolloverIn);
        Assert.Equal(580m, positive.Available);
        Assert.Equal(0m, overspent.RolloverIn);
        Assert.Equal(500m, overspent.Available);
    }

    [Fact]
    public void GoalPaceHandlesMissingAndPastDates()
    {
        var today = new DateOnly(2026, 8, 8);
        var flexible = FinanceMath.CalculateGoalPace(1200m, null, today);
        var overdue = FinanceMath.CalculateGoalPace(1200m, today.AddDays(-3), today);
        Assert.Null(flexible.Monthly);
        Assert.Equal(-3, overdue.DaysRemaining);
        Assert.Equal(36525m, overdue.Monthly);
    }

    [Fact]
    public void CreditPositionTreatsNegativeBalanceAsDebt()
    {
        var result = FinanceMath.CalculateCreditPosition(-1200m, 4000m);
        Assert.Equal(1200m, result.DebtBalance);
        Assert.Equal(2800m, result.AvailableCredit);
        Assert.Equal(30m, result.UtilizationPercent);
    }

    [Fact]
    public void HouseholdPositionSubtractsDebtFromAssets()
    {
        var result = FinanceMath.CalculateHouseholdPosition(new[] { 3000m, 800m, -1200m });
        Assert.Equal(3800m, result.Assets);
        Assert.Equal(1200m, result.Debt);
        Assert.Equal(2600m, result.NetPosition);
    }
}
