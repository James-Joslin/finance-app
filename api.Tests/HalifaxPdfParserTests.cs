using System.Text;
using financesApi.models;
using financesApi.services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace financesApi.Tests;

public sealed class HalifaxPdfParserTests
{
    [Fact]
    public void ParsesMoneyDirectionsAndRowsContinuedAcrossPages()
    {
        var statement = BuildStatement();
        using var stream = new MemoryStream(statement);

        var transactions = FinancialFileParserService.Parse(stream, "halifax-july.pdf")
            .Cast<HalifaxPdfTransactionDto>().ToList();

        Assert.True(transactions.Count == 4, transactions.Count == 4
            ? null
            : $"Expected four transactions but found {transactions.Count}. Extracted PDF: {Describe(statement)}");
        Assert.Equal(362.94m, transactions[0].Amount);
        Assert.Equal("KINDER HOME CARE S", transactions[0].Payee);
        Assert.Equal("BP", transactions[0].TransactionCode);
        Assert.Equal(-200m, transactions[1].Amount);
        Assert.Equal("FPO", transactions[1].TransactionCode);
        Assert.Equal("THE JUICE PLUS COMPANY", transactions[2].Payee);
        Assert.Equal(-49.99m, transactions[2].Amount);
        Assert.Equal("DEB", transactions[2].TransactionCode);
        Assert.Equal(195.05m, transactions[3].Amount);
        Assert.All(transactions, item => Assert.StartsWith("halifax-pdf-", item.FitId));
        Assert.Equal(transactions.Count, transactions.Select(item => item.FitId).Distinct().Count());
    }

    [Fact]
    public void RejectsAFileWithAPdfExtensionButNoPdfSignature()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

        var error = Assert.Throws<InvalidDataException>(() => FinancialFileParserService.Parse(stream, "statement.pdf"));

        Assert.Contains("not a valid PDF", error.Message);
    }

    private static byte[] BuildStatement()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var first = builder.AddPage(PageSize.A4);
        AddHeader(first, font);
        AddRow(first, font, 690, "03 Jul 20", "KINDER HOME CARE S", "BP", "362.94", "", "868.85");
        AddRow(first, font, 665, "03 Jul 20", "BRIGET BOAKYE", "FPO", "", "200.00", "668.85");
        AddRow(first, font, 640, "13 Jul 20", "THE JUICE", "", "", "", "");
        first.AddText("(Continued on next page)", 9, new PdfPoint(50, 60), font);

        var second = builder.AddPage(PageSize.A4);
        AddHeader(second, font);
        AddRow(second, font, 690, "", "PLUS COMPANY", "DEB", "", "49.99", "618.86");
        AddRow(second, font, 665, "16 Jul 20", "C QUARTSIN", "TFR", "195.05", "", "813.91");
        second.AddText("Bank Statement Abbreviations & Meanings", 9, new PdfPoint(50, 80), font);

        return builder.Build();
    }

    private static string Describe(byte[] statement)
    {
        using var document = PdfDocument.Open(statement);
        return string.Join(" | ", document.GetPages().SelectMany(page =>
            page.GetWords(NearestNeighbourWordExtractor.Instance)
                .Select(word => $"p{page.Number}:{word.Text}@{word.BoundingBox.Left:0},{word.Letters[0].StartBaseLine.Y:0}")));
    }

    private static void AddHeader(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        page.AddText("HALIFAX", 14, new PdfPoint(50, 810), font);
        AddRow(page, font, 720, "Date", "Description", "Type", "Money In (£)", "Money Out (£)", "Balance (£)");
    }

    private static void AddRow(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, double y,
        string date, string description, string type, string moneyIn, string moneyOut, string balance)
    {
        Add(page, font, date, 50, y);
        Add(page, font, description, 120, y);
        Add(page, font, type, 270, y);
        Add(page, font, moneyIn, 324, y);
        Add(page, font, moneyOut, 415, y);
        Add(page, font, balance, 495, y);
    }

    private static void Add(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string value, double x, double y)
    {
        if (!string.IsNullOrWhiteSpace(value)) page.AddText(value, 9, new PdfPoint(x, y), font);
    }
}
