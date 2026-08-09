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
    private const double LineTolerance = 8;

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
        var maskedLayoutDiagnostics = new List<string>();
        var isHalifax = false;
        var readableWords = 0;
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            readableWords += words.Count;
            var pageText = string.Join(' ', words.Select(word => word.Text));
            isHalifax |= pageText.Contains("HALIFAX", StringComparison.OrdinalIgnoreCase)
                || pageText.Contains("Bank of Scotland", StringComparison.OrdinalIgnoreCase);
            var pageRows = ExtractRows(page.Number, page.Width, words).ToList();
            maskedLayoutDiagnostics.AddRange(DescribeMaskedTransactionLayout(page.Number, words));
            var datedRows = pageRows.Count(row => TryParseDate(row.Date, out _));
            var amountRows = pageRows.Count(row => row.MoneyIn.HasValue || row.MoneyOut.HasValue);
            Console.WriteLine($"Halifax PDF page {page.Number}: extracted {words.Count} words and {pageRows.Count} candidate transaction rows ({datedRows} dated, {amountRows} with an amount).");
            statementRows.AddRange(pageRows);
        }

        if (readableWords == 0)
            throw new InvalidDataException("The PDF contains no readable text. Scanned statements need OCR before they can be imported.");
        if (!isHalifax)
            throw new InvalidDataException("This PDF does not look like a Halifax bank statement.");

        var transactions = ParseStatementRows(statementRows).Cast<TransactionDto>().ToList();
        if (transactions.Count == 0)
        {
            Console.WriteLine("Halifax PDF parsing failed; masked transaction-table layout follows. L/D/S are letter, digit, and symbol counts only.");
            foreach (var diagnostic in maskedLayoutDiagnostics) Console.WriteLine(diagnostic);
            throw new InvalidDataException(
                $"The Halifax PDF was readable, but no transaction rows were recognised across {document.NumberOfPages} page(s). " +
                "Check that this is a current-account statement rather than a statement summary or scanned document.");
        }
        return transactions;
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
        var header = lines.FirstOrDefault(line => IsColumnHeader(line.Text));
        var transactionHeading = lines.FirstOrDefault(line =>
            NormaliseLabel(line.Text).Contains("yourtransactions", StringComparison.Ordinal));
        var tableTop = header?.Y ?? transactionHeading?.Y;
        var layout = BuildColumnLayout(lines, header, pageWidth);
        Console.WriteLine($"Halifax PDF page {pageNumber}: table header {(header is null ? "not found" : "found")}; using {layout.Source} columns.");

        if (header is not null && layout.Source == "header-derived")
        {
            foreach (var row in ExtractHeaderMatrixRows(pageNumber, words, lines, header, layout)) yield return row;
            yield break;
        }

        var tableLines = tableTop.HasValue
            ? lines.Where(line => line.Y < tableTop.Value - LineTolerance)
            : lines;
        foreach (var line in tableLines.OrderByDescending(line => line.Y))
        {
            if (IsFooter(line.Text)) break;
            if (IsColumnHeader(line.Text)) continue;
            var columns = new StringBuilder[6];
            for (var index = 0; index < columns.Length; index++) columns[index] = new();
            var orderedWords = line.Words.OrderBy(word => word.BoundingBox.Left).ToList();
            var dateWordCount = LeadingDateWordCount(orderedWords, out var leadingDate);
            if (dateWordCount > 0) columns[0].Append(leadingDate);
            foreach (var word in orderedWords.Skip(dateWordCount))
            {
                var column = layout.ColumnFor(word.BoundingBox.Left);
                if (columns[column].Length > 0) columns[column].Append(' ');
                columns[column].Append(word.Text);
            }

            var date = columns[0].ToString();
            var description = columns[1].ToString();
            var type = columns[2].ToString();
            if (!TryParseDate(date, out _) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(type)) continue;
            yield return new(pageNumber, date, description, type,
                ParseMoney(columns[3].ToString()), ParseMoney(columns[4].ToString()), ParseMoney(columns[5].ToString()));
        }
    }

    private static IEnumerable<StatementRow> ExtractHeaderMatrixRows(int pageNumber, IReadOnlyList<Word> words,
        IReadOnlyList<ExtractedLine> lines, ExtractedLine header, ColumnLayout layout)
    {
        var headerBottom = lines.Where(line => Math.Abs(line.Y - header.Y) <= 16 && IsColumnHeader(line.Text))
            .Select(line => line.Y).DefaultIfEmpty(header.Y).Min();
        var footer = lines.FirstOrDefault(line => line.Y < header.Y && IsFooter(line.Text));
        var bottom = footer?.Y ?? double.NegativeInfinity;
        var repeatedHeaderWords = lines.Where(line => IsColumnHeader(line.Text))
            .SelectMany(line => line.Words).ToHashSet();
        var tableWords = words.Where(word =>
                Baseline(word) < headerBottom - 1 && Baseline(word) > bottom + 1
                && !repeatedHeaderWords.Contains(word))
            .ToList();

        var dateAnchors = GroupIntoLines(tableWords.Where(word => layout.ColumnFor(word.BoundingBox.Left) == 0).ToList(), 4)
            .Select(line => new { Line = line, Parsed = TryParseDate(line.Text, out var date), Date = date })
            .Where(item => item.Parsed)
            .Select(item => new DateAnchor(item.Date, item.Line.Text, item.Line.Y))
            .OrderByDescending(item => item.Y)
            .ToList();

        if (dateAnchors.Count == 0) yield break;
        var rowGap = dateAnchors.Zip(dateAnchors.Skip(1), (upper, lower) => upper.Y - lower.Y)
            .Where(gap => gap > 0).DefaultIfEmpty(40).Min();
        var halfGap = Math.Clamp(rowGap / 2, 12, 30);
        var firstRowTop = Math.Min(headerBottom - 1, dateAnchors[0].Y + halfGap);
        var continuation = BuildMatrixCells(tableWords, layout, headerBottom - 1, firstRowTop);
        if (HasTransactionContent(continuation))
            yield return BuildStatementRow(pageNumber, string.Empty, continuation);

        for (var rowIndex = 0; rowIndex < dateAnchors.Count; rowIndex++)
        {
            var anchor = dateAnchors[rowIndex];
            var rowTop = rowIndex == 0 ? firstRowTop : (dateAnchors[rowIndex - 1].Y + anchor.Y) / 2;
            var rowBottom = rowIndex == dateAnchors.Count - 1 ? bottom : (anchor.Y + dateAnchors[rowIndex + 1].Y) / 2;
            var cells = BuildMatrixCells(tableWords, layout, rowTop, rowBottom);
            var row = BuildStatementRow(pageNumber, anchor.Text, cells);
            Console.WriteLine($"Halifax matrix p{pageNumber}: cell lengths [{string.Join(',', cells.Select(cell => cell.Length))}], " +
                $"money parsed [{row.MoneyIn.HasValue},{row.MoneyOut.HasValue},{row.Balance.HasValue}].");
            yield return row;
        }
    }

    private static StringBuilder[] BuildMatrixCells(IEnumerable<Word> words, ColumnLayout layout, double top, double bottom)
    {
        var cells = new StringBuilder[6];
        for (var index = 0; index < cells.Length; index++) cells[index] = new();
        foreach (var word in words.Where(word => Baseline(word) < top && Baseline(word) >= bottom)
                     .OrderByDescending(Baseline).ThenBy(word => word.BoundingBox.Left))
        {
            var column = layout.ColumnFor(word.BoundingBox.Left);
            if (layout.Source == "header-derived" && column >= 2 && ParseMoney(word.Text).HasValue)
                column = layout.NumericColumnFor(word.BoundingBox.Left);
            if (cells[column].Length > 0) cells[column].Append(' ');
            cells[column].Append(word.Text);
        }
        return cells;
    }

    private static bool HasTransactionContent(IReadOnlyList<StringBuilder> cells) =>
        cells.Skip(1).Any(cell => cell.Length > 0);

    private static StatementRow BuildStatementRow(int pageNumber, string date, IReadOnlyList<StringBuilder> cells) =>
        new(pageNumber, date, cells[1].ToString(), cells[2].ToString(),
            ParseMoney(cells[3].ToString()), ParseMoney(cells[4].ToString()), ParseMoney(cells[5].ToString()));

    private static ColumnLayout BuildColumnLayout(IReadOnlyList<ExtractedLine> lines, ExtractedLine? header, double pageWidth)
    {
        var fallback = new ColumnLayout([pageWidth * .18, pageWidth * .44, pageWidth * .53, pageWidth * .68, pageWidth * .82], [], "fallback");
        if (header is null) return fallback;

        var words = lines.Where(line => Math.Abs(line.Y - header.Y) <= 16)
            .SelectMany(line => line.Words).OrderBy(word => word.BoundingBox.Left).ToList();
        double? FindAnchor(string label) => words.FirstOrDefault(word => NormaliseLabel(word.Text) == label)?.BoundingBox.Left;
        var moneyIn = words.FirstOrDefault(word => NormaliseLabel(word.Text).Contains("moneyin", StringComparison.Ordinal))?.BoundingBox.Left;
        var moneyOut = words.FirstOrDefault(word => NormaliseLabel(word.Text).Contains("moneyout", StringComparison.Ordinal))?.BoundingBox.Left;
        var moneyWords = words.Where(word => NormaliseLabel(word.Text) == "money").Select(word => word.BoundingBox.Left).ToList();
        if (moneyIn is null && moneyWords.Count > 0) moneyIn = moneyWords[0];
        if (moneyOut is null && moneyWords.Count > 1) moneyOut = moneyWords[1];

        var anchors = new[] { FindAnchor("date"), FindAnchor("description"), FindAnchor("type"), moneyIn, moneyOut, FindAnchor("balance") };
        if (anchors.Any(value => value is null)) return fallback;
        var positions = anchors.Select(value => value!.Value).ToArray();
        if (positions.Zip(positions.Skip(1), (left, right) => left < right).Any(inOrder => !inOrder)) return fallback;

        return new ColumnLayout(positions.Skip(1).ToArray(), positions, "header-derived");
    }

    private static int LeadingDateWordCount(IReadOnlyList<Word> words, out string dateText)
    {
        dateText = string.Empty;
        for (var count = 1; count <= Math.Min(3, words.Count); count++)
        {
            var candidate = string.Join(' ', words.Take(count).Select(word => word.Text));
            if (!TryParseDate(candidate, out _)) continue;
            dateText = candidate;
            return count;
        }
        return 0;
    }

    private static List<ExtractedLine> GroupIntoLines(IReadOnlyList<Word> words, double tolerance = LineTolerance)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(Baseline).ThenBy(word => word.BoundingBox.Left))
        {
            var line = lines.FirstOrDefault(candidate => Math.Abs(Baseline(candidate[0]) - Baseline(word)) <= tolerance);
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

    private static IEnumerable<string> DescribeMaskedTransactionLayout(int pageNumber, IReadOnlyList<Word> words)
    {
        var lines = GroupIntoLines(words);
        var header = lines.FirstOrDefault(line => IsColumnHeader(line.Text));
        if (header is null)
        {
            yield return $"Halifax masked layout p{pageNumber}: transaction header not located.";
            yield break;
        }

        var footer = lines.FirstOrDefault(line => line.Y < header.Y && IsFooter(line.Text));
        var tableLines = lines.Where(line => line.Y < header.Y - LineTolerance
                && (footer is null || line.Y > footer.Y + LineTolerance))
            .OrderByDescending(line => line.Y).Take(60);
        foreach (var line in tableLines)
        {
            var shapes = line.Words.OrderBy(word => word.BoundingBox.Left).Take(40)
                .Select(word => $"{TokenShape(word.Text)}@{word.BoundingBox.Left:0.0},{Baseline(word):0.0}");
            yield return $"Halifax masked layout p{pageNumber}: {string.Join(' ', shapes)}";
        }
    }

    private static string TokenShape(string value)
    {
        var letters = value.Count(char.IsLetter);
        var digits = value.Count(char.IsDigit);
        var symbols = value.Length - letters - digits;
        return $"L{letters}D{digits}S{symbols}";
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

    private static bool TryParseDate(string value, out DateTime date)
    {
        var readable = Regex.Replace((value ?? string.Empty).Replace('\u00a0', ' ').Replace('\u202f', ' '), @"\s+", " ").Trim();
        if (DateTime.TryParseExact(readable,
                ["dd MMM yy", "d MMM yy", "dd MMM yyyy", "d MMM yyyy", "dd/MM/yy", "d/MM/yy",
                    "dd/MM/yyyy", "d/MM/yyyy", "dd-MM-yy", "d-MM-yy", "dd-MM-yyyy", "d-MM-yyyy"],
                CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out date)) return true;

        var compact = new string(readable.Where(char.IsLetterOrDigit).ToArray());
        return DateTime.TryParseExact(compact,
            ["ddMMMyy", "dMMMyy", "ddMMMyyyy", "dMMMyyyy", "ddMMyy", "dMMyy", "ddMMyyyy", "dMMyyyy"],
            CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out date);
    }

    private static decimal? ParseMoney(string value)
    {
        var raw = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Replace('\u2212', '-');
        var digits = new StringBuilder();
        foreach (var character in raw)
        {
            if (char.IsDigit(character))
            {
                var digit = char.GetNumericValue(character);
                if (digit is >= 0 and <= 9 && digit == Math.Truncate(digit)) digits.Append((int)digit);
            }
        }
        if (digits.Length == 0 || !decimal.TryParse(digits.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var minorUnits)) return null;
        var negative = raw.Contains('-') || (raw.Contains('(') && raw.Contains(')'));
        var amount = minorUnits / 100m;
        return negative ? -amount : amount;
    }

    private static string Clean(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    private static string CleanCode(string value) => Regex.Replace(Clean(value).ToUpperInvariant(), "[^A-Z]", string.Empty);
    private static string NormaliseLabel(string value) => Regex.Replace(Clean(value).ToLowerInvariant(), "[^a-z]", string.Empty);
    private static bool IsColumnHeader(string text)
    {
        var label = NormaliseLabel(text);
        return (label.Contains("date", StringComparison.Ordinal) && label.Contains("description", StringComparison.Ordinal))
            || (label.Contains("moneyin", StringComparison.Ordinal) && label.Contains("moneyout", StringComparison.Ordinal))
            || label is "date" or "description" or "type" or "moneyin" or "moneyout" or "balance";
    }
    private static bool IsFooter(string text) =>
        text.Contains("Continued on next page", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("Transaction types", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("If you think something", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Bank Statement Abbreviations", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Additional Abbreviations", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("Halifax is a division", StringComparison.OrdinalIgnoreCase);
    private static bool IsIgnoredText(string text) => string.IsNullOrWhiteSpace(text) || IsFooter(text);

    internal sealed record StatementRow(int Page, string Date, string Description, string Type,
        decimal? MoneyIn, decimal? MoneyOut, decimal? Balance);
    private sealed record DateAnchor(DateTime Date, string Text, double Y);
    private sealed record ExtractedLine(double Y, string Text, IReadOnlyList<Word> Words);
    private sealed record ColumnLayout(IReadOnlyList<double> Boundaries, IReadOnlyList<double> Anchors, string Source)
    {
        public int ColumnFor(double left)
        {
            for (var index = 0; index < Boundaries.Count; index++)
                if (left < Boundaries[index]) return index;
            return Boundaries.Count;
        }

        public int NumericColumnFor(double right)
        {
            if (Anchors.Count < 6) return ColumnFor(right);
            return Enumerable.Range(3, 3).MinBy(index => Math.Abs(Anchors[index] - right));
        }
    }
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
