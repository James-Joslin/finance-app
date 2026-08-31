using System.Globalization;
using System.Text.RegularExpressions;
using financesApi.models;

namespace financesApi.utilities;

public static class OfxParser
{
    public static List<OfxTransactionDto> Parse(Stream ofxStream)
    {
        var rows = ParseRows(ofxStream);
        var rejected = rows.FirstOrDefault(row => row.Transaction is null);
        if (rejected is not null) throw new InvalidDataException(rejected.ErrorMessage);
        return rows.Select(row => (OfxTransactionDto)row.Transaction!).ToList();
    }

    public static IReadOnlyList<ParsedFinancialRow> ParseRows(Stream ofxStream)
    {
        var results = new List<ParsedFinancialRow>();
        using var reader = new StreamReader(ofxStream);
        var content = reader.ReadToEnd();

        var bodyStart = content.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0)
            throw new InvalidDataException("The OFX file is missing its <OFX> document marker.");

        var transactionMatches = Regex.Matches(
            content[bodyStart..], @"<STMTTRN>(.*?)</STMTTRN>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (transactionMatches.Count == 0)
            throw new InvalidDataException("The OFX file contains no recognisable transaction records.");

        var ordinal = 0;
        foreach (Match match in transactionMatches)
        {
            ordinal++;
            var block = match.Groups[1].Value;
            var rawDate = ExtractTag(block, "DTPOSTED");
            var rawAmount = ExtractTag(block, "TRNAMT");
            var payee = ExtractTag(block, "NAME");
            var memo = ExtractTag(block, "MEMO");
            var fitId = ExtractTag(block, "FITID");
            var transactionType = ExtractTag(block, "TRNTYPE");
            var label = $"OFX transaction {ordinal}";

            if (!TryExtractDate(rawDate, out var date))
            {
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "invalid_date", string.IsNullOrWhiteSpace(rawDate)
                        ? $"{label} has no posting date."
                        : $"{label} has an invalid posting date."));
                continue;
            }
            if (!TryExtractDecimal(rawAmount, out var amount))
            {
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "invalid_amount", $"{label} has an invalid amount."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(payee) && string.IsNullOrWhiteSpace(memo))
            {
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "missing_description", $"{label} has no payee or memo."));
                continue;
            }

            results.Add(new(ordinal, label, new OfxTransactionDto
            {
                Date = date,
                Amount = amount,
                Payee = payee,
                Memo = memo,
                FitId = fitId,
                TransType = transactionType,
            }, rawDate, rawAmount, payee, memo));
        }
        return results;
    }

    private static string ExtractTag(string block, string tag)
    {
        var match = Regex.Match(block, $"<{tag}>(.*?)\\s*(?=<|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static bool TryExtractDate(string raw, out DateTime date)
    {
        date = default;
        if (raw.Length < 8) return false;
        var cleaned = raw.Length >= 14 ? raw[..14] : raw[..8];
        return DateTime.TryParseExact(cleaned, ["yyyyMMddHHmmss", "yyyyMMdd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryExtractDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
}
