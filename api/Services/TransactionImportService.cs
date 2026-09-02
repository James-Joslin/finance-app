using System.Globalization;
using System.Security.Cryptography;
using financesApi.models;
using financesApi.utilities;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace financesApi.services;

public static class TransactionImportService
{
    private const int PreviewLifetimeHours = 24;
    private static readonly string[] RowOutcomes = ["ready", "imported", "skipped", "rejected", "undone"];

    public static async Task<ImportBatchSummary> PreviewAsync(IFormFile file, int accountId)
    {
        if (file.Length == 0) throw new ArgumentException("The uploaded file is empty.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        buffer.Position = 0;
        var parsed = FinancialFileParserService.ParseRows(buffer, Path.GetFileName(file.FileName));
        var fileName = Path.GetFileName(file.FileName);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddHours(PreviewLifetimeHours);

        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await PurgeExpiredPreviewsAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();

        string accountName;
        decimal startingBalance;
        await using (var account = new NpgsqlCommand(
            """
            SELECT a.name, coalesce(sum(t.amount), 0)
            FROM accounts a LEFT JOIN transactions t ON t.account_id=a.id
            WHERE a.id=@id AND NOT a.is_archived
            GROUP BY a.id
            """, connection, transaction))
        {
            account.Parameters.AddWithValue("id", accountId);
            await using var reader = await account.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ArgumentException("An active import account is required.");
            accountName = reader.GetString(0);
            startingBalance = reader.GetDecimal(1);
        }

        var classified = ClassifyRows(parsed.Rows);
        var existing = await ExistingFingerprintsAsync(connection, transaction, accountId,
            classified.Where(row => row.Fingerprint is not null).Select(row => row.Fingerprint!).Distinct().ToArray());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < classified.Count; index++)
        {
            var row = classified[index];
            if (row.Transaction is null || row.Fingerprint is null) continue;
            if (existing.Contains(row.Fingerprint))
                classified[index] = row with
                {
                    Outcome = "skipped",
                    ReasonCode = "duplicate_existing",
                    ReasonMessage = "A matching transaction already exists in this account.",
                };
            else if (!seen.Add(row.Fingerprint))
                classified[index] = row with
                {
                    Outcome = "skipped",
                    ReasonCode = "duplicate_in_file",
                    ReasonMessage = "This stable transaction identifier appears more than once in the file.",
                };
        }

        ApplyProjectedBalances(classified, startingBalance);

        var total = classified.Count;
        var importable = classified.Count(row => row.Outcome == "ready");
        var skipped = classified.Count(row => row.Outcome == "skipped");
        var rejected = classified.Count(row => row.Outcome == "rejected");
        long batchId;
        const string batchSql = """
            INSERT INTO transaction_import_batches (
                account_id, file_name, file_type, file_size, file_sha256, starting_balance, status,
                total_rows, importable_rows, imported_rows, skipped_rows, rejected_rows,
                created_at, expires_at
            ) VALUES (
                @account, @name, @type, @size, @hash, @starting_balance, 'preview',
                @total, @importable, 0, @skipped, @rejected, @created, @expires
            ) RETURNING id
            """;
        await using (var batch = new NpgsqlCommand(batchSql, connection, transaction))
        {
            batch.Parameters.AddWithValue("account", accountId);
            batch.Parameters.AddWithValue("name", fileName);
            batch.Parameters.AddWithValue("type", parsed.FileType);
            batch.Parameters.AddWithValue("size", file.Length);
            batch.Parameters.AddWithValue("hash", fileHash);
            batch.Parameters.AddWithValue("starting_balance", startingBalance);
            batch.Parameters.AddWithValue("total", total);
            batch.Parameters.AddWithValue("importable", importable);
            batch.Parameters.AddWithValue("skipped", skipped);
            batch.Parameters.AddWithValue("rejected", rejected);
            batch.Parameters.AddWithValue("created", createdAt);
            batch.Parameters.AddWithValue("expires", expiresAt);
            batchId = Convert.ToInt64(await batch.ExecuteScalarAsync());
        }

        foreach (var row in classified)
            await InsertRowAsync(connection, transaction, batchId, parsed.FileType, row);

        await transaction.CommitAsync();
        return new(batchId, accountId, accountName, fileName, parsed.FileType, file.Length, fileHash,
            startingBalance, "preview", createdAt, expiresAt, null, null, total, importable, 0, skipped, rejected, false);
    }

