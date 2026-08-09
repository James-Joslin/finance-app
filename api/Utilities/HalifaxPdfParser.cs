using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using financesApi.models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace financesApi.utilities;

public static partial class HalifaxPdfParser
{
    private const int MaximumPages = 100;
    private const double LineTolerance = 2.5;

    public static List<TransactionDto> Parse(Stream pdfStream)
    {
        try
        {
            return ParseDocument(pdfStream);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("The PDF could not be read. Check that it is a valid, unlocked Halifax statement.", exception);
        }
    }

    private static List<TransactionDto> ParseDocument(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream, new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
        });
        if (document.NumberOfPages > MaximumPages)
            throw new InvalidDataException($"PDF statements are limited to {MaximumPages} pages.");

        var statementRows = new List<StatementRow>();
        var isHalifax = false;
        var readableWords = 0;
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            readableWords += words.Count;
            var pageText = string.Join(' ', words.Select(word => word.Text));
            isHalifax |= pageText.Contains("HALIFAX", StringComparison.OrdinalIgnoreCase)
                || pageText.Contains("Bank of Scotland", StringComparison.OrdinalIgnoreCase);
            statementRows.AddRange(ExtractRows(page.Number, page.Width, words));
        }

        if (readableWords == 0)
            throw new InvalidDataException("The PDF contains no readable text. Scanned statements need OCR before they can be imported.");
        if (!isHalifax)
            throw new InvalidDataException("This PDF does not look like a Halifax bank statement.");

        return ParseStatementRows(statementRows).Cast<TransactionDto>().ToList();
    }

    internal static List<HalifaxPdfTransactionDto> ParseStatementRows(IEnumerable<StatementRow> rows)
    {
        var results = new List<HalifaxPdfTransactionDto>();
        PendingTransaction? pending = null;
        foreach (var row in rows)
        {
            if (TryParseDate(row.Date, out var date))
            {
                Finalise(pending, results);
                pending = new(date, Clean(row.Description), CleanCode(row.Type), row.MoneyIn, row.MoneyOut, row.Balance, row.Page);
                continue;
            }

            if (pending is null || IsIgnoredText(row.Description)) continue;
            if (!string.IsNullOrWhiteSpace(row.Description))
                pending.Description = string.Join(' ', new[] { pending.Description, Clean(row.Description) }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(pending.Type) && !string.IsNullOrWhiteSpace(row.Type)) pending.Type = CleanCode(row.Type);
            pending.MoneyIn ??= row.MoneyIn;
            pending.MoneyOut ??= row.MoneyOut;
            pending.Balance ??= row.Balance;
        }
        Finalise(pending, results);
        return results;
    }

    private static IEnumerable<StatementRow> ExtractRows(int pageNumber, double pageWidth, IReadOnlyList<Word> words)
    {
        var lines = GroupIntoLines(words);
        var header = lines.FirstOrDefault(line =>
            line.Text.Contains("Money In", StringComparison.OrdinalIgnoreCase)
            && line.Text.Contains("Money Out", StringComparison.OrdinalIgnoreCase)
            && line.Text.Contains("Balance", StringComparison.OrdinalIgnoreCase));
        if (header is null) yield break;

        foreach (var line in lines.Where(line => line.Y < header.Y - LineTolerance).OrderByDescending(line => line.Y))
        {
            if (IsFooter(line.Text)) break;
            var columns = new StringBuilder[6];
            for (var index = 0; index < columns.Length; index++) columns[index] = new();
            foreach (var word in line.Words.OrderBy(word => word.BoundingBox.Left))
            {
                var ratio = word.BoundingBox.Left / pageWidth;
                var column = ratio switch
                {
                    < .18 => 0,
                    < .44 => 1,
                    < .53 => 2,
                    < .68 => 3,
                    < .82 => 4,
                    _ => 5,
                };
                if (columns[column].Length > 0) columns[column].Append(' ');
                columns[column].Append(word.Text);
            }

            var date = columns[0].ToString();
            var description = columns[1].ToString();
            var type = columns[2].ToString();
            if (!DateStartRegex().IsMatch(date) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(type)) continue;
            yield return new(pageNumber, date, description, type,
                ParseMoney(columns[3].ToString()), ParseMoney(columns[4].ToString()), ParseMoney(columns[5].ToString()));
        }
    }

    private static List<ExtractedLine> GroupIntoLines(IReadOnlyList<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(Baseline).ThenBy(word => word.BoundingBox.Left))
        {
            var line = lines.FirstOrDefault(candidate => Math.Abs(Baseline(candidate[0]) - Baseline(word)) <= LineTolerance);
            if (line is null)
            {
                line = [];
                lines.Add(line);
            }
            line.Add(word);
        }
        return lines.Select(line => new ExtractedLine(
            line.Average(Baseline),
            Clean(string.Join(' ', line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))),
            line)).OrderByDescending(line => line.Y).ToList();
    }

    private static double Baseline(Word word) => word.Letters.Count > 0
        ? word.Letters[0].StartBaseLine.Y
        : word.BoundingBox.Bottom;

    private static void Finalise(PendingTransaction? pending, ICollection<HalifaxPdfTransactionDto> results)
    {
        if (pending is null) return;
        var moneyIn = pending.MoneyIn ?? 0;
        var moneyOut = pending.MoneyOut ?? 0;
        if (moneyIn > 0 && moneyOut > 0)
            throw new InvalidDataException($"Halifax row on {pending.Date:dd MMM yyyy} has both Money In and Money Out values.");
        if (moneyIn <= 0 && moneyOut <= 0)
            throw new InvalidDataException($"Could not read the amount for a Halifax transaction on {pending.Date:dd MMM yyyy}.");
        if (string.IsNullOrWhiteSpace(pending.Description))
            throw new InvalidDataException($"Could not read the description for a Halifax transaction on {pending.Date:dd MMM yyyy}.");

        var amount = moneyIn > 0 ? moneyIn : -moneyOut;
        var balance = pending.Balance ?? 0;
        var code = CleanCode(pending.Type);
        var hashInput = FormattableString.Invariant($"{pending.Date:yyyy-MM-dd}|{amount:0.00}|{pending.Description}|{code}|{balance:0.00}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant()[..32];
        results.Add(new HalifaxPdfTransactionDto
        {
            Date = pending.Date,
            Amount = amount,
            Payee = pending.Description,
            Memo = null,
            FitId = $"halifax-pdf-{hash}",
            TransactionCode = code,
            StatementBalance = balance,
            Category = code == "TFR" ? "Transfers" : moneyIn > 0 ? "Income" : null,
        });
    }

    private static bool TryParseDate(string value, out DateTime date) => DateTime.TryParseExact(
        Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim(),
        ["dd MMM yy", "d MMM yy", "dd MMM yyyy", "d MMM yyyy"],
        CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out date);

    private static decimal? ParseMoney(string value)
    {
        var cleaned = (value ?? string.Empty).Replace("£", "").Replace(",", "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return null;
        var negative = cleaned.StartsWith('(') && cleaned.EndsWith(')');
        cleaned = cleaned.Trim('(', ')');
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) ? (negative ? -amount : amount) : null;
    }

    private static string Clean(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    private static string CleanCode(string value) => Regex.Replace(Clean(value).ToUpperInvariant(), "[^A-Z]", string.Empty);
    private static bool IsFooter(string text) =>
        text.Contains("Continued on next page", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("If you think something", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Bank Statement Abbreviations", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Additional Abbreviations", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("Halifax is a division", StringComparison.OrdinalIgnoreCase);
    private static bool IsIgnoredText(string text) => string.IsNullOrWhiteSpace(text) || IsFooter(text);

    [GeneratedRegex(@"^\s*\d{1,2}\s+[A-Za-z]{3}\s+\d{2,4}\b")]
    private static partial Regex DateStartRegex();

    internal sealed record StatementRow(int Page, string Date, string Description, string Type,
        decimal? MoneyIn, decimal? MoneyOut, decimal? Balance);
    private sealed record ExtractedLine(double Y, string Text, IReadOnlyList<Word> Words);
    private sealed class PendingTransaction(DateTime date, string description, string type,
        decimal? moneyIn, decimal? moneyOut, decimal? balance, int page)
    {
        public DateTime Date { get; } = date;
        public string Description { get; set; } = description;
        public string Type { get; set; } = type;
        public decimal? MoneyIn { get; set; } = moneyIn;
        public decimal? MoneyOut { get; set; } = moneyOut;
        public decimal? Balance { get; set; } = balance;
        public int Page { get; } = page;
    }
}
