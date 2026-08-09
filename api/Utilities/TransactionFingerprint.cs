using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using financesApi.models;

namespace financesApi.utilities;

public static class TransactionFingerprint
{
    public static string Build(TransactionDto transaction, int occurrence)
    {
        var fitId = GetFitId(transaction);
        var identity = !string.IsNullOrWhiteSpace(fitId)
            ? $"id|{Normalize(fitId)}"
            : $"row|{BuildBase(transaction)}|{Math.Max(1, occurrence)}";
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public static string BuildBase(TransactionDto transaction)
    {
        var transactionType = transaction switch
        {
            OfxTransactionDto ofx => ofx.TransType,
            HalifaxPdfTransactionDto pdf => pdf.TransactionCode,
            _ => null,
        };
        var checkNumber = transaction is QifTransactionDto qif ? qif.CheckNumber : null;
        return string.Join('|',
            transaction.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Normalize(transaction.Payee),
            Normalize(transaction.Memo),
            Normalize(transactionType),
            Normalize(checkNumber));
    }

    public static string? GetFitId(TransactionDto transaction) => transaction switch
    {
        OfxTransactionDto ofx => Clean(ofx.FitId),
        HalifaxPdfTransactionDto pdf => Clean(pdf.FitId),
        _ => null,
    };

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
