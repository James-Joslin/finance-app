using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

            foreach (Match match in transactionMatches)
            {
                string block = match.Groups[1].Value;

                DateTime date = ExtractDate(block, "DTPOSTED");
                decimal amount = ExtractDecimal(block, "TRNAMT");
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
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static DateTime ExtractDate(string block, string tag)
        {
            string raw = ExtractTag(block, tag);

            if (string.IsNullOrWhiteSpace(raw))
                return DateTime.MinValue;

            // OFX datetime format: YYYYMMDDHHMMSS[.XXX][GMT offset]
            // We'll just take the first 14 digits if available
            string cleaned = raw.Length >= 14 ? raw.Substring(0, 14) : raw.Substring(0, 8);

            string[] formats = { "yyyyMMddHHmmss", "yyyyMMdd" };

            if (DateTime.TryParseExact(cleaned, formats, null, System.Globalization.DateTimeStyles.None, out var date))
                return date;

            return DateTime.MinValue;
        }

        private static decimal ExtractDecimal(string block, string tag)
        {
            string raw = ExtractTag(block, tag);
            if (decimal.TryParse(raw, out var value))
                return value;
            return 0;
        }
    }
}
