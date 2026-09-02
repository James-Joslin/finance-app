using financesApi.utilities;
using Xunit;

namespace financesApi.Tests;

public sealed class ReconciliationMathTests
{
    [Fact]
    public void MatchingOpeningAndClosingBalancesCanClose()
    {
        var result = ReconciliationMath.Calculate(100m, 100m, 75m, -25m);

        Assert.Equal(0m, result.OpeningDiscrepancy);
        Assert.Equal(75m, result.ClearedBalance);
        Assert.Equal(0m, result.ClosingDiscrepancy);
        Assert.True(result.CanClose);
    }

    [Fact]
    public void OpeningDiscrepancyBlocksClose()
    {
        var result = ReconciliationMath.Calculate(100m, 95m, 70m, -25m);

        Assert.Equal(-5m, result.OpeningDiscrepancy);
        Assert.False(result.CanClose);
    }

    [Fact]
    public void PositiveAndNegativeClosingDifferencesProduceSignedAdjustments()
    {
        Assert.Equal(10m, ReconciliationMath.AdjustmentAmount(110m, 100m));
        Assert.Equal(-10m, ReconciliationMath.AdjustmentAmount(90m, 100m));
    }
}
