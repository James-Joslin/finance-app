namespace financesApi.utilities;

public sealed record ReconciliationCalculation(
    decimal ExpectedOpeningBalance,
    decimal OpeningDiscrepancy,
    decimal ClearedBalance,
    decimal ClosingDiscrepancy,
    bool CanClose);

public static class ReconciliationMath
{
    public static ReconciliationCalculation Calculate(
        decimal expectedOpeningBalance,
        decimal statementOpeningBalance,
        decimal statementClosingBalance,
        decimal clearedActivity)
    {
        var openingDiscrepancy = statementOpeningBalance - expectedOpeningBalance;
        var clearedBalance = statementOpeningBalance + clearedActivity;
        var closingDiscrepancy = statementClosingBalance - clearedBalance;
        return new(
            expectedOpeningBalance,
            openingDiscrepancy,
            clearedBalance,
            closingDiscrepancy,
            openingDiscrepancy == 0m && closingDiscrepancy == 0m);
    }

    public static decimal AdjustmentAmount(decimal statementClosingBalance, decimal clearedBalance) =>
        statementClosingBalance - clearedBalance;
}
