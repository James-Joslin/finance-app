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
            var existingKeys = new HashSet<string>();
            foreach (DataRow row in existingTransactions.Rows)
            {
                // Primary key: FitId for OFX transactions
                var fitId = row["fitid"]?.ToString();
                if (!string.IsNullOrEmpty(fitId))
                {
                    existingKeys.Add($"FITID:{fitId}");
                }
                
                // Composite key for all transactions
                var date = ((DateTime)row["transaction_date"]).ToString("yyyy-MM-dd");
                var amount = row["amount"].ToString();
                var payee = row["payee"]?.ToString() ?? "";
                existingKeys.Add($"{date}|{amount}|{payee}");
            }

            // Step 4: Filter for new transactions
            var newTransactions = new List<TransactionDto>();
            
            foreach (var tx in incomingTransactions)
            {
                bool isDuplicate = false;
                
                // Check OFX by FitId
                if (tx is OfxTransactionDto ofxTx && !string.IsNullOrEmpty(ofxTx.FitId))
                {
                    if (existingKeys.Contains($"FITID:{ofxTx.FitId}"))
                    {
                        isDuplicate = true;
                        Console.WriteLine($"Duplicate OFX transaction found (FitId: {ofxTx.FitId})");
                    }
                }
                
                // Check all transactions by composite key
                if (!isDuplicate)
                {
                    var compositeKey = $"{tx.Date:yyyy-MM-dd}|{tx.Amount}|{tx.Payee}";
                    if (existingKeys.Contains(compositeKey))
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
                    _ => "UNKNOWN"
                };
                
                var insertQuery = @"
                    INSERT INTO transactions (
                        account_id, transaction_date, amount, payee, memo,
                        fitid, transaction_type, category, check_number, source_file_type
                    )
                    VALUES (
                        @accountId, @transaction_date, @amount, @payee, @memo,
                        @fitid, @transaction_type, @category, @check_number, @source_file_type
                    )";

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
    }
}