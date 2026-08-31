using System.Globalization;
using financesApi.models;

namespace financesApi.utilities;

public static class QifParser
{
    public static List<TransactionDto> Parse(Stream qifStream)
    {
        var rows = ParseRows(qifStream);
        var rejected = rows.FirstOrDefault(row => row.Transaction is null);
        if (rejected is not null) throw new InvalidDataException(rejected.ErrorMessage);
        return rows.Select(row => row.Transaction!).ToList();
    }

    public static IReadOnlyList<ParsedFinancialRow> ParseRows(Stream qifStream)
    {
        var results = new List<ParsedFinancialRow>();
        using var reader = new StreamReader(qifStream);
        string? line;
        string? rawDate = null;
        string? rawAmount = null;
        string? payee = null;
        string? memo = null;
        string? category = null;
        string? checkNumber = null;
        var hasRecord = false;
        var ordinal = 0;

        void Finalise()
        {
            if (!hasRecord) return;
            ordinal++;
            var label = $"QIF transaction {ordinal}";
            if (!TryParseQifDate(rawDate, out var date))
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "invalid_date", string.IsNullOrWhiteSpace(rawDate)
                        ? $"{label} has no date."
                        : $"{label} has an invalid date."));
            else if (!TryParseQifAmount(rawAmount, out var amount))
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "invalid_amount", $"{label} has an invalid amount."));
            else if (amount == 0)
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "zero_amount", $"{label} has a zero amount."));
            else if (string.IsNullOrWhiteSpace(payee) && string.IsNullOrWhiteSpace(memo))
                results.Add(new(ordinal, label, null, rawDate, rawAmount, payee, memo,
                    "missing_description", $"{label} has no payee or memo."));
            else
                results.Add(new(ordinal, label, new QifTransactionDto
                {
                    Date = date,
                    Amount = amount,
                    Payee = payee ?? memo ?? string.Empty,
                    Memo = memo,
                    Category = category,
                    CheckNumber = checkNumber,
                }, rawDate, rawAmount, payee, memo));

            rawDate = rawAmount = payee = memo = category = checkNumber = null;
            hasRecord = false;
        }

        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.StartsWith("!Type:", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(line)) continue;
            if (line == "^")
            {
                Finalise();
                continue;
            }
            if (line.Length <= 1) continue;
            hasRecord = true;
            var value = line[1..].Trim();
            switch (line[0])
            {
                case 'D': rawDate = value; break;
                case 'T': rawAmount = value; break;
                case 'P': payee = value; break;
                case 'M': memo = value; break;
                case 'N': checkNumber = value; break;
                case 'L': category = value; break;
            }
        }

        Finalise();
        return results;
    }

    private static bool TryParseQifDate(string? dateText, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(dateText)) return false;
        string[] formats = ["dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d/M/yyyy", "M/d/yyyy"];
        return DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date)
            || DateTime.TryParse(dateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseQifAmount(string? amountText, out decimal amount)
    {
        amount = default;
        if (string.IsNullOrWhiteSpace(amountText)) return false;
        var cleaned = amountText.Replace("£", "")
            .Replace("$", "")
            .Replace("€", "")
            .Replace(",", "")
            .Replace(" ", "")
            .Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
    }
}
