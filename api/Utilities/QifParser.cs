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
            var seenKeys = new HashSet<TransactionKey>();

            using var reader = new StreamReader(qifStream);
            string line;
            QifTransactionDto currentTransaction = null;
            
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                
                if (line.StartsWith("!Type:"))
                {
                    Console.WriteLine($"QIF Account Type: {line.Substring(6)}");
                    continue;
                }
                
                if (string.IsNullOrEmpty(line))
                    continue;
                
                if (line == "^")
                {
                    // End of transaction - add it if valid
                    if (currentTransaction != null && 
                        currentTransaction.Date != DateTime.MinValue &&
                        !string.IsNullOrEmpty(currentTransaction.Payee))
                    {
                        var key = new TransactionKey
                        {
                            Date = currentTransaction.Date,
                            Amount = currentTransaction.Amount,
                            Payee = currentTransaction.Payee,
                            Memo = currentTransaction.Memo
                        };

                        if (!seenKeys.Contains(key))
                        {
                            seenKeys.Add(key);
                            results.Add(currentTransaction);
                            // Console.WriteLine($"QIF Transaction: {currentTransaction.Date:yyyy-MM-dd} | {currentTransaction.Amount:F2} | {currentTransaction.Payee}");
                        }
                    }
                    currentTransaction = null;
                    continue;
                }
                
                // Start new transaction if needed
                if (currentTransaction == null && line.Length > 1)
                {
                    currentTransaction = new QifTransactionDto
                    {
                        Date = DateTime.MinValue,
                        Amount = 0,
                        Payee = ""
                    };
                }
                
                if (currentTransaction != null && line.Length > 1)
                {
                    char field = line[0];
                    string value = line.Substring(1).Trim();
                    
                    switch (field)
                    {
                        case 'D': // Date
                            currentTransaction.Date = ParseQifDate(value);
                            break;
                            
                        case 'T': // Amount
                            currentTransaction.Amount = ParseQifAmount(value);
                            break;
                            
                        case 'P': // Payee
                            currentTransaction.Payee = value;
                            break;
                            
                        case 'M': // Memo
                            currentTransaction.Memo = value;
                            break;
                            
                        case 'N': // Check number
                            currentTransaction.CheckNumber = value;
                            break;
                            
                        case 'L': // Category
                            currentTransaction.Category = value;
                            break;
                    }
                }
            }
            
            // Handle last transaction if file doesn't end with ^
            if (currentTransaction != null && 
                currentTransaction.Date != DateTime.MinValue &&
                !string.IsNullOrEmpty(currentTransaction.Payee))
            {
                var key = new TransactionKey
                {
                    Date = currentTransaction.Date,
                    Amount = currentTransaction.Amount,
                    Payee = currentTransaction.Payee,
                    Memo = currentTransaction.Memo
                };

                if (!seenKeys.Contains(key))
                {
                    results.Add(currentTransaction);
                }
            }
            
            Console.WriteLine($"Total unique QIF transactions found: {results.Count}");
            return results;
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