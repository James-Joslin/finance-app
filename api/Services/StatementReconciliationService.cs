using financesApi.models;
using financesApi.utilities;
using Npgsql;

namespace financesApi.services;

public static class StatementReconciliationService
{
    private const string SessionSummarySql = """
        WITH session_metrics AS (
            SELECT s.id,
                coalesce(sum(t.amount) FILTER (WHERE t.cleared), 0)::numeric(12, 2) AS cleared_amount,
                count(t.id)::int AS transaction_count,
                count(t.id) FILTER (WHERE t.cleared)::int AS cleared_transaction_count,
                coalesce(previous.statement_closing_balance, (
                    SELECT coalesce(sum(before_transactions.amount), 0)::numeric(12, 2)
                    FROM transactions before_transactions
                    WHERE before_transactions.account_id = s.account_id
                      AND before_transactions.transaction_date < s.period_start
                ))::numeric(12, 2) AS expected_opening_balance
            FROM statement_sessions s
            LEFT JOIN transactions t ON t.account_id = s.account_id
                AND t.transaction_date >= s.period_start
                AND t.transaction_date < s.period_end + 1
            LEFT JOIN LATERAL (
                SELECT previous.statement_closing_balance
                FROM statement_sessions previous
                WHERE previous.account_id = s.account_id
                  AND previous.status = 'closed'
                  AND previous.period_end < s.period_start
                ORDER BY previous.period_end DESC, previous.id DESC
                LIMIT 1
            ) previous ON true
            GROUP BY s.id, previous.statement_closing_balance
        )
        SELECT s.id, s.account_id, a.name, s.period_start, s.period_end,
            s.statement_opening_balance, s.statement_closing_balance,
            metrics.expected_opening_balance,
            s.statement_opening_balance - metrics.expected_opening_balance AS opening_discrepancy,
            s.statement_opening_balance + metrics.cleared_amount AS cleared_balance,
            s.statement_closing_balance - (s.statement_opening_balance + metrics.cleared_amount) AS closing_discrepancy,
            metrics.cleared_transaction_count, metrics.transaction_count,
            s.status, s.created_at, s.closed_at
        FROM statement_sessions s
        JOIN accounts a ON a.id = s.account_id
        JOIN session_metrics metrics ON metrics.id = s.id
        """;

    public static async Task<IReadOnlyList<StatementSessionDto>> GetSessionsAsync(int? accountId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        var filter = accountId.HasValue ? "WHERE s.account_id = @account" : string.Empty;
        await using var command = new NpgsqlCommand($"{SessionSummarySql} {filter} ORDER BY s.period_end DESC, s.id DESC", connection);
        if (accountId.HasValue) command.Parameters.AddWithValue("account", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var sessions = new List<StatementSessionDto>();
        while (await reader.ReadAsync()) sessions.Add(ReadSession(reader));
        return sessions;
    }

    public static async Task<StatementSessionDetailDto> GetSessionDetailAsync(int id)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        var session = await GetSessionAsync(connection, id);
        var transactions = await GetTransactionsAsync(connection, session);
        return new(session, transactions);
    }

    public static async Task<StatementSessionDetailDto> CreateSessionAsync(CreateStatementSessionRequest request)
    {
        ValidatePeriod(request.PeriodStart, request.PeriodEnd);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await EnsureActiveAccountAsync(connection, transaction, request.AccountId);
        await EnsureNoOverlappingSessionAsync(connection, transaction, request.AccountId, request.PeriodStart, request.PeriodEnd);

        const string sql = """
            INSERT INTO statement_sessions (
                account_id, period_start, period_end, statement_opening_balance, statement_closing_balance
            ) VALUES (@account, @start, @end, @opening, @closing)
            RETURNING id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("account", request.AccountId);
        command.Parameters.AddWithValue("start", request.PeriodStart);
        command.Parameters.AddWithValue("end", request.PeriodEnd);
        command.Parameters.AddWithValue("opening", request.StatementOpeningBalance);
        command.Parameters.AddWithValue("closing", request.StatementClosingBalance);
        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return await GetSessionDetailAsync(id);
    }

    public static async Task<StatementSessionDetailDto> SetTransactionClearedAsync(
        int sessionId, int transactionId, bool cleared)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var session = await LockSessionAsync(connection, transaction, sessionId);
        EnsureOpen(session);

        const string transactionSql = """
            SELECT t.is_reconciliation_adjustment
            FROM transactions t
            WHERE t.id = @transaction
              AND t.account_id = @account
              AND t.transaction_date >= @start
              AND t.transaction_date < @end + 1
            FOR UPDATE
            """;
        await using (var transactionCommand = new NpgsqlCommand(transactionSql, connection, transaction))
        {
            transactionCommand.Parameters.AddWithValue("transaction", transactionId);
            transactionCommand.Parameters.AddWithValue("account", session.AccountId);
            transactionCommand.Parameters.AddWithValue("start", session.PeriodStart);
            transactionCommand.Parameters.AddWithValue("end", session.PeriodEnd);
            await using var reader = await transactionCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ResourceNotFoundException("The transaction is not in this statement period.");
            if (reader.GetBoolean(0)) throw new ResourceConflictException("A reconciliation adjustment is managed by the reconciliation session.");
        }

        await using (var update = new NpgsqlCommand("UPDATE transactions SET cleared=@cleared WHERE id=@transaction", connection, transaction))
        {
            update.Parameters.AddWithValue("cleared", cleared);
            update.Parameters.AddWithValue("transaction", transactionId);
            await update.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return await GetSessionDetailAsync(sessionId);
    }

    public static async Task<StatementSessionDetailDto> UpsertAdjustmentAsync(int sessionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var session = await LockSessionAsync(connection, transaction, sessionId);
        EnsureOpen(session);
        var metrics = await ReadMetricsAsync(connection, transaction, session);
        var adjustmentAmount = session.StatementClosingBalance -
            (session.StatementOpeningBalance + metrics.ClearedAmount);
        if (adjustmentAmount == 0m)
            throw new ArgumentException("This statement has no closing discrepancy to adjust.");

        int? adjustmentId = null;
        await using (var existing = new NpgsqlCommand("""
            SELECT t.id
            FROM statement_session_transactions link
            JOIN transactions t ON t.id = link.transaction_id
            WHERE link.session_id=@session AND t.is_reconciliation_adjustment
            FOR UPDATE OF t
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("session", sessionId);
            adjustmentId = (int?)await existing.ExecuteScalarAsync();
        }

        if (adjustmentId.HasValue)
        {
            await using var update = new NpgsqlCommand("""
                UPDATE transactions
                SET transaction_date=@date, amount=@amount, cleared=true,
                    payee='Reconciliation adjustment', memo=@memo
                WHERE id=@id
                """, connection, transaction);
            update.Parameters.AddWithValue("date", session.PeriodEnd);
            update.Parameters.AddWithValue("amount", adjustmentAmount);
            update.Parameters.AddWithValue("memo", $"Statement reconciliation adjustment for {session.PeriodStart:yyyy-MM-dd} to {session.PeriodEnd:yyyy-MM-dd}");
            update.Parameters.AddWithValue("id", adjustmentId.Value);
            await update.ExecuteNonQueryAsync();
        }
        else
        {
            const string insertSql = """
                INSERT INTO transactions (
                    account_id, transaction_date, amount, payee, memo, transaction_type,
                    source_file_type, category_id, status, is_transfer, cleared, is_reconciliation_adjustment
                ) VALUES (
                    @account, @date, @amount, 'Reconciliation adjustment', @memo,
                    'Reconciliation adjustment', 'ADJUSTMENT',
                    (SELECT id FROM categories WHERE name='Reconciliation adjustment'),
                    'completed', false, true, true
                ) RETURNING id
                """;
            await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("account", session.AccountId);
            insert.Parameters.AddWithValue("date", session.PeriodEnd);
            insert.Parameters.AddWithValue("amount", adjustmentAmount);
            insert.Parameters.AddWithValue("memo", $"Statement reconciliation adjustment for {session.PeriodStart:yyyy-MM-dd} to {session.PeriodEnd:yyyy-MM-dd}");
            adjustmentId = Convert.ToInt32(await insert.ExecuteScalarAsync());
            await using var link = new NpgsqlCommand("INSERT INTO statement_session_transactions (session_id, transaction_id) VALUES (@session, @transaction)", connection, transaction);
            link.Parameters.AddWithValue("session", sessionId);
            link.Parameters.AddWithValue("transaction", adjustmentId.Value);
            await link.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return await GetSessionDetailAsync(sessionId);
    }

