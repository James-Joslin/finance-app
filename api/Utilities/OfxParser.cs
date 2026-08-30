using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using financesApi.models;

namespace financesApi.utilities
{
    public static class OfxParser
    {
        public static List<OfxTransactionDto> Parse(Stream ofxStream)
        {
            var results = new List<OfxTransactionDto>();
            var seenKeys = new HashSet<TransactionKey>();

            using var reader = new StreamReader(ofxStream);
            string content = reader.ReadToEnd();

            var bodyStart = content.IndexOf("<OFX>");
            if (bodyStart < 0)
            {
                Console.WriteLine("Invalid OFX file: missing <OFX> tag.");
                return results;
            }

            string ofxBody = content.Substring(bodyStart);
            var transactionMatches = Regex.Matches(ofxBody, @"<STMTTRN>(.*?)</STMTTRN>", RegexOptions.Singleline);

            var transactionNumber = 0;
            foreach (Match match in transactionMatches)
            {
                transactionNumber++;
                string block = match.Groups[1].Value;

                DateTime date = ExtractDate(block, "DTPOSTED", transactionNumber);
                decimal amount = ExtractDecimal(block, "TRNAMT", transactionNumber);
                string payee = ExtractTag(block, "NAME");
                string memo = ExtractTag(block, "MEMO");
                string fitId = ExtractTag(block, "FITID");
                string transType = ExtractTag(block, "TRNTYPE");

                var key = new TransactionKey
                {
                    Date = date,
                    Amount = amount,
                    Payee = payee,
                    Memo = memo,
                    FitId = fitId,
                    TransType = transType
                };

                if (seenKeys.Contains(key))
                {
                    // Console.WriteLine($"Duplicate transaction skipped: {fitId}");
                    continue;
                }

                seenKeys.Add(key);

                var tx = new OfxTransactionDto
                {
                    Date = date,
                    Amount = amount,
                    Payee = payee,
                    Memo = memo,
                    FitId = fitId,
                    TransType = transType
                };

                // Console.WriteLine($"Date: {tx.Date:yyyy-MM-dd}, Amount: {tx.Amount}, Payee: {tx.Payee}, Memo: {tx.Memo}, FIT ID: {tx.FitId}, Transaction Type: {tx.transType}");
                results.Add(tx);
            }

            // Console.WriteLine($"Total unique transactions found: {results.Count}");
            return results;
        }

        private static string ExtractTag(string block, string tag)
        {
            var match = Regex.Match(block, $"<{tag}>(.*?)\\s*(?=<|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static DateTime ExtractDate(string block, string tag, int transactionNumber)
        {
            string raw = ExtractTag(block, tag);

            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidDataException($"OFX transaction {transactionNumber} has no posting date.");

            // OFX datetime format: YYYYMMDDHHMMSS[.XXX][GMT offset]
            // We'll just take the first 14 digits if available
            if (raw.Length < 8) throw new InvalidDataException($"OFX transaction {transactionNumber} has an invalid posting date.");
            string cleaned = raw.Length >= 14 ? raw[..14] : raw[..8];

            string[] formats = { "yyyyMMddHHmmss", "yyyyMMdd" };

            if (DateTime.TryParseExact(cleaned, formats, null, System.Globalization.DateTimeStyles.None, out var date))
                return date;

            throw new InvalidDataException($"OFX transaction {transactionNumber} has an invalid posting date.");
        }

        private static decimal ExtractDecimal(string block, string tag, int transactionNumber)
        {
            string raw = ExtractTag(block, tag);
            if (decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                return value;
            throw new InvalidDataException($"OFX transaction {transactionNumber} has an invalid amount.");
        }
    }
}
