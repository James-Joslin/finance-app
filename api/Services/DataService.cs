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

        public static async Task<DataTable> ExecuteParameterisedQueryAsync(string queryPath, Dictionary<string, object> parameters)
        {
            string query = await SqlQueryLoader.GetQueryAsync(queryPath)
                ?? throw new ArgumentNullException(nameof(query), $"Query '{queryPath}' returned null");
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

            var insertedTransactions = new List<TransactionDto>();
            var unkeyedOccurrences = new Dictionary<string, int>();
            await using var connection = PostgreSqlQuerier.BuildConnection();
            await connection.OpenAsync();
            await using (var account = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=@id AND NOT is_archived", connection))
            {
                account.Parameters.AddWithValue("id", accountId);
                if (await account.ExecuteScalarAsync() is null) throw new ArgumentException("An active import account is required.");
            }
            await using var databaseTransaction = await connection.BeginTransactionAsync();

            foreach (var item in incomingTransactions)
            {
                var baseKey = TransactionFingerprint.BuildBase(item);
                var occurrence = 1;
                if (TransactionFingerprint.GetFitId(item) is null)
                {
                    occurrence = unkeyedOccurrences.GetValueOrDefault(baseKey) + 1;
                    unkeyedOccurrences[baseKey] = occurrence;
                }
                var fingerprint = TransactionFingerprint.Build(item, occurrence);
                var sourceType = item switch
                {
                    OfxTransactionDto => "OFX",
                    QifTransactionDto => "QIF",
                    HalifaxPdfTransactionDto => "PDF",
                    _ => "UNKNOWN"
                };
                var fitId = TransactionFingerprint.GetFitId(item);
                var transactionType = item switch
                {
                    OfxTransactionDto ofx => ofx.TransType,
                    HalifaxPdfTransactionDto pdf => pdf.TransactionCode,
                    _ => null,
                };
                var importedCategory = item switch
                {
                    QifTransactionDto qif => qif.Category,
                    HalifaxPdfTransactionDto pdf => pdf.Category,
                    _ => null,
                };
                var checkNumber = item is QifTransactionDto qifItem ? qifItem.CheckNumber : null;

                const string insertSql = """
                    WITH chosen_category AS (
                        SELECT coalesce(
                            (SELECT tr.category_id FROM transaction_rules tr
                             WHERE tr.is_active
                               AND nullif(trim(tr.match_text), '') IS NOT NULL
                               AND (tr.direction = 'any' OR tr.direction = CASE WHEN @amount >= 0 THEN 'in' ELSE 'out' END)
                               AND lower(coalesce(@payee::text, @memo::text, '')) LIKE '%' || lower(trim(tr.match_text)) || '%'
                             ORDER BY length(trim(tr.match_text)) DESC,
                                CASE WHEN tr.direction = 'any' THEN 1 ELSE 0 END,
                                tr.priority, tr.id LIMIT 1),
                            (SELECT c.id FROM categories c
                             WHERE lower(c.name) = lower(coalesce(@category::text, '')) LIMIT 1),
                            (SELECT c.id FROM categories c WHERE c.name = 'Uncategorised')
                        ) AS id
                    )
                    INSERT INTO transactions (
                        account_id, transaction_date, amount, payee, memo,
                        fitid, transaction_type, category, check_number, source_file_type,
                        category_id, is_transfer, import_fingerprint
                    )
                    SELECT
                        @account_id, @transaction_date, @amount, @payee, @memo,
                        @fitid, @transaction_type, @category, @check_number, @source_file_type,
                        chosen_category.id,
                        exists(SELECT 1 FROM categories c WHERE c.id = chosen_category.id AND c.kind = 'transfer'),
                        @fingerprint
                    FROM chosen_category
                    ON CONFLICT (account_id, import_fingerprint) WHERE import_fingerprint IS NOT NULL DO NOTHING
                    """;
                await using var insert = new NpgsqlCommand(insertSql, connection, databaseTransaction);
                insert.Parameters.AddWithValue("account_id", accountId);
                insert.Parameters.AddWithValue("transaction_date", item.Date);
                insert.Parameters.AddWithValue("amount", item.Amount);
                insert.Parameters.AddWithValue("payee", (object?)item.Payee ?? DBNull.Value);
                insert.Parameters.AddWithValue("memo", (object?)item.Memo ?? DBNull.Value);
                insert.Parameters.AddWithValue("fitid", (object?)fitId ?? DBNull.Value);
                insert.Parameters.AddWithValue("transaction_type", (object?)transactionType ?? DBNull.Value);
                insert.Parameters.AddWithValue("category", (object?)importedCategory ?? DBNull.Value);
                insert.Parameters.AddWithValue("check_number", (object?)checkNumber ?? DBNull.Value);
                insert.Parameters.AddWithValue("source_file_type", sourceType);
                insert.Parameters.AddWithValue("fingerprint", fingerprint);
                if (await insert.ExecuteNonQueryAsync() > 0) insertedTransactions.Add(item);
            }

            await databaseTransaction.CommitAsync();
            return insertedTransactions;
        }

    }
}