    public static async Task<ImportBatchSummary> CommitAsync(long batchId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        int accountId;
        string status;
        DateTimeOffset expiresAt;
        await using (var batch = new NpgsqlCommand(
            "SELECT account_id, status, expires_at FROM transaction_import_batches WHERE id=@id FOR UPDATE",
            connection, transaction))
        {
            batch.Parameters.AddWithValue("id", batchId);
            await using var reader = await batch.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Import preview was not found.");
            accountId = reader.GetInt32(0);
            status = reader.GetString(1);
            expiresAt = AsOffset(reader.GetDateTime(2));
        }

        if (status != "preview")
            throw new ImportBatchConflictException("This import preview has already been completed.");
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            await using var delete = new NpgsqlCommand(
                "DELETE FROM transaction_import_batches WHERE id=@id", connection, transaction);
            delete.Parameters.AddWithValue("id", batchId);
            await delete.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            throw new ImportBatchExpiredException("This import preview has expired. Upload the statement again.");
        }

        var rows = await GetReadyRowsAsync(connection, transaction, batchId);
        var importedDates = new List<DateOnly>();
        var importedTransactionIds = new List<int>();
        foreach (var row in rows)
        {
            var transactionId = await InsertTransactionAsync(connection, transaction, accountId, batchId, row);
            if (transactionId.HasValue)
            {
                importedDates.Add(DateOnly.FromDateTime(row.Date));
                importedTransactionIds.Add(transactionId.Value);
                await SetRowOutcomeAsync(connection, transaction, row.Id, "imported", null, null, transactionId);
            }
            else
            {
                await SetRowOutcomeAsync(connection, transaction, row.Id, "skipped",
                    "duplicate_at_commit", "A matching transaction was imported after this preview was created.", null);
            }
        }

        if (importedTransactionIds.Count > 0)
            await FinovaDataService.AutoPairImportedTransfersAsync(connection, transaction, importedTransactionIds);

        if (importedDates.Count > 0)
            await FinovaDataService.ReconcileRecurringTransactionsAsync(
                connection, transaction, accountId, importedDates.Min(), importedDates.Max());

