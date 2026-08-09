using Xunit;
using financesApi.models;
using financesApi.utilities;

namespace financesApi.Tests;

public sealed class TransactionFingerprintTests
{
    [Fact]
    public void StableBankIdWinsOverEditableDescriptionFields()
    {
        var first = new OfxTransactionDto { Date = new(2026, 8, 3), Amount = -78.21m, Payee = "E.ON", FitId = "ABC-123" };
        var renamed = new OfxTransactionDto { Date = new(2026, 8, 4), Amount = -99m, Payee = "E.ON NEXT LTD", FitId = "abc-123" };

        Assert.Equal(TransactionFingerprint.Build(first, 1), TransactionFingerprint.Build(renamed, 1));
    }

    [Fact]
    public void UnkeyedRowsAreNormalizedButPreserveRealMultiplicity()
    {
        var first = new QifTransactionDto { Date = new(2026, 8, 3), Amount = -12.50m, Payee = "  Coffee   Shop ", Memo = " CARD " };
        var equivalent = new QifTransactionDto { Date = new(2026, 8, 3), Amount = -12.50m, Payee = "coffee shop", Memo = "card" };

        Assert.Equal(TransactionFingerprint.Build(first, 1), TransactionFingerprint.Build(equivalent, 1));
        Assert.NotEqual(TransactionFingerprint.Build(first, 1), TransactionFingerprint.Build(first, 2));
    }

    [Fact]
    public void QifParserKeepsGenuineRepeatedRowsForOccurrenceDeduplication()
    {
        const string qif = "!Type:Bank\nD03/01/2099\nT-3.33\nPRepeated shop\nMsame purchase\n^\nD03/01/2099\nT-3.33\nPRepeated shop\nMsame purchase\n^\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(qif));

        var rows = QifParser.Parse(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Payee, rows[1].Payee);
        Assert.Equal(rows[0].Amount, rows[1].Amount);
    }
}
