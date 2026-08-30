using System.Text;
using financesApi.utilities;
using Xunit;

namespace financesApi.Tests;

public sealed class FinancialFileValidationTests
{
    [Fact]
    public void OfxRejectsAnInvalidPostingDate()
    {
        const string content = "<OFX><STMTTRN><DTPOSTED>not-a-date<TRNAMT>-12.34<NAME>Shop</STMTTRN></OFX>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var exception = Assert.Throws<InvalidDataException>(() => OfxParser.Parse(stream));

        Assert.Contains("posting date", exception.Message);
    }

    [Fact]
    public void OfxRejectsAnInvalidAmount()
    {
        const string content = "<OFX><STMTTRN><DTPOSTED>20260830120000<TRNAMT>twelve<NAME>Shop</STMTTRN></OFX>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var exception = Assert.Throws<InvalidDataException>(() => OfxParser.Parse(stream));

        Assert.Contains("invalid amount", exception.Message);
    }

    [Fact]
    public void QifRejectsAnInvalidAmount()
    {
        const string content = "!Type:Bank\nD30/08/2026\nTnot-money\nPShop\n^\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var exception = Assert.Throws<InvalidDataException>(() => QifParser.Parse(stream));

        Assert.Contains("invalid amount", exception.Message);
    }
}