        await RecalculateBalancesAsync(connection, transaction, batchId);
        var counts = await CountRowsAsync(connection, transaction, batchId);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE transaction_import_batches SET status='completed', completed_at=CURRENT_TIMESTAMP,
                imported_rows=@imported, skipped_rows=@skipped, rejected_rows=@rejected
            WHERE id=@id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", batchId);
            update.Parameters.AddWithValue("imported", counts.Imported);
            update.Parameters.AddWithValue("skipped", counts.Skipped);
            update.Parameters.AddWithValue("rejected", counts.Rejected);
            await update.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return await GetBatchAsync(batchId);
    }

    public static async Task<ImportBatchSummary> ImportImmediatelyAsync(IFormFile file, int accountId)
    {
        var preview = await PreviewAsync(file, accountId);
        return await CommitAsync(preview.Id);
    }

    public static async Task<PagedImportBatches> GetHistoryAsync(int? accountId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await PurgeExpiredPreviewsAsync(connection);

        var filter = accountId.HasValue ? " AND b.account_id=@account" : string.Empty;
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM transaction_import_batches b WHERE b.status IN ('completed','undone')" + filter,
            connection);
        if (accountId.HasValue) count.Parameters.AddWithValue("account", accountId.Value);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync());

        var sql = BatchSelectSql + filter + " ORDER BY b.created_at DESC, b.id DESC LIMIT @limit OFFSET @offset";
        await using var command = new NpgsqlCommand(sql, connection);
        if (accountId.HasValue) command.Parameters.AddWithValue("account", accountId.Value);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var items = new List<ImportBatchSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) items.Add(ReadBatch(reader));
        return new(items, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }

    public static async Task<PagedImportRows> GetRowsAsync(long batchId, string? outcome, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (!string.IsNullOrWhiteSpace(outcome) && !RowOutcomes.Contains(outcome))
            throw new ArgumentException("Unknown import row outcome.");

        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var exists = new NpgsqlCommand(
            "SELECT 1 FROM transaction_import_batches WHERE id=@id", connection))
        {
            exists.Parameters.AddWithValue("id", batchId);
            if (await exists.ExecuteScalarAsync() is null)
                throw new ResourceNotFoundException("Import batch was not found.");
        }

        var filter = string.IsNullOrWhiteSpace(outcome) ? string.Empty : " AND outcome=@outcome";
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM transaction_import_rows WHERE batch_id=@batch" + filter, connection);
        count.Parameters.AddWithValue("batch", batchId);
        if (filter.Length > 0) count.Parameters.AddWithValue("outcome", outcome!);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync());

        var sql = """
            SELECT id, ordinal, source_label, transaction_date, display_date, amount, display_amount,
                balance_after, payee, memo, outcome, reason_code, reason_message
            FROM transaction_import_rows WHERE batch_id=@batch
            """ + filter + " ORDER BY ordinal LIMIT @limit OFFSET @offset";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch", batchId);
        if (filter.Length > 0) command.Parameters.AddWithValue("outcome", outcome!);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var items = new List<ImportRowResult>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }
        return new(items, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }

    public static async Task<ImportUndoResult> UndoAsync(long batchId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        int accountId;
        string status;
        int imported;
        await using (var batch = new NpgsqlCommand(
            "SELECT account_id, status, imported_rows FROM transaction_import_batches WHERE id=@id FOR UPDATE",
            connection, transaction))
        {
            batch.Parameters.AddWithValue("id", batchId);
            await using var reader = await batch.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Import batch was not found.");
            accountId = reader.GetInt32(0);
            status = reader.GetString(1);
            imported = reader.GetInt32(2);
        }
        if (status != "completed" || imported == 0)
            throw new ImportBatchConflictException("This import batch cannot be undone.");

        long? latest;
        await using (var latestCommand = new NpgsqlCommand(
            """
            SELECT id FROM transaction_import_batches
            WHERE account_id=@account AND status='completed' AND imported_rows > 0
            ORDER BY created_at DESC, id DESC LIMIT 1 FOR UPDATE
            """, connection, transaction))
        {
            latestCommand.Parameters.AddWithValue("account", accountId);
            latest = (long?)await latestCommand.ExecuteScalarAsync();
        }
        if (latest != batchId)
            throw new ImportBatchConflictException("Only the latest active import for this account can be undone.");

        await using (var resetOccurrences = new NpgsqlCommand(
            """
            UPDATE recurring_occurrences SET status='expected', transaction_id=NULL,
                actual_amount=NULL, matched_at=NULL, updated_at=CURRENT_TIMESTAMP
            WHERE transaction_id IN (SELECT id FROM transactions WHERE import_batch_id=@batch)
            """, connection, transaction))
        {
            resetOccurrences.Parameters.AddWithValue("batch", batchId);
            await resetOccurrences.ExecuteNonQueryAsync();
        }

        int deleted;
        await using (var deleteTransactions = new NpgsqlCommand(
            "DELETE FROM transactions WHERE import_batch_id=@batch", connection, transaction))
        {
            deleteTransactions.Parameters.AddWithValue("batch", batchId);
            deleted = await deleteTransactions.ExecuteNonQueryAsync();
        }

        await using (var reconcileTransfers = new NpgsqlCommand("""
            UPDATE transactions t SET is_transfer = EXISTS (
                SELECT 1 FROM categories c WHERE c.id=t.category_id AND c.kind='transfer'
            ) OR EXISTS (
                SELECT 1 FROM transaction_transfer_pairs p WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id
            ) WHERE t.is_transfer
            """, connection, transaction))
        {
            await reconcileTransfers.ExecuteNonQueryAsync();
        }

        await using (var updateRows = new NpgsqlCommand(
            """
            UPDATE transaction_import_rows SET outcome='undone', transaction_id=NULL,
                reason_code='import_undone', reason_message='The import batch was undone.'
            WHERE batch_id=@batch AND outcome='imported'
            """, connection, transaction))
        {
            updateRows.Parameters.AddWithValue("batch", batchId);
            await updateRows.ExecuteNonQueryAsync();
        }
        await using (var updateBatch = new NpgsqlCommand(
            "UPDATE transaction_import_batches SET status='undone', undone_at=CURRENT_TIMESTAMP WHERE id=@batch",
            connection, transaction))
        {
            updateBatch.Parameters.AddWithValue("batch", batchId);
            await updateBatch.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return new(batchId, accountId, deleted);
    }

    public static async Task<ImportBatchSummary> GetBatchAsync(long batchId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(BatchSelectSql + " AND b.id=@id", connection);
        command.Parameters.AddWithValue("id", batchId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Import batch was not found.");
        return ReadBatch(reader);
    }

    private static List<ClassifiedRow> ClassifyRows(IReadOnlyList<ParsedFinancialRow> rows)
    {
        var occurrences = new Dictionary<string, int>();
        var results = new List<ClassifiedRow>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Transaction is null)
            {
                results.Add(new(row, null, null, "rejected", row.ErrorCode, row.ErrorMessage));
                continue;
            }
            var baseKey = TransactionFingerprint.BuildBase(row.Transaction);
            var occurrence = 1;
            if (TransactionFingerprint.GetFitId(row.Transaction) is null)
            {
                occurrence = occurrences.GetValueOrDefault(baseKey) + 1;
                occurrences[baseKey] = occurrence;
            }
            results.Add(new(row, row.Transaction, TransactionFingerprint.Build(row.Transaction, occurrence),
                "ready", null, null));
        }
        return results;
    }

    private static async Task<HashSet<string>> ExistingFingerprintsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId, string[] fingerprints)
    {
        if (fingerprints.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
            SELECT import_fingerprint FROM transactions
            WHERE account_id=@account AND import_fingerprint::text = ANY(@fingerprints)
            """, connection, transaction);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("fingerprints", fingerprints);
        var results = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0));
        return results;
    }

    private static async Task InsertRowAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long batchId, string fileType, ClassifiedRow row)
    {
        var item = row.Transaction;
        var fitId = item is null ? null : TransactionFingerprint.GetFitId(item);
        var transactionType = item switch
        {
            OfxTransactionDto ofx => ofx.TransType,
            HalifaxPdfTransactionDto pdf => pdf.TransactionCode,
            _ => null,
        };
        var category = item switch
        {
            QifTransactionDto qif => qif.Category,
            HalifaxPdfTransactionDto pdf => pdf.Category,
            _ => null,
        };
        var checkNumber = item is QifTransactionDto qifItem ? qifItem.CheckNumber : null;
        var balance = item is HalifaxPdfTransactionDto pdfItem ? pdfItem.StatementBalance : (decimal?)null;
        const string sql = """
            INSERT INTO transaction_import_rows (
                batch_id, ordinal, source_label, transaction_date, display_date, amount, display_amount,
                payee, memo, fitid, transaction_type, category, check_number, source_file_type,
                statement_balance, fingerprint, balance_after, outcome, reason_code, reason_message
            ) VALUES (
                @batch, @ordinal, @label, @date, @display_date, @amount, @display_amount,
                @payee, @memo, @fitid, @transaction_type, @category, @check_number, @source_type,
                @balance, @fingerprint, @balance_after, @outcome, @reason_code, @reason_message
            )
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batch", batchId);
        command.Parameters.AddWithValue("ordinal", row.Source.Ordinal);
        command.Parameters.AddWithValue("label", row.Source.SourceLabel);
        AddNullable(command, "date", item?.Date);
        AddNullable(command, "display_date", row.Source.DisplayDate);
        AddNullable(command, "amount", item?.Amount);
        AddNullable(command, "display_amount", row.Source.DisplayAmount);
        AddNullable(command, "payee", item?.Payee ?? row.Source.Payee);
        AddNullable(command, "memo", item?.Memo ?? row.Source.Memo);
        AddNullable(command, "fitid", fitId);
        AddNullable(command, "transaction_type", transactionType);
        AddNullable(command, "category", category);
        AddNullable(command, "check_number", checkNumber);
        command.Parameters.AddWithValue("source_type", fileType);
        AddNullable(command, "balance", balance);
        AddNullable(command, "fingerprint", row.Fingerprint);
        AddNullable(command, "balance_after", row.BalanceAfter);
        command.Parameters.AddWithValue("outcome", row.Outcome);
        AddNullable(command, "reason_code", row.ReasonCode);
        AddNullable(command, "reason_message", row.ReasonMessage);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<StagedRow>> GetReadyRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long batchId)
    {
        const string sql = """
            SELECT id, transaction_date, amount, payee, memo, fitid, transaction_type,
                category, check_number, source_file_type, statement_balance, fingerprint
            FROM transaction_import_rows WHERE batch_id=@batch AND outcome='ready' ORDER BY ordinal
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batch", batchId);
        var results = new List<StagedRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new(
                reader.GetInt64(0), reader.GetDateTime(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.GetString(11)));
        return results;
    }

    private static async Task<int?> InsertTransactionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId, long batchId, StagedRow row)
    {
        const string sql = """
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
                account_id, transaction_date, amount, payee, memo, fitid, transaction_type,
                category, check_number, source_file_type, category_id, is_transfer,
                import_fingerprint, import_batch_id
            )
            SELECT @account, @date, @amount, @payee, @memo, @fitid, @transaction_type,
                @category, @check_number, @source_type, chosen_category.id,
                exists(SELECT 1 FROM categories c WHERE c.id=chosen_category.id AND c.kind='transfer'),
                @fingerprint, @batch
            FROM chosen_category
            ON CONFLICT (account_id, import_fingerprint) WHERE import_fingerprint IS NOT NULL DO NOTHING
            RETURNING id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("batch", batchId);
        command.Parameters.AddWithValue("date", row.Date);
        command.Parameters.AddWithValue("amount", row.Amount);
        AddNullable(command, "payee", row.Payee);
        AddNullable(command, "memo", row.Memo);
        AddNullable(command, "fitid", row.FitId);
        AddNullable(command, "transaction_type", row.TransactionType);
        AddNullable(command, "category", row.Category);
        AddNullable(command, "check_number", row.CheckNumber);
        command.Parameters.AddWithValue("source_type", row.SourceFileType);
        command.Parameters.AddWithValue("fingerprint", row.Fingerprint);
        var result = await command.ExecuteScalarAsync();
        return result is null ? null : Convert.ToInt32(result);
    }

    private static async Task SetRowOutcomeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long rowId, string outcome,
        string? reasonCode, string? reasonMessage, int? transactionId)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE transaction_import_rows SET outcome=@outcome, reason_code=@reason_code,
                reason_message=@reason_message, transaction_id=@transaction
            WHERE id=@id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", rowId);
        command.Parameters.AddWithValue("outcome", outcome);
        AddNullable(command, "reason_code", reasonCode);
        AddNullable(command, "reason_message", reasonMessage);
        AddNullable(command, "transaction", transactionId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RecalculateBalancesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long batchId)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH direction AS (
                SELECT (
                    SELECT first_row.transaction_date
                    FROM transaction_import_rows first_row
                    WHERE first_row.batch_id=@batch AND first_row.transaction_date IS NOT NULL
                    ORDER BY first_row.ordinal LIMIT 1
                ) > (
                    SELECT last_row.transaction_date
                    FROM transaction_import_rows last_row
                    WHERE last_row.batch_id=@batch AND last_row.transaction_date IS NOT NULL
                    ORDER BY last_row.ordinal DESC LIMIT 1
                ) AS source_descending
            )
            UPDATE transaction_import_rows row SET balance_after = CASE
                WHEN row.transaction_date IS NULL OR row.amount IS NULL THEN NULL
                ELSE batch.starting_balance + coalesce((
                    SELECT sum(previous.amount)
                    FROM transaction_import_rows previous, direction
                    WHERE previous.batch_id=row.batch_id
                        AND previous.outcome='imported'
                        AND (
                            previous.transaction_date < row.transaction_date
                            OR (previous.transaction_date = row.transaction_date AND (
                                (coalesce(direction.source_descending, false) AND previous.ordinal >= row.ordinal)
                                OR (NOT coalesce(direction.source_descending, false) AND previous.ordinal <= row.ordinal)
                            ))
                        )
                ), 0)
            END
            FROM transaction_import_batches batch
            WHERE row.batch_id=@batch AND batch.id=row.batch_id
            """, connection, transaction);
        command.Parameters.AddWithValue("batch", batchId);
        await command.ExecuteNonQueryAsync();
    }

    private static void ApplyProjectedBalances(List<ClassifiedRow> rows, decimal startingBalance)
    {
        var datedRows = rows
            .Select((row, index) => (Row: row, Index: index))
            .Where(item => item.Row.Transaction is not null)
            .ToList();
        var balances = FinanceMath.CalculateImportBalances(startingBalance, datedRows.Select(item =>
            new ImportBalanceEntry(item.Row.Source.Ordinal, item.Row.Transaction!.Date,
                item.Row.Transaction.Amount, item.Row.Outcome == "ready")));
        foreach (var item in datedRows)
        {
            rows[item.Index] = item.Row with { BalanceAfter = balances[item.Row.Source.Ordinal] };
        }
    }

    private static async Task<(int Imported, int Skipped, int Rejected)> CountRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long batchId)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FILTER (WHERE outcome='imported'),
                count(*) FILTER (WHERE outcome='skipped'),
                count(*) FILTER (WHERE outcome='rejected')
            FROM transaction_import_rows WHERE batch_id=@batch
            """, connection, transaction);
        command.Parameters.AddWithValue("batch", batchId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static async Task PurgeExpiredPreviewsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM transaction_import_batches WHERE status='preview' AND expires_at < CURRENT_TIMESTAMP",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddNullable(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ImportBatchSummary ReadBatch(NpgsqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetInt64(5), reader.GetString(6), reader.GetDecimal(7), reader.GetString(8),
        AsOffset(reader.GetDateTime(9)), AsOffset(reader.GetDateTime(10)),
        reader.IsDBNull(11) ? null : AsOffset(reader.GetDateTime(11)),
        reader.IsDBNull(12) ? null : AsOffset(reader.GetDateTime(12)),
        reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
        reader.GetInt32(17), reader.GetBoolean(18));

    private const string BatchSelectSql = """
        SELECT b.id, b.account_id, a.name, b.file_name, b.file_type, b.file_size, b.file_sha256,
            b.starting_balance, b.status, b.created_at, b.expires_at, b.completed_at, b.undone_at,
            b.total_rows, b.importable_rows, b.imported_rows, b.skipped_rows, b.rejected_rows,
            b.status='completed' AND b.imported_rows > 0 AND NOT EXISTS (
                SELECT 1 FROM transaction_import_batches newer
                WHERE newer.account_id=b.account_id AND newer.status='completed' AND newer.imported_rows > 0
                    AND (newer.created_at > b.created_at OR (newer.created_at=b.created_at AND newer.id > b.id))
            ) AS can_undo
        FROM transaction_import_batches b JOIN accounts a ON a.id=b.account_id
        WHERE b.status IN ('preview','completed','undone')
        """;

    private sealed record ClassifiedRow(
        ParsedFinancialRow Source,
        TransactionDto? Transaction,
        string? Fingerprint,
        string Outcome,
        string? ReasonCode,
        string? ReasonMessage,
        decimal? BalanceAfter = null);

    private sealed record StagedRow(
        long Id,
        DateTime Date,
        decimal Amount,
        string Payee,
        string? Memo,
        string? FitId,
        string? TransactionType,
        string? Category,
        string? CheckNumber,
        string SourceFileType,
        decimal? StatementBalance,
        string Fingerprint);
}
