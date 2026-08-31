using System.Text;
using financesApi.services;
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

    [Fact]
    public void PreviewKeepsValidOfxRowsWhenAnotherRowIsRejected()
    {
        const string content = """
            <OFX>
            <STMTTRN><DTPOSTED>20260830120000<TRNAMT>-12.34<NAME>Shop<FITID>one</STMTTRN>
            <STMTTRN><DTPOSTED>not-a-date<TRNAMT>-9.99<NAME>Broken<FITID>two</STMTTRN>
            </OFX>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = FinancialFileParserService.ParseRows(stream, "mixed.ofx");

        Assert.Equal("OFX", result.FileType);
        Assert.Equal(2, result.Rows.Count);
        Assert.NotNull(result.Rows[0].Transaction);
        Assert.Equal("OFX transaction 1", result.Rows[0].SourceLabel);
        Assert.Null(result.Rows[1].Transaction);
        Assert.Equal("invalid_date", result.Rows[1].ErrorCode);
    }

    [Fact]
    public void PreviewKeepsValidQifRowsAndReportsTheirRecordNumbers()
    {
        const string content = """
            !Type:Bank
            D30/08/2026
            T-4.20
            PCoffee
            ^
            D31/08/2026
            Tnot-money
            PBroken
            ^
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = FinancialFileParserService.ParseRows(stream, "mixed.qif");

        Assert.Equal(2, result.Rows.Count);
        Assert.NotNull(result.Rows[0].Transaction);
        Assert.Equal("QIF transaction 2", result.Rows[1].SourceLabel);
        Assert.Equal("invalid_amount", result.Rows[1].ErrorCode);
    }

    [Fact]
    public void PreviewDoesNotDiscardRepeatedStableIdentifiersBeforeClassification()
    {
        const string content = """
            <OFX>
            <STMTTRN><DTPOSTED>20260830120000<TRNAMT>-12.34<NAME>Shop<FITID>same</STMTTRN>
            <STMTTRN><DTPOSTED>20260830120000<TRNAMT>-12.34<NAME>Shop<FITID>same</STMTTRN>
            </OFX>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = FinancialFileParserService.ParseRows(stream, "duplicates.ofx");

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.NotNull(row.Transaction));
    }
}
