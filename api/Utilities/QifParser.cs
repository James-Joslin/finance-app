// Services/QifParserService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using financesApi.models;

namespace financesApi.utilities
{
    public static class QifParser
    {
        public static List<TransactionDto> Parse(Stream qifStream)
        {
            var results = new List<TransactionDto>();
            using var reader = new StreamReader(qifStream);
            string? line;
            QifTransactionDto? currentTransaction = null;

            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.StartsWith("!Type:"))
                {
                    Console.WriteLine($"QIF Account Type: {line[6..]}");
                    continue;
                }
                if (string.IsNullOrEmpty(line)) continue;
                if (line == "^")
                {
                    AddIfValid(currentTransaction, results);
                    currentTransaction = null;
                    continue;
                }
                if (currentTransaction is null && line.Length > 1)
                {
                    currentTransaction = new QifTransactionDto
                    {
                        Date = DateTime.MinValue,
                        Amount = 0,
                        Payee = string.Empty,
                    };
                }
                if (currentTransaction is null || line.Length <= 1) continue;
                var value = line[1..].Trim();
                switch (line[0])
                {
                    case 'D': currentTransaction.Date = ParseQifDate(value); break;
                    case 'T': currentTransaction.Amount = ParseQifAmount(value); break;
                    case 'P': currentTransaction.Payee = value; break;
                    case 'M': currentTransaction.Memo = value; break;
                    case 'N': currentTransaction.CheckNumber = value; break;
                    case 'L': currentTransaction.Category = value; break;
                }
            }

            AddIfValid(currentTransaction, results);
            Console.WriteLine($"Total QIF transactions found: {results.Count}");
            return results;
        }

        private static void AddIfValid(QifTransactionDto? transaction, ICollection<TransactionDto> results)
        {
            if (transaction is not null && transaction.Date != DateTime.MinValue && !string.IsNullOrWhiteSpace(transaction.Payee))
                results.Add(transaction);
        }

        private static DateTime ParseQifDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return DateTime.MinValue;

            // Try different date formats
            string[] formats = {
                "dd/MM/yyyy",  // UK format (27/08/2025)
                "MM/dd/yyyy",  // US format
                "yyyy-MM-dd",  // ISO format
                "d/M/yyyy",    // Single digit day/month
                "M/d/yyyy"
            };

            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out DateTime date))
            {
                return date;
            }

            // Fallback to general parse
            if (DateTime.TryParse(dateStr, out date))
            {
                return date;
            }

            Console.WriteLine($"Failed to parse QIF date: {dateStr}");
            return DateTime.MinValue;
        }

        private static decimal ParseQifAmount(string amountStr)
        {
            if (string.IsNullOrWhiteSpace(amountStr))
                return 0;

            // Remove currency symbols and spaces
            amountStr = amountStr.Replace("£", "")
                                 .Replace("$", "")
                                 .Replace("€", "")
                                 .Replace(",", "")
                                 .Replace(" ", "")
                                 .Trim();

            if (decimal.TryParse(amountStr, NumberStyles.Any, 
                CultureInfo.InvariantCulture, out decimal amount))
            {
                return amount;
            }

            Console.WriteLine($"Failed to parse QIF amount: {amountStr}");
            return 0;
        }
    }
}