    public static async Task<StatementSessionDetailDto> DeleteAdjustmentAsync(int sessionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var session = await LockSessionAsync(connection, transaction, sessionId);
        EnsureOpen(session);
        int? adjustmentId;
        await using (var find = new NpgsqlCommand("""
            SELECT t.id FROM statement_session_transactions link
            JOIN transactions t ON t.id=link.transaction_id
            WHERE link.session_id=@session AND t.is_reconciliation_adjustment
            FOR UPDATE OF t
            """, connection, transaction))
        {
            find.Parameters.AddWithValue("session", sessionId);
            adjustmentId = (int?)await find.ExecuteScalarAsync();
        }
        if (!adjustmentId.HasValue) throw new ResourceNotFoundException("This statement has no reconciliation adjustment.");
        await using (var unlink = new NpgsqlCommand("DELETE FROM statement_session_transactions WHERE session_id=@session AND transaction_id=@transaction", connection, transaction))
        {
            unlink.Parameters.AddWithValue("session", sessionId);
            unlink.Parameters.AddWithValue("transaction", adjustmentId.Value);
            await unlink.ExecuteNonQueryAsync();
        }
        await using (var delete = new NpgsqlCommand("DELETE FROM transactions WHERE id=@transaction", connection, transaction))
        {
            delete.Parameters.AddWithValue("transaction", adjustmentId.Value);
            await delete.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return await GetSessionDetailAsync(sessionId);
    }

    public static async Task<StatementSessionDetailDto> CloseSessionAsync(int sessionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var session = await LockSessionAsync(connection, transaction, sessionId);
        EnsureOpen(session);
        var metrics = await ReadMetricsAsync(connection, transaction, session);
        var openingDiscrepancy = session.StatementOpeningBalance - metrics.ExpectedOpeningBalance;
        var closingDiscrepancy = session.StatementClosingBalance -
            (session.StatementOpeningBalance + metrics.ClearedAmount);
        if (openingDiscrepancy != 0m || closingDiscrepancy != 0m)
            throw new ResourceConflictException("The statement cannot be closed until the opening and closing discrepancies are zero.");

        await using (var link = new NpgsqlCommand("""
            INSERT INTO statement_session_transactions (session_id, transaction_id)
            SELECT @session, t.id
            FROM transactions t
            WHERE t.account_id=@account
              AND t.transaction_date >= @start
              AND t.transaction_date < @end + 1
              AND t.cleared
            ON CONFLICT (transaction_id) DO NOTHING
            """, connection, transaction))
        {
            link.Parameters.AddWithValue("session", sessionId);
            link.Parameters.AddWithValue("account", session.AccountId);
            link.Parameters.AddWithValue("start", session.PeriodStart);
            link.Parameters.AddWithValue("end", session.PeriodEnd);
            await link.ExecuteNonQueryAsync();
        }
        await using (var close = new NpgsqlCommand("UPDATE statement_sessions SET status='closed', closed_at=CURRENT_TIMESTAMP WHERE id=@id", connection, transaction))
        {
            close.Parameters.AddWithValue("id", sessionId);
            await close.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return await GetSessionDetailAsync(sessionId);
    }

    private static async Task<StatementSessionDto> GetSessionAsync(NpgsqlConnection connection, int id)
    {
        await using var command = new NpgsqlCommand($"{SessionSummarySql} WHERE s.id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Statement session was not found.");
        return ReadSession(reader);
    }

    private static async Task<List<ReconciliationTransactionDto>> GetTransactionsAsync(
        NpgsqlConnection connection, StatementSessionDto session)
    {
        const string sql = """
            SELECT t.id, t.transaction_date, t.amount, t.payee, t.memo,
                coalesce(c.name, 'Uncategorised'), t.status, t.cleared,
                t.is_reconciliation_adjustment,
                sum(t.amount) OVER (PARTITION BY t.account_id ORDER BY t.transaction_date, t.id)
            FROM transactions t
            LEFT JOIN categories c ON c.id=t.category_id
            WHERE t.account_id=@account
              AND t.transaction_date >= @start
              AND t.transaction_date < @end + 1
            ORDER BY t.transaction_date, t.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("account", session.AccountId);
        command.Parameters.AddWithValue("start", session.PeriodStart);
        command.Parameters.AddWithValue("end", session.PeriodEnd);
        await using var reader = await command.ExecuteReaderAsync();
        var transactions = new List<ReconciliationTransactionDto>();
        while (await reader.ReadAsync())
        {
            transactions.Add(new(
                reader.GetInt32(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetBoolean(7), reader.GetBoolean(8), reader.GetDecimal(9)));
        }
        return transactions;
    }

    private static async Task<SessionLock> LockSessionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int id)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, account_id, period_start, period_end, statement_opening_balance,
                statement_closing_balance, status
            FROM statement_sessions WHERE id=@id FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Statement session was not found.");
        return new(reader.GetInt32(0), reader.GetInt32(1), DateOnly.FromDateTime(reader.GetDateTime(2)),
            DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetString(6));
    }

    private static async Task<Metrics> ReadMetricsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SessionLock session)
    {
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT coalesce(sum(t.amount) FILTER (WHERE t.cleared), 0)::numeric(12,2)
                    FROM transactions t
                    WHERE t.account_id=@account
                      AND t.transaction_date >= @start
                      AND t.transaction_date < @end + 1),
                coalesce((
                    SELECT previous.statement_closing_balance
                    FROM statement_sessions previous
                    WHERE previous.account_id=@account
                      AND previous.status='closed'
                      AND previous.period_end < @start
                    ORDER BY previous.period_end DESC, previous.id DESC LIMIT 1
                ), (
                    SELECT coalesce(sum(before_transactions.amount), 0)::numeric(12,2)
                    FROM transactions before_transactions
                    WHERE before_transactions.account_id=@account
                      AND before_transactions.transaction_date < @start
                ))::numeric(12,2)
            """, connection, transaction);
        command.Parameters.AddWithValue("account", session.AccountId);
        command.Parameters.AddWithValue("start", session.PeriodStart);
        command.Parameters.AddWithValue("end", session.PeriodEnd);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new(0m, 0m);
        return new(reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task EnsureActiveAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=@account AND NOT is_archived", connection, transaction);
        command.Parameters.AddWithValue("account", accountId);
        if (await command.ExecuteScalarAsync() is null)
            throw new ResourceNotFoundException("An active account is required for reconciliation.");
    }

    private static async Task EnsureNoOverlappingSessionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId, DateOnly start, DateOnly end)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1 FROM statement_sessions
            WHERE account_id=@account AND period_start <= @end AND period_end >= @start
            LIMIT 1
            """, connection, transaction);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        if (await command.ExecuteScalarAsync() is not null)
            throw new ResourceConflictException("This statement period overlaps an existing reconciliation session.");
    }

    private static void ValidatePeriod(DateOnly start, DateOnly end)
    {
        if (end < start) throw new ArgumentException("Statement end date must be on or after the start date.");
    }

    private static void EnsureOpen(SessionLock session)
    {
        if (session.Status != "open") throw new ResourceConflictException("This statement session is already closed.");
    }

    private static StatementSessionDto ReadSession(NpgsqlDataReader reader)
    {
        var openingDiscrepancy = reader.GetDecimal(8);
        var closingDiscrepancy = reader.GetDecimal(10);
        var status = reader.GetString(13);
        return new(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
            DateOnly.FromDateTime(reader.GetDateTime(3)), DateOnly.FromDateTime(reader.GetDateTime(4)),
            reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), openingDiscrepancy,
            reader.GetDecimal(9), closingDiscrepancy, reader.GetInt32(11), reader.GetInt32(12), status,
            status == "open" && openingDiscrepancy == 0m && closingDiscrepancy == 0m,
            reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15));
    }

    private sealed record SessionLock(
        int Id, int AccountId, DateOnly PeriodStart, DateOnly PeriodEnd,
        decimal StatementOpeningBalance, decimal StatementClosingBalance, string Status);

    private sealed record Metrics(decimal ClearedAmount, decimal ExpectedOpeningBalance);
}
