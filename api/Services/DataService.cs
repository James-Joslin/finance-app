using System.Data;
using financesApi.models;
using financesApi.utilities;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace financesApi.services
{
    // Generic Data Service - replaces your DataService
    public static class GenericDataService
    {
        // Generic read operation
        public static async Task<DataTable> ExecuteQueryAsync(string queryPath, TransactionQueryRequest? queryParameters = null)
        {
            string query = await SqlQueryLoader.GetQueryAsync(queryPath)
                ?? throw new ArgumentNullException(nameof(query), $"Query '{queryPath}' returned null");

            if (queryParameters != null)
            {
                string filter = FilterBuilder.BuildFilter(queryParameters);

                query += filter;
            }

            return await PostgreSqlQuerier.ExecuteQueryAsync(query);
        }

        public static async Task<DataTable> ExecuteParameterisedQueryAsync(string queryPath, Dictionary<string, object> parameters)
        {
            string query = await SqlQueryLoader.GetQueryAsync(queryPath)
                ?? throw new ArgumentNullException(nameof(query), $"Query '{queryPath}' returned null");
            // Console.WriteLine(query);
            return await PostgreSqlQuerier.ExecuteParameterisedQueryAsync(query, parameters);
        }

        // Generic write operation
        public static async Task<int> ExecuteCommandAsync(string queryPath, Dictionary<string, object> parameters)
        {
            string query = await SqlQueryLoader.GetQueryAsync(queryPath)
                ?? throw new ArgumentNullException(nameof(query), $"Query '{queryPath}' returned null");

            return await PostgreSqlQuerier.ExecuteNonQueryAsync(query, parameters);
        }

        // Insert and return ID
        public static async Task<T?> InsertAndReturnAsync<T>(string queryPath, Dictionary<string, object> parameters)
        {
            string query = await SqlQueryLoader.GetQueryAsync(queryPath)
                ?? throw new ArgumentNullException(nameof(query), $"Query '{queryPath}' returned null");

            return await PostgreSqlQuerier.ExecuteScalarAsync<T>(query, parameters);
        }

        // Batch operations with transaction
        public static async Task ExecuteBatchAsync(List<(string queryPath, Dictionary<string, object> parameters)> operations)
        {
            await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
            {
                foreach (var (queryPath, parameters) in operations)
                {
                    string query = await SqlQueryLoader.GetQueryAsync(queryPath);
                    
                    using var command = new NpgsqlCommand(query, connection, transaction);
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                    
                    await command.ExecuteNonQueryAsync();
                }
            });
        }

        public static async Task<List<TransactionDto>> FilterAndInsertTransactionsAsync(
            List<TransactionDto> incomingTransactions,
            int accountId,
            int daysTolerance = 1)
        {
            if (!incomingTransactions.Any()) return new List<TransactionDto>();

            // Step 1: Get date range
            var minDate = incomingTransactions.Min(t => t.Date).AddDays(-daysTolerance);
            var maxDate = incomingTransactions.Max(t => t.Date).AddDays(daysTolerance);

            // Step 2: Query existing transactions
            var existingQuery = @"
                SELECT transaction_date, amount, payee, memo, fitid, source_file_type
                FROM transactions 
                WHERE account_id = @accountId 
                AND transaction_date BETWEEN @minDate AND @maxDate";

            var parameters = new Dictionary<string, object>
            {
                { "@accountId", accountId },
                { "@minDate", minDate },
                { "@maxDate", maxDate }
            };

            var existingTransactions = await PostgreSqlQuerier.ExecuteParameterisedQueryAsync(existingQuery, parameters);

            // Step 3: Build hash set for duplicate detection
            var existingFitIds = new HashSet<string>();
            var existingCompositeKeys = new HashSet<string>();
            var existingUnkeyedCompositeKeys = new HashSet<string>();
            foreach (DataRow row in existingTransactions.Rows)
            {
                var fitId = row["fitid"]?.ToString();
                var payee = row["payee"]?.ToString() ?? "";
                var composite = TransactionCompositeKey((DateTime)row["transaction_date"], Convert.ToDecimal(row["amount"]), payee);
                existingCompositeKeys.Add(composite);
                if (!string.IsNullOrEmpty(fitId)) existingFitIds.Add(fitId);
                else existingUnkeyedCompositeKeys.Add(composite);
            }

            // Step 4: Filter for new transactions
            var newTransactions = new List<TransactionDto>();
            var incomingFitIds = new HashSet<string>();
            var incomingUnkeyedCompositeKeys = new HashSet<string>();
            
            foreach (var tx in incomingTransactions)
            {
                bool isDuplicate = false;
                var compositeKey = TransactionCompositeKey(tx.Date, tx.Amount, tx.Payee);
                
                var incomingFitId = tx switch
                {
                    OfxTransactionDto ofx => ofx.FitId,
                    HalifaxPdfTransactionDto pdf => pdf.FitId,
                    _ => null
                };
                if (!string.IsNullOrEmpty(incomingFitId))
                {
                    isDuplicate = existingFitIds.Contains(incomingFitId)
                        || existingUnkeyedCompositeKeys.Contains(compositeKey)
                        || !incomingFitIds.Add(incomingFitId);
                }
                
                // Check all transactions by composite key
                if (!isDuplicate && string.IsNullOrEmpty(incomingFitId))
                {
                    if (existingCompositeKeys.Contains(compositeKey) || !incomingUnkeyedCompositeKeys.Add(compositeKey))
                    {
                        isDuplicate = true;
                        Console.WriteLine($"Duplicate transaction found: {tx.Date:yyyy-MM-dd} - {tx.Amount} - {tx.Payee}");
                    }
                }
                
                if (!isDuplicate)
                {
                    newTransactions.Add(tx);
                }
            }

            // Step 5: Insert new transactions with a unified approach
            foreach (var tx in newTransactions)
            {
                // Determine source type
                string sourceType = tx switch
                {
                    OfxTransactionDto => "OFX",
                    QifTransactionDto => "QIF",
                    HalifaxPdfTransactionDto => "PDF",
                    _ => "UNKNOWN"
                };
                
                var insertQuery = @"
                    WITH chosen_category AS (
                        SELECT coalesce(
                            (SELECT tr.category_id FROM transaction_rules tr
                             WHERE tr.is_active AND lower(@payee::text) LIKE '%' || lower(tr.match_text) || '%'
                             ORDER BY tr.priority, tr.id LIMIT 1),
                            (SELECT c.id FROM categories c
                             WHERE lower(c.name) = lower(coalesce(@category::text, '')) LIMIT 1),
                            (SELECT c.id FROM categories c WHERE c.name = 'Uncategorised')
                        ) AS id
                    )
                    INSERT INTO transactions (
                        account_id, transaction_date, amount, payee, memo,
                        fitid, transaction_type, category, check_number, source_file_type,
                        category_id, is_transfer
                    )
                    SELECT
                        @accountId, @transaction_date, @amount, @payee, @memo,
                        @fitid, @transaction_type, @category, @check_number, @source_file_type,
                        chosen_category.id,
                        exists(SELECT 1 FROM categories c WHERE c.id = chosen_category.id AND c.kind = 'transfer')
                    FROM chosen_category";

                var insertParams = new Dictionary<string, object>
                {
                    { "@accountId", accountId },
                    { "@transaction_date", tx.Date },
                    { "@amount", tx.Amount },
                    { "@payee", (object)tx.Payee ?? DBNull.Value },
                    { "@memo", (object)tx.Memo ?? DBNull.Value },
                    { "@source_file_type", sourceType }
                };

                // Add type-specific fields
                if (tx is OfxTransactionDto ofxTx)
                {
                    insertParams["@fitid"] = (object)ofxTx.FitId ?? DBNull.Value;
                    insertParams["@transaction_type"] = (object)ofxTx.TransType ?? DBNull.Value;
                    insertParams["@category"] = DBNull.Value;
                    insertParams["@check_number"] = DBNull.Value;
                }
                else if (tx is QifTransactionDto qifTx)
                {
                    insertParams["@fitid"] = DBNull.Value;
                    insertParams["@transaction_type"] = DBNull.Value;
                    insertParams["@category"] = (object)qifTx.Category ?? DBNull.Value;
                    insertParams["@check_number"] = (object)qifTx.CheckNumber ?? DBNull.Value;
                }
                else if (tx is HalifaxPdfTransactionDto pdfTx)
                {
                    insertParams["@fitid"] = pdfTx.FitId;
                    insertParams["@transaction_type"] = pdfTx.TransactionCode;
                    insertParams["@category"] = (object?)pdfTx.Category ?? DBNull.Value;
                    insertParams["@check_number"] = DBNull.Value;
                }
                else
                {
                    insertParams["@fitid"] = DBNull.Value;
                    insertParams["@transaction_type"] = DBNull.Value;
                    insertParams["@category"] = DBNull.Value;
                    insertParams["@check_number"] = DBNull.Value;
                }

                await PostgreSqlQuerier.ExecuteNonQueryAsync(insertQuery, insertParams);
            }

            Console.WriteLine($"Successfully inserted {newTransactions.Count} new transactions (skipped {incomingTransactions.Count - newTransactions.Count} duplicates)");
            return newTransactions;
        }

        private static string TransactionCompositeKey(DateTime date, decimal amount, string? payee) =>
            $"{date:yyyy-MM-dd}|{amount.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture)}|{payee ?? string.Empty}";
    }
}
