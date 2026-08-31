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
    public void BudgetRolloverCanBeCalculatedAcrossSkippedMonths()
    {
        var january = FinanceMath.CalculateBudget(500m, true, 0m, 300m);
        var february = FinanceMath.CalculateBudget(500m, true, january.Remaining, 450m);
        var march = FinanceMath.CalculateBudget(500m, true, february.Remaining, 100m);

        Assert.Equal(200m, february.RolloverIn);
        Assert.Equal(250m, march.RolloverIn);
        Assert.Equal(650m, march.Remaining);
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

    [Fact]
    public void ImportBalancesReverseNewestFirstRowsIntoChronologicalOrder()
    {
        var entries = new[]
        {
            new ImportBalanceEntry(1, new DateTime(2026, 8, 31), -20.19m, true),
            new ImportBalanceEntry(2, new DateTime(2026, 8, 10), -8.40m, true),
            new ImportBalanceEntry(3, new DateTime(2026, 8, 10), -39.80m, true),
            new ImportBalanceEntry(4, new DateTime(2026, 8, 10), 72.80m, true),
            new ImportBalanceEntry(5, new DateTime(2026, 8, 10), -27.50m, true),
        };

        var balances = FinanceMath.CalculateImportBalances(206.99m, entries);

        Assert.Equal(179.49m, balances[5]);
        Assert.Equal(252.29m, balances[4]);
        Assert.Equal(212.49m, balances[3]);
        Assert.Equal(204.09m, balances[2]);
        Assert.Equal(183.90m, balances[1]);
    }
}
