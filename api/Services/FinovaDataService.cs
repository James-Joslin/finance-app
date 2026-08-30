using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using financesApi.models;
using financesApi.utilities;
using Npgsql;

namespace financesApi.services;

public static class FinovaDataService
{
    private static readonly string[] AccountTypes = ["current", "savings", "credit", "cash", "investment"];
    private static readonly string[] Frequencies = ["weekly", "fortnightly", "monthly", "quarterly", "yearly"];

    public static async Task<DateOnly> GetHouseholdTodayAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (CURRENT_TIMESTAMP AT TIME ZONE timezone)::date FROM household_settings WHERE id=1", connection);
        var value = await command.ExecuteScalarAsync();
        return value is DateTime date ? DateOnly.FromDateTime(date) : DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public static async Task<EnrollmentStatusDto> GetEnrollmentStatusAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT p.id, p.first_name, p.last_name, h.household_name
            FROM user_profiles p
            JOIN household_settings h ON h.id = 1
            WHERE p.id = 1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new(false, null);
        return new(true, new UserProfileDto(reader.GetInt16(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    }

    public static async Task<EnrollmentStatusDto> SaveEnrollmentAsync(SaveEnrollmentRequest request)
    {
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var householdName = request.HouseholdName?.Trim() ?? string.Empty;
        if (firstName.Length is < 1 or > 80) throw new ArgumentException("First name must be between 1 and 80 characters.");
        if (lastName.Length is < 1 or > 80) throw new ArgumentException("Last name must be between 1 and 80 characters.");
        if (householdName.Length is < 1 or > 120) throw new ArgumentException("Household name must be between 1 and 120 characters.");

        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            const string profileSql = """
                INSERT INTO user_profiles (id, first_name, last_name)
                VALUES (1, @first_name, @last_name)
                ON CONFLICT (id) DO UPDATE SET first_name = excluded.first_name,
                    last_name = excluded.last_name, updated_at = CURRENT_TIMESTAMP
                """;
            await using (var profile = new NpgsqlCommand(profileSql, connection, transaction))
            {
                profile.Parameters.AddWithValue("first_name", firstName);
                profile.Parameters.AddWithValue("last_name", lastName);
                await profile.ExecuteNonQueryAsync();
            }

            const string householdSql = """
                UPDATE household_settings SET household_name = @household_name,
                    updated_at = CURRENT_TIMESTAMP WHERE id = 1
                """;
            await using var household = new NpgsqlCommand(householdSql, connection, transaction);
            household.Parameters.AddWithValue("household_name", householdName);
            await household.ExecuteNonQueryAsync();
        });
        return await GetEnrollmentStatusAsync();
    }

    public static async Task<HouseholdSettingsDto> GetSettingsAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT household_name, currency_code, locale, timezone FROM household_settings WHERE id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new("My Household", "GBP", "en-GB", "Europe/London");
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public static async Task<HouseholdSettingsDto> UpdateSettingsAsync(UpdateHouseholdSettingsRequest request)
    {
        var householdName = request.HouseholdName?.Trim() ?? string.Empty;
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var locale = request.Locale?.Trim() ?? string.Empty;
        var timezone = request.Timezone?.Trim() ?? string.Empty;
        if (householdName.Length is < 1 or > 120) throw new ArgumentException("Household name must be between 1 and 120 characters.");
        if (currency.Length != 3 || !currency.All(char.IsLetter)) throw new ArgumentException("Currency must be a three-letter ISO code.");
        try { _ = CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { throw new ArgumentException("Locale is not recognised."); }
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Timezone is not recognised."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("Timezone is not valid."); }

        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            UPDATE household_settings SET household_name = @name, currency_code = @currency,
                locale = @locale, timezone = @timezone, updated_at = CURRENT_TIMESTAMP
            WHERE id = 1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", householdName);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("locale", locale);
        command.Parameters.AddWithValue("timezone", timezone);
        await command.ExecuteNonQueryAsync();
        return await GetSettingsAsync();
    }

    public static async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(bool includeArchived = false)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT a.id, a.name,
                coalesce(a.primary_holder_name, nullif(trim(concat_ws(' ', p.first_name, p.last_name)), ''), 'Household'),
                coalesce(a.is_shared, false), a.secondary_holder_name, a.account_type, a.institution,
                a.last_four, coalesce(sum(t.amount), 0), a.safe_zone_amount, a.include_in_safe_to_spend, a.is_archived, a.credit_limit
            FROM accounts a
            LEFT JOIN people p ON p.id = a.owner_id
            LEFT JOIN transactions t ON t.account_id = a.id
            WHERE (@include_archived OR NOT a.is_archived)
            GROUP BY a.id, p.first_name, p.last_name
            ORDER BY a.is_archived, a.name, a.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("include_archived", includeArchived);
        await using var reader = await command.ExecuteReaderAsync();
        var accounts = new List<AccountDto>();
        while (await reader.ReadAsync()) accounts.Add(ReadAccount(reader));
        return accounts;
    }

    public static async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request)
    {
        var primaryHolder = Clean(request.PrimaryHolderName) ??
            Clean(string.Join(' ', new[] { request.FirstName, request.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))));
        var secondaryHolder = Clean(request.SecondaryHolderName);
        ValidateAccount(request.AccountType, request.SafeZoneAmount, request.Name, request.IsShared, primaryHolder, secondaryHolder, request.CreditLimit);
        ValidateLastFour(request.LastFour);
        var accountId = 0;
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            var holderParts = primaryHolder!.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var first = holderParts[0];
            var last = holderParts.Length > 1 ? holderParts[1] : "";
            await using (var personLock = new NpgsqlCommand("LOCK TABLE people IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
                await personLock.ExecuteNonQueryAsync();
            int personId;
            await using (var existingPerson = new NpgsqlCommand(
                "SELECT id FROM people WHERE lower(first_name)=lower(@first) AND lower(last_name)=lower(@last) ORDER BY id LIMIT 1",
                connection, transaction))
            {
                existingPerson.Parameters.AddWithValue("first", first);
                existingPerson.Parameters.AddWithValue("last", last);
                var existingId = await existingPerson.ExecuteScalarAsync();
                if (existingId is not null)
                {
                    personId = Convert.ToInt32(existingId);
                }
                else
                {
                    await using var insertPerson = new NpgsqlCommand(
                        "INSERT INTO people (first_name, last_name) VALUES (@first, @last) RETURNING id", connection, transaction);
                    insertPerson.Parameters.AddWithValue("first", first);
                    insertPerson.Parameters.AddWithValue("last", last);
                    personId = Convert.ToInt32(await insertPerson.ExecuteScalarAsync());
                }
            }

            const string accountSql = """
                INSERT INTO accounts (name, owner_id, is_shared, account_type, institution, last_four,
                    safe_zone_amount, include_in_safe_to_spend, primary_holder_name, secondary_holder_name, credit_limit)
                VALUES (@name, @owner, @shared, @type, @institution, @last_four, @buffer, @include, @primary_holder, @secondary_holder, @credit_limit)
                RETURNING id
                """;
            await using var accountCommand = new NpgsqlCommand(accountSql, connection, transaction);
            accountCommand.Parameters.AddWithValue("name", request.Name.Trim());
            accountCommand.Parameters.AddWithValue("owner", personId);
            accountCommand.Parameters.AddWithValue("shared", request.IsShared);
            accountCommand.Parameters.AddWithValue("primary_holder", primaryHolder);
            accountCommand.Parameters.AddWithValue("secondary_holder", (object?)secondaryHolder ?? DBNull.Value);
            accountCommand.Parameters.AddWithValue("type", request.AccountType);
            accountCommand.Parameters.AddWithValue("institution", (object?)Clean(request.Institution) ?? DBNull.Value);
            accountCommand.Parameters.AddWithValue("last_four", (object?)Clean(request.LastFour) ?? DBNull.Value);
            accountCommand.Parameters.AddWithValue("buffer", request.AccountType == "credit" ? 0 : request.SafeZoneAmount);
            accountCommand.Parameters.AddWithValue("include", request.AccountType != "credit" && request.IncludeInSafeToSpend);
            accountCommand.Parameters.AddWithValue("credit_limit", request.AccountType == "credit" ? (object?)request.CreditLimit ?? DBNull.Value : DBNull.Value);
            accountId = Convert.ToInt32(await accountCommand.ExecuteScalarAsync());

            const string openingSql = """
                INSERT INTO transactions (account_id, transaction_date, amount, payee, memo, fitid,
                    transaction_type, source_file_type, category_id)
                VALUES (@account, @date, @amount, 'Opening balance', 'Opening balance', @fitid,
                    'Initial Deposit', 'MANUAL', (SELECT id FROM categories WHERE name = 'Transfers'))
                """;
            await using var openingCommand = new NpgsqlCommand(openingSql, connection, transaction);
            openingCommand.Parameters.AddWithValue("account", accountId);
            openingCommand.Parameters.AddWithValue("date", request.OpeningDate.ToDateTime(TimeOnly.MinValue));
            openingCommand.Parameters.AddWithValue("amount", request.AccountType == "credit" ? -Math.Abs(request.OpeningBalance) : request.OpeningBalance);
            openingCommand.Parameters.AddWithValue("fitid", $"opening-{accountId}");
            await openingCommand.ExecuteNonQueryAsync();
        });
        return (await GetAccountsAsync(true)).Single(a => a.Id == accountId);
    }

    public static async Task<AccountDto?> UpdateAccountAsync(int id, UpdateAccountRequest request)
    {
        var primaryHolder = Clean(request.PrimaryHolderName);
        var secondaryHolder = Clean(request.SecondaryHolderName);
        ValidateAccount(request.AccountType, request.SafeZoneAmount, request.Name, request.IsShared, primaryHolder, secondaryHolder, request.CreditLimit);
        ValidateLastFour(request.LastFour);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var typeCommand = new NpgsqlCommand("SELECT account_type FROM accounts WHERE id = @id", connection))
        {
            typeCommand.Parameters.AddWithValue("id", id);
            var existingType = await typeCommand.ExecuteScalarAsync() as string;
            if (existingType is null) return null;
            if ((existingType == "credit") != (request.AccountType == "credit"))
                throw new ArgumentException("An existing account cannot be changed between credit and asset account types because their transaction signs differ.");
        }
        const string sql = """
            UPDATE accounts SET name = @name, is_shared = @shared, account_type = @type,
                institution = @institution, last_four = @last_four, safe_zone_amount = @buffer,
                include_in_safe_to_spend = @include, is_archived = @archived,
                primary_holder_name = @primary_holder, secondary_holder_name = @secondary_holder, credit_limit = @credit_limit
            WHERE id = @id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("shared", request.IsShared);
        command.Parameters.AddWithValue("primary_holder", primaryHolder!);
        command.Parameters.AddWithValue("secondary_holder", (object?)secondaryHolder ?? DBNull.Value);
        command.Parameters.AddWithValue("type", request.AccountType);
        command.Parameters.AddWithValue("institution", (object?)Clean(request.Institution) ?? DBNull.Value);
        command.Parameters.AddWithValue("last_four", (object?)Clean(request.LastFour) ?? DBNull.Value);
        command.Parameters.AddWithValue("buffer", request.AccountType == "credit" ? 0 : request.SafeZoneAmount);
        command.Parameters.AddWithValue("include", request.AccountType != "credit" && request.IncludeInSafeToSpend);
        command.Parameters.AddWithValue("credit_limit", request.AccountType == "credit" ? (object?)request.CreditLimit ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("archived", request.IsArchived);
        if (await command.ExecuteNonQueryAsync() == 0) return null;
        return (await GetAccountsAsync(true)).Single(a => a.Id == id);
    }

    public static async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = "SELECT id, name, kind, icon_key, color_key, is_system FROM categories WHERE NOT is_archived ORDER BY kind, name";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<CategoryDto>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return rows;
    }

    public static async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var icon = request.IconKey?.Trim() ?? string.Empty;
        var color = request.ColorKey?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120) throw new ArgumentException("Category name must be between 1 and 120 characters.");
        if (kind is not ("income" or "expense" or "transfer")) throw new ArgumentException("Category kind must be income, expense, or transfer.");
        if (icon.Length is < 1 or > 40 || color.Length is < 1 or > 24) throw new ArgumentException("Category icon or colour is invalid.");
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var duplicate = new NpgsqlCommand("SELECT 1 FROM categories WHERE lower(name)=lower(@name)", connection))
        {
            duplicate.Parameters.AddWithValue("name", name);
            if (await duplicate.ExecuteScalarAsync() is not null) throw new ResourceConflictException("A category with that name already exists.");
        }
        const string sql = """
            INSERT INTO categories (name, kind, icon_key, color_key) VALUES (@name, @kind, @icon, @color)
            RETURNING id, name, kind, icon_key, color_key, is_system
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("icon", icon);
        command.Parameters.AddWithValue("color", color);
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException("A category with that name already exists.");
        }
    }

    public static async Task<IReadOnlyList<TransactionRuleDto>> GetTransactionRulesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT r.id, r.match_text, r.direction, r.category_id, c.name, r.priority, r.is_active
            FROM transaction_rules r JOIN categories c ON c.id = r.category_id
            ORDER BY r.match_text, r.direction
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TransactionRuleDto>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5), reader.GetBoolean(6)));
        return rows;
    }

    public static async Task<bool> DeleteTransactionRuleAsync(int id) =>
        await PostgreSqlQuerier.ExecuteNonQueryAsync("DELETE FROM transaction_rules WHERE id = @id", new() { ["id"] = id }) > 0;

    public static async Task<IReadOnlyList<TransactionTypeCodeDto>> GetTransactionTypeCodesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT code, meaning, institution FROM transaction_type_codes WHERE is_active ORDER BY code", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TransactionTypeCodeDto>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    public static async Task<TransactionPageDto> GetTransactionsAsync(
        int? accountId, int? categoryId, string? search, string type, DateOnly? startDate, DateOnly? endDate, int page, int pageSize)
    {
        if (type is not ("all" or "income" or "spending" or "transfer")) throw new ArgumentException("Unsupported transaction type filter.");
        if (startDate.HasValue && endDate.HasValue && endDate < startDate) throw new ArgumentException("End date must be on or after start date.");
        if (search?.Length > 200) throw new ArgumentException("Search text must be no longer than 200 characters.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 10000);
        var filters = new List<string> { "NOT a.is_archived" };
        if (accountId.HasValue) filters.Add("tx.account_id = @account_id");
        if (categoryId.HasValue) filters.Add("tx.category_id = @category_id");
        if (!string.IsNullOrWhiteSpace(search)) filters.Add("(tx.payee ILIKE @search OR tx.memo ILIKE @search OR a.name ILIKE @search)");
        if (type == "income") filters.Add("tx.amount > 0 AND a.account_type <> 'credit' AND NOT tx.is_transfer");
        if (type == "spending") filters.Add("tx.amount < 0 AND NOT tx.is_transfer");
        if (type == "transfer") filters.Add("tx.is_transfer");
        if (startDate.HasValue) filters.Add("tx.transaction_date >= @start_date");
        if (endDate.HasValue) filters.Add("tx.transaction_date < @end_date + interval '1 day'");
        var where = string.Join(" AND ", filters);
        var cte = """
            WITH tx AS (
                SELECT t.*, sum(t.amount) OVER (PARTITION BY t.account_id ORDER BY t.transaction_date, t.id) AS running_balance
                FROM transactions t
            )
            """;
        var countSql = $"{cte} SELECT count(*) FROM tx JOIN accounts a ON a.id = tx.account_id WHERE {where}";
        var dataSql = $"""
            {cte}
            SELECT tx.id, tx.account_id, a.name, a.account_type, tx.transaction_date, tx.amount, tx.payee, tx.memo,
                tx.transaction_type, tc.meaning, tx.category_id, coalesce(c.name, 'Uncategorised'), tx.status, tx.is_transfer,
                tx.source_file_type, tx.running_balance,
                (SELECT ro.recurring_item_id FROM recurring_occurrences ro WHERE ro.transaction_id = tx.id LIMIT 1)
            FROM tx JOIN accounts a ON a.id = tx.account_id
            LEFT JOIN categories c ON c.id = tx.category_id
            LEFT JOIN transaction_type_codes tc ON tc.code = upper(tx.transaction_type) AND tc.is_active
            WHERE {where}
            ORDER BY tx.transaction_date DESC, tx.id DESC
            LIMIT @limit OFFSET @offset
            """;
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        var total = 0;
        await using (var countCommand = new NpgsqlCommand(countSql, connection))
        {
            AddTransactionFilters(countCommand, accountId, categoryId, search, startDate, endDate);
            total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        }
        var items = new List<TransactionDtoV2>();
        await using (var command = new NpgsqlCommand(dataSql, connection))
        {
            AddTransactionFilters(command, accountId, categoryId, search, startDate, endDate);
            command.Parameters.AddWithValue("limit", pageSize);
            command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(ReadTransaction(reader));
        }
        return new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public static async Task<bool> UpdateTransactionCategoryAsync(int id, UpdateTransactionCategoryRequest request)
    {
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using (var category = new NpgsqlCommand("SELECT 1 FROM categories WHERE id=@id AND NOT is_archived", connection, transaction))
            {
                category.Parameters.AddWithValue("id", request.CategoryId);
                if (await category.ExecuteScalarAsync() is null) throw new ArgumentException("Category was not found.");
            }
            const string updateSql = """
                UPDATE transactions SET category_id = @category,
                    is_transfer = EXISTS (SELECT 1 FROM categories WHERE id = @category AND kind = 'transfer')
                WHERE id = @id
                """;
            await using var update = new NpgsqlCommand(updateSql, connection, transaction);
            update.Parameters.AddWithValue("category", request.CategoryId);
            update.Parameters.AddWithValue("id", id);
            if (await update.ExecuteNonQueryAsync() == 0) throw new ResourceNotFoundException("Transaction was not found.");
            if (request.SaveRule)
            {
                const string ruleSql = """
                    WITH source AS (
                        SELECT lower(trim(coalesce(nullif(payee, ''), memo))) AS reference_text,
                            CASE WHEN amount >= 0 THEN 'in' ELSE 'out' END AS direction
                        FROM transactions
                        WHERE id = @id AND nullif(trim(coalesce(nullif(payee, ''), memo)), '') IS NOT NULL
                    ), updated AS (
                        UPDATE transaction_rules rule SET
                            category_id = @category, is_active = true, updated_at = CURRENT_TIMESTAMP
                        FROM source
                        WHERE lower(trim(rule.match_text)) = source.reference_text
                            AND rule.direction = source.direction
                        RETURNING rule.id
                    )
                    INSERT INTO transaction_rules (match_text, direction, category_id)
                    SELECT source.reference_text, source.direction, @category
                    FROM source
                    WHERE NOT EXISTS (SELECT 1 FROM updated)
                    """;
                await using var rule = new NpgsqlCommand(ruleSql, connection, transaction);
                rule.Parameters.AddWithValue("category", request.CategoryId);
                rule.Parameters.AddWithValue("id", id);
                await rule.ExecuteNonQueryAsync();
            }
        });
        return true;
    }

    public static async Task<IReadOnlyList<RecurringItemDto>> GetRecurringItemsAsync(bool activeOnly = true)
    {
        await EnsureRecurringOccurrencesAsync();
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT r.id, r.name, r.kind, r.account_id, a.name, r.category_id, c.name, r.amount,
                r.frequency, coalesce(next_occurrence.due_date, r.next_date), r.source, r.is_active, r.match_text, r.amount_tolerance, r.date_window_days,
                coalesce(next_occurrence.status, CASE WHEN r.is_active THEN 'expected' ELSE 'paused' END), last_match.due_date
            FROM recurring_items r JOIN accounts a ON a.id = r.account_id
            LEFT JOIN categories c ON c.id = r.category_id
            LEFT JOIN LATERAL (SELECT ro.due_date, ro.status FROM recurring_occurrences ro WHERE ro.recurring_item_id=r.id AND ro.status='expected' ORDER BY ro.due_date LIMIT 1) next_occurrence ON true
            LEFT JOIN LATERAL (SELECT ro.due_date FROM recurring_occurrences ro WHERE ro.recurring_item_id=r.id AND ro.status IN ('matched', 'paid') ORDER BY ro.due_date DESC LIMIT 1) last_match ON true
            WHERE (@active_only = false OR r.is_active) AND NOT a.is_archived
            ORDER BY r.next_date, r.name
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("active_only", activeOnly);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<RecurringItemDto>();
        while (await reader.ReadAsync()) rows.Add(ReadRecurring(reader));
        return rows;
    }

    public static async Task<RecurringItemDto> SaveRecurringItemAsync(int? id, SaveRecurringItemRequest request)
    {
        ValidateRecurring(request);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var accountCommand = new NpgsqlCommand("SELECT account_type FROM accounts WHERE id = @account", connection))
        {
            accountCommand.Parameters.AddWithValue("account", request.AccountId);
            var accountType = await accountCommand.ExecuteScalarAsync() as string;
            if (accountType is null) throw new ArgumentException("Account was not found.");
            if (request.Kind == "income" && accountType == "credit")
                throw new ArgumentException("Credit-card repayments are not income. Record the payday against the account that receives it.");
        }
        if (request.CategoryId.HasValue)
        {
            await using var categoryCommand = new NpgsqlCommand("SELECT kind FROM categories WHERE id=@id AND NOT is_archived", connection);
            categoryCommand.Parameters.AddWithValue("id", request.CategoryId.Value);
            var categoryKind = await categoryCommand.ExecuteScalarAsync() as string;
            if (categoryKind is null || categoryKind == "transfer" || (request.Kind == "income" && categoryKind != "income") || (request.Kind == "bill" && categoryKind != "expense"))
                throw new ArgumentException("The selected category does not match the recurring item type.");
        }
        var identity = Clean(request.MatchText) ?? request.Name.Trim();
        const string duplicateSql = """
            SELECT r.id FROM recurring_items r
            WHERE r.account_id=@account AND r.kind=@kind AND r.id<>@current_id
                AND r.frequency=@frequency
                AND regexp_replace(lower(coalesce(nullif(trim(r.match_text), ''), r.name)), '[^a-z0-9]+', '', 'g') =
                    regexp_replace(lower(@identity), '[^a-z0-9]+', '', 'g')
                AND abs(r.next_date - @date::date) <= greatest(r.date_window_days, @date_window)
                AND abs(r.amount - @amount) <= greatest(r.amount_tolerance, @amount_tolerance)
            LIMIT 1
            """;
        await using (var duplicate = new NpgsqlCommand(duplicateSql, connection))
        {
            duplicate.Parameters.AddWithValue("account", request.AccountId);
            duplicate.Parameters.AddWithValue("kind", request.Kind);
            duplicate.Parameters.AddWithValue("current_id", id ?? 0);
            duplicate.Parameters.AddWithValue("frequency", request.Frequency);
            duplicate.Parameters.AddWithValue("identity", identity);
            duplicate.Parameters.AddWithValue("date", request.NextDate.ToDateTime(TimeOnly.MinValue));
            duplicate.Parameters.AddWithValue("date_window", request.DateWindowDays);
            duplicate.Parameters.AddWithValue("amount", Math.Abs(request.Amount));
            duplicate.Parameters.AddWithValue("amount_tolerance", request.AmountTolerance);
            if (await duplicate.ExecuteScalarAsync() is not null)
                throw new ResourceConflictException("A matching recurring plan already exists for this account, schedule, and reference. Edit the existing plan instead.");
        }
        var sql = id.HasValue
            ? @"UPDATE recurring_items SET name=@name, kind=@kind, account_id=@account, category_id=@category,
                amount=@amount, frequency=@frequency, next_date=@date, source=@source, is_active=@active,
                match_text=@match_text, amount_tolerance=@amount_tolerance, date_window_days=@date_window,
                source_transaction_id=coalesce(@source_transaction, source_transaction_id), updated_at=CURRENT_TIMESTAMP
                WHERE id=@id RETURNING id"
            : @"INSERT INTO recurring_items (name, kind, account_id, category_id, amount, frequency, next_date, source, is_active,
                match_text, amount_tolerance, date_window_days, source_transaction_id)
                VALUES (@name, @kind, @account, @category, @amount, @frequency, @date, @source, @active,
                @match_text, @amount_tolerance, @date_window, @source_transaction) RETURNING id";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("kind", request.Kind);
        command.Parameters.AddWithValue("account", request.AccountId);
        command.Parameters.AddWithValue("category", (object?)request.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("amount", Math.Abs(request.Amount));
        command.Parameters.AddWithValue("frequency", request.Frequency);
        command.Parameters.AddWithValue("date", request.NextDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("source", request.Source);
        command.Parameters.AddWithValue("active", request.IsActive);
        command.Parameters.AddWithValue("match_text", (object?)Clean(request.MatchText) ?? DBNull.Value);
        command.Parameters.AddWithValue("amount_tolerance", request.AmountTolerance);
        command.Parameters.AddWithValue("date_window", request.DateWindowDays);
        command.Parameters.AddWithValue("source_transaction", (object?)request.SourceTransactionId ?? DBNull.Value);
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        var savedId = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (savedId == 0) throw new ResourceNotFoundException("Recurring plan was not found.");
        await RefreshRecurringOccurrencesAsync(savedId);
        return (await GetRecurringItemsAsync(false)).Single(r => r.Id == savedId);
    }

    public static async Task<RecurringItemDto> MarkTransactionRecurringAsync(int transactionId, MarkTransactionRecurringRequest request)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT t.account_id, abs(t.amount), coalesce(nullif(trim(t.payee), ''), nullif(trim(t.memo), ''), 'Transaction'),
                t.category_id, t.amount, t.is_transfer, a.account_type,
                coalesce((SELECT ro.recurring_item_id FROM recurring_occurrences ro WHERE ro.transaction_id=t.id LIMIT 1),
                    (SELECT r.id FROM recurring_items r WHERE r.source_transaction_id=t.id LIMIT 1))
            FROM transactions t JOIN accounts a ON a.id=t.account_id WHERE t.id=@id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", transactionId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction was not found.");
        var accountId = reader.GetInt32(0);
        var importedAmount = reader.GetDecimal(1);
        var reference = reader.GetString(2);
        int? categoryId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
        var signedAmount = reader.GetDecimal(4);
        var isTransfer = reader.GetBoolean(5);
        var accountType = reader.GetString(6);
        var existingRecurringId = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
        await reader.DisposeAsync();
        if (existingRecurringId.HasValue)
            return (await GetRecurringItemsAsync(false)).Single(item => item.Id == existingRecurringId.Value);
        if (isTransfer) throw new ArgumentException("Transfers cannot be marked as household income or bills.");
        if (accountType == "credit" && signedAmount >= 0)
            throw new ArgumentException("A credit-card repayment should be planned against the account it is paid from.");
        var kind = signedAmount < 0 || accountType == "credit" ? "bill" : "income";
        try
        {
            return await SaveRecurringItemAsync(null, new(
                Clean(request.Name) ?? reference, kind, accountId, request.CategoryId ?? categoryId,
                request.Amount ?? importedAmount, request.Frequency, request.NextDate, "transaction", true,
                reference, request.AmountTolerance, request.DateWindowDays, transactionId));
        }
        catch (PostgresException exception) when (exception.ConstraintName == "recurring_items_source_transaction_key")
        {
            await using var lookup = new NpgsqlCommand("SELECT id FROM recurring_items WHERE source_transaction_id=@transaction", connection);
            lookup.Parameters.AddWithValue("transaction", transactionId);
            var recurringId = Convert.ToInt32(await lookup.ExecuteScalarAsync());
            return (await GetRecurringItemsAsync(false)).Single(item => item.Id == recurringId);
        }
    }

    public static async Task<IReadOnlyList<RecurringOccurrenceDto>> GetRecurringOccurrencesAsync(
        DateOnly? start = null, DateOnly? end = null, int? recurringItemId = null)
    {
        await EnsureRecurringOccurrencesAsync();
        var today = await GetHouseholdTodayAsync();
        start ??= today.AddDays(-31);
        end ??= today.AddMonths(6);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT ro.id, ro.recurring_item_id, r.name, r.kind, r.account_id, a.name, ro.due_date,
                ro.expected_amount, ro.status, ro.transaction_id, ro.actual_amount, ro.note
            FROM recurring_occurrences ro
            JOIN recurring_items r ON r.id=ro.recurring_item_id
            JOIN accounts a ON a.id=r.account_id
            WHERE ro.due_date BETWEEN @start AND @end AND (@item_id::integer IS NULL OR r.id=@item_id)
            ORDER BY ro.due_date, r.name
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("end", end.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("item_id", (object?)recurringItemId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<RecurringOccurrenceDto>();
        while (await reader.ReadAsync()) rows.Add(new(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5),
            DateOnly.FromDateTime(reader.GetDateTime(6)), reader.GetDecimal(7), reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
            reader.IsDBNull(11) ? null : reader.GetString(11)));
        return rows;
    }

    public static async Task<RecurringOccurrenceDto> UpdateRecurringOccurrenceAsync(int id, UpdateRecurringOccurrenceRequest request)
    {
        if (request.ExpectedAmount < 0) throw new ArgumentException("Expected amount cannot be negative.");
        if (request.Status is not ("expected" or "paid" or "skipped"))
            throw new ArgumentException("An occurrence can be expected, paid, or skipped.");
        const string sql = """
            UPDATE recurring_occurrences SET due_date=@date, expected_amount=@amount, status=@status,
                transaction_id=CASE WHEN @status IN ('expected', 'skipped') THEN NULL ELSE transaction_id END,
                actual_amount=CASE WHEN @status='paid' AND transaction_id IS NULL THEN @amount
                    WHEN @status='paid' THEN actual_amount ELSE NULL END,
                matched_at=CASE WHEN @status='paid' THEN coalesce(matched_at, CURRENT_TIMESTAMP) ELSE NULL END,
                note=@note, updated_at=CURRENT_TIMESTAMP
            WHERE id=@id RETURNING recurring_item_id
            """;
        var itemId = await PostgreSqlQuerier.ExecuteScalarAsync<int>(sql, new()
        {
            ["id"] = id,
            ["date"] = request.DueDate,
            ["amount"] = request.ExpectedAmount,
            ["status"] = request.Status,
            ["note"] = (object?)Clean(request.Note) ?? DBNull.Value,
        });
        if (itemId == 0) throw new ResourceNotFoundException("Recurring occurrence was not found.");
        return (await GetRecurringOccurrencesAsync(request.DueDate.AddDays(-1), request.DueDate.AddDays(1), Convert.ToInt32(itemId)))
            .Single(x => x.Id == id);
    }

    public static async Task ReconcileRecurringTransactionsAsync(int accountId, DateOnly start, DateOnly end)
    {
        await EnsureRecurringOccurrencesAsync();
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            WITH candidates AS (
                SELECT ro.id occurrence_id, t.id transaction_id, abs(t.amount) actual_amount,
                    row_number() OVER (PARTITION BY t.id ORDER BY abs(t.transaction_date::date - ro.due_date), abs(abs(t.amount) - ro.expected_amount), ro.id) tx_rank,
                    row_number() OVER (PARTITION BY ro.id ORDER BY abs(t.transaction_date::date - ro.due_date), abs(abs(t.amount) - ro.expected_amount), t.id) occurrence_rank
                FROM recurring_occurrences ro
                JOIN recurring_items r ON r.id=ro.recurring_item_id AND r.is_active
                JOIN transactions t ON t.account_id=r.account_id AND NOT t.is_transfer
                    AND t.transaction_date BETWEEN ro.due_date - r.date_window_days AND ro.due_date + r.date_window_days
                    AND abs(abs(t.amount) - ro.expected_amount) <= r.amount_tolerance
                    AND ((r.kind='bill' AND t.amount < 0) OR (r.kind='income' AND t.amount > 0))
                    AND regexp_replace(lower(coalesce(t.payee, t.memo, '')), '[^a-z0-9]+', '', 'g') LIKE
                        '%' || regexp_replace(lower(coalesce(r.match_text, r.name)), '[^a-z0-9]+', '', 'g') || '%'
                WHERE ro.status='expected' AND r.account_id=@account AND t.transaction_date BETWEEN @start AND @end
                    AND NOT EXISTS (SELECT 1 FROM recurring_occurrences linked WHERE linked.transaction_id=t.id)
            )
            UPDATE recurring_occurrences ro SET status='matched', transaction_id=c.transaction_id,
                actual_amount=c.actual_amount, matched_at=CURRENT_TIMESTAMP, updated_at=CURRENT_TIMESTAMP
            FROM candidates c WHERE ro.id=c.occurrence_id AND c.tx_rank=1 AND c.occurrence_rank=1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("start", start.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("end", end.ToDateTime(TimeOnly.MinValue));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RefreshRecurringOccurrencesAsync(int recurringItemId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM recurring_occurrences WHERE recurring_item_id=@id AND status IN ('expected', 'skipped')", connection, transaction))
        {
            delete.Parameters.AddWithValue("id", recurringItemId);
            await delete.ExecuteNonQueryAsync();
        }
        await PopulateRecurringOccurrencesAsync(connection, transaction, recurringItemId, true);
        await transaction.CommitAsync();
    }

    private static async Task EnsureRecurringOccurrencesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var ids = new List<int>();
        await using (var command = new NpgsqlCommand("SELECT id FROM recurring_items WHERE is_active", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        foreach (var id in ids) await PopulateRecurringOccurrencesAsync(connection, transaction, id, false);
        await transaction.CommitAsync();
    }

    private static async Task PopulateRecurringOccurrencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int itemId, bool linkSource)
    {
        const string itemSql = "SELECT next_date, frequency, amount, source_transaction_id FROM recurring_items WHERE id=@id AND is_active";
        DateOnly next;
        string frequency;
        decimal amount;
        int? sourceTransactionId;
        await using (var item = new NpgsqlCommand(itemSql, connection, transaction))
        {
            item.Parameters.AddWithValue("id", itemId);
            await using var reader = await item.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return;
            next = DateOnly.FromDateTime(reader.GetDateTime(0));
            frequency = reader.GetString(1);
            amount = reader.GetDecimal(2);
            sourceTransactionId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
        }
        if (linkSource && sourceTransactionId.HasValue)
        {
            const string matchedSql = """
                INSERT INTO recurring_occurrences (recurring_item_id, due_date, expected_amount, status, transaction_id, actual_amount, matched_at)
                SELECT @item, t.transaction_date, abs(t.amount), 'matched', t.id, abs(t.amount), CURRENT_TIMESTAMP
                FROM transactions t WHERE t.id=@transaction
                ON CONFLICT DO NOTHING
                """;
            await using var matched = new NpgsqlCommand(matchedSql, connection, transaction);
            matched.Parameters.AddWithValue("item", itemId);
            matched.Parameters.AddWithValue("transaction", sourceTransactionId.Value);
            await matched.ExecuteNonQueryAsync();
        }
        var today = await GetHouseholdTodayAsync();
        next = NormalizeNextDate(next, frequency, today.AddDays(-31));
        var horizon = today.AddMonths(18);
        for (var occurrence = 0; ; occurrence++)
        {
            var date = OccurrenceDate(next, frequency, occurrence);
            if (date > horizon) break;
            await using var insert = new NpgsqlCommand(
                "INSERT INTO recurring_occurrences (recurring_item_id, due_date, expected_amount) VALUES (@item, @date, @amount) ON CONFLICT DO NOTHING",
                connection, transaction);
            insert.Parameters.AddWithValue("item", itemId);
            insert.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
            insert.Parameters.AddWithValue("amount", amount);
            await insert.ExecuteNonQueryAsync();
        }

    }

    public static async Task<bool> DeleteRecurringItemAsync(int id) =>
        await PostgreSqlQuerier.ExecuteNonQueryAsync("DELETE FROM recurring_items WHERE id=@id", new() { ["id"] = id }) > 0;

    public static async Task<IReadOnlyList<AccountSafetyDto>> GetAccountSafetyAsync()
    {
        var accounts = await GetAccountsAsync();
        var today = await GetHouseholdTodayAsync();
        var occurrences = await GetRecurringOccurrencesAsync(today.AddDays(-31), today.AddDays(400));
        var results = new List<AccountSafetyDto>();
        foreach (var account in accounts)
        {
            if (account.AccountType == "credit")
            {
                results.Add(new(account.Id, account.Name, account.AccountType, account.Balance, account.DebtBalance,
                    account.CreditLimit, account.AvailableCredit, account.CreditUtilizationPercent,
                    0, 0, today.AddDays(30), 0, 0));
                continue;
            }
            var accountOccurrences = occurrences.Where(o => o.AccountId == account.Id && o.Status == "expected").ToList();
            var nextIncome = accountOccurrences.Where(o => o.Kind == "income" && o.DueDate >= today)
                .OrderBy(o => o.DueDate).Select(o => (DateOnly?)o.DueDate).FirstOrDefault();
            var horizon = nextIncome?.AddDays(-1) ?? today.AddDays(30);
            var bills = accountOccurrences.Where(o => o.Kind == "bill" && o.DueDate <= horizon).Sum(o => o.ExpectedAmount);
            var calculated = FinanceMath.CalculateSafety(account.Balance, account.SafeZoneAmount, bills);
            results.Add(new(account.Id, account.Name, account.AccountType, account.Balance, 0, null, null, null, account.SafeZoneAmount, bills, horizon,
                calculated.SafeToSpend, calculated.Shortfall));
        }
        return results;
    }

    public static async Task<GoalSummaryDto> GetGoalsAsync(bool includeArchived = false)
    {
        var safety = (await GetAccountSafetyAsync()).ToDictionary(x => x.AccountId, x => x);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT g.id, g.name, g.description, g.target_amount, g.target_date, g.account_id, a.name,
                g.priority_order, g.icon_key, g.color_key, g.image_id, g.status
            FROM savings_goals g JOIN accounts a ON a.id = g.account_id
            WHERE (@include_archived OR g.status <> 'archived')
            ORDER BY g.priority_order, g.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("include_archived", includeArchived);
        await using var reader = await command.ExecuteReaderAsync();
        var rawGoals = new List<GoalRow>();
        while (await reader.ReadAsync())
        {
            rawGoals.Add(new(
                reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3), reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7), reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.GetString(11)));
        }
        var pools = safety.ToDictionary(kv => kv.Key, kv => kv.Value.AccountType == "credit" ? 0 : Math.Max(0, kv.Value.Balance - kv.Value.BufferAmount - kv.Value.UpcomingBills));
        var today = await GetHouseholdTodayAsync();
        var goals = new List<GoalDto>();
        foreach (var goal in rawGoals)
        {
            var pool = pools.GetValueOrDefault(goal.AccountId);
            var allocation = goal.Status == "archived"
                ? new GoalAllocationResult(0, pool)
                : FinanceMath.AllocateGoal(pool, goal.TargetAmount);
            var allocated = allocation.Allocated;
            if (goal.Status != "archived") pools[goal.AccountId] = allocation.RemainingPool;
            var remaining = Math.Max(0, goal.TargetAmount - allocated);
            var pace = FinanceMath.CalculateGoalPace(remaining, goal.TargetDate, today);
            goals.Add(new(
                goal.Id, goal.Name, goal.Description, goal.TargetAmount, goal.TargetDate, goal.AccountId, goal.AccountName,
                goal.Priority, goal.IconKey, goal.ColorKey, goal.ImageId,
                goal.ImageId.HasValue ? $"/api/goals/images/{goal.ImageId}" : null, goal.Status, allocated, remaining,
                goal.TargetAmount == 0 ? 0 : Math.Round(allocated / goal.TargetAmount * 100, 1), pace.DaysRemaining,
                pace.Weekly, pace.Monthly, allocated >= goal.TargetAmount));
        }
        var active = goals.Where(g => g.Status != "archived").ToList();
        var allocatedTotal = active.Sum(g => g.AllocatedAmount);
        var targetTotal = active.Sum(g => g.TargetAmount);
        return new(goals, allocatedTotal, targetTotal, targetTotal == 0 ? 0 : Math.Round(allocatedTotal / targetTotal * 100, 1));
    }

    public static async Task<GoalDto> SaveGoalAsync(int? id, SaveGoalRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 160 || request.TargetAmount <= 0) throw new ArgumentException("A goal name and positive target are required.");
        if (request.PriorityOrder < 1) throw new ArgumentException("Goal priority must be at least one.");
        if (request.Status is not ("active" or "completed" or "archived")) throw new ArgumentException("Goal status must be active, completed, or archived.");
        if (string.IsNullOrWhiteSpace(request.IconKey) || request.IconKey.Length > 40 || string.IsNullOrWhiteSpace(request.ColorKey) || request.ColorKey.Length > 24)
            throw new ArgumentException("Goal icon or colour is invalid.");
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var account = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=@id AND account_type <> 'credit' AND NOT is_archived", connection))
        {
            account.Parameters.AddWithValue("id", request.AccountId);
            if (await account.ExecuteScalarAsync() is null) throw new ArgumentException("An active non-credit account is required for a goal.");
        }
        if (request.ImageId.HasValue)
        {
            await using var image = new NpgsqlCommand("SELECT 1 FROM goal_images WHERE id=@id", connection);
            image.Parameters.AddWithValue("id", request.ImageId.Value);
            if (await image.ExecuteScalarAsync() is null) throw new ArgumentException("Goal image was not found.");
        }
        int? previousImageId = null;
        if (id.HasValue)
        {
            await using var previousImage = new NpgsqlCommand("SELECT image_id FROM savings_goals WHERE id=@id", connection);
            previousImage.Parameters.AddWithValue("id", id.Value);
            var previous = await previousImage.ExecuteScalarAsync();
            previousImageId = previous is null or DBNull ? null : Convert.ToInt32(previous);
        }

        var sql = id.HasValue
            ? @"UPDATE savings_goals SET name=@name, description=@description, target_amount=@target,
                target_date=@date, account_id=@account, priority_order=@priority, icon_key=@icon,
                color_key=@color, image_id=@image, status=@status, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id RETURNING id"
            : @"INSERT INTO savings_goals (name, description, target_amount, target_date, account_id,
                priority_order, icon_key, color_key, image_id, status)
                VALUES (@name, @description, @target, @date, @account, @priority, @icon, @color, @image, @status)
                RETURNING id";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("description", (object?)Clean(request.Description) ?? DBNull.Value);
        command.Parameters.AddWithValue("target", request.TargetAmount);
        command.Parameters.AddWithValue("date", request.TargetDate.HasValue ? request.TargetDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        command.Parameters.AddWithValue("account", request.AccountId);
        command.Parameters.AddWithValue("priority", request.PriorityOrder);
        command.Parameters.AddWithValue("icon", request.IconKey);
        command.Parameters.AddWithValue("color", request.ColorKey);
        command.Parameters.AddWithValue("image", (object?)request.ImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", request.Status);
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        var savedId = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (savedId == 0) throw new ResourceNotFoundException("Goal was not found.");
        if (previousImageId.HasValue && previousImageId != request.ImageId)
        {
            await using var cleanup = new NpgsqlCommand(
                "DELETE FROM goal_images WHERE id=@id AND NOT EXISTS (SELECT 1 FROM savings_goals WHERE image_id=@id)", connection);
            cleanup.Parameters.AddWithValue("id", previousImageId.Value);
            await cleanup.ExecuteNonQueryAsync();
        }
        return (await GetGoalsAsync(true)).Items.Single(g => g.Id == savedId);
    }

    public static async Task ReorderGoalsAsync(IReadOnlyList<int> ids)
    {
        if (ids.Count != ids.Distinct().Count() || ids.Any(id => id <= 0)) throw new ArgumentException("Goal order contains invalid or duplicate IDs.");
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            for (var index = 0; index < ids.Count; index++)
            {
                await using var command = new NpgsqlCommand("UPDATE savings_goals SET priority_order=@priority, updated_at=CURRENT_TIMESTAMP WHERE id=@id", connection, transaction);
                command.Parameters.AddWithValue("priority", index + 1);
                command.Parameters.AddWithValue("id", ids[index]);
                if (await command.ExecuteNonQueryAsync() == 0) throw new ResourceNotFoundException($"Goal {ids[index]} was not found.");
            }
        });
    }

    public static async Task<int> SaveGoalImageAsync(IFormFile file)
    {
        if (file.Length == 0 || file.Length > 2 * 1024 * 1024) throw new ArgumentException("Images must be between 1 byte and 2 MB.");
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var bytes = memory.ToArray();
        var type = DetectImageType(bytes) ?? throw new ArgumentException("Only PNG, JPEG, and WebP images are supported.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO goal_images (content_type, file_name, content, content_hash)
            VALUES (@type, @name, @content, @hash) RETURNING id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("name", Path.GetFileName(file.FileName));
        command.Parameters.AddWithValue("content", bytes);
        command.Parameters.AddWithValue("hash", hash);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task<(byte[] Content, string ContentType, string Hash)?> GetGoalImageAsync(int id)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT content, content_type, content_hash FROM goal_images WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return ((byte[])reader[0], reader.GetString(1), reader.GetString(2));
    }

    public static async Task<bool> DeleteGoalImageAsync(int id)
    {
        const string sql = "DELETE FROM goal_images WHERE id=@id AND NOT EXISTS (SELECT 1 FROM savings_goals WHERE image_id=@id)";
        return await PostgreSqlQuerier.ExecuteNonQueryAsync(sql, new() { ["id"] = id }) > 0;
    }

    public static async Task<BudgetDto> SaveBudgetAsync(SaveBudgetRequest request)
    {
        if (request.MonthlyAmount < 0) throw new ArgumentException("Budget amount cannot be negative.");
        var today = await GetHouseholdTodayAsync();
        var month = new DateOnly(today.Year, today.Month, 1);
        const string sql = """
            INSERT INTO budget_definitions (category_id, monthly_amount, rollover_enabled, effective_from)
            VALUES (@category, @amount, @rollover, @month)
            ON CONFLICT (category_id) DO UPDATE SET monthly_amount=excluded.monthly_amount,
                rollover_enabled=excluded.rollover_enabled, updated_at=CURRENT_TIMESTAMP, is_active=true
            RETURNING id
        """;
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var category = new NpgsqlCommand(
            "SELECT 1 FROM categories WHERE id=@id AND kind='expense' AND NOT is_archived", connection, transaction))
        {
            category.Parameters.AddWithValue("id", request.CategoryId);
            if (await category.ExecuteScalarAsync() is null) throw new ArgumentException("An active expense category is required for a budget.");
        }
        const string preserveSql = """
            INSERT INTO budget_months (budget_id, month, base_amount, rollover_in, spent_amount, rollover_enabled)
            SELECT b.id, series.month::date, b.monthly_amount, 0, 0, b.rollover_enabled
            FROM budget_definitions b
            CROSS JOIN LATERAL generate_series(
                date_trunc('month', b.effective_from)::date,
                @month::date - interval '1 month', interval '1 month') AS series(month)
            WHERE b.category_id=@category
            ON CONFLICT (budget_id, month) DO NOTHING
            """;
        await using (var preserve = new NpgsqlCommand(preserveSql, connection, transaction))
        {
            preserve.Parameters.AddWithValue("category", request.CategoryId);
            preserve.Parameters.AddWithValue("month", month.ToDateTime(TimeOnly.MinValue));
            await preserve.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("category", request.CategoryId);
        command.Parameters.AddWithValue("amount", request.MonthlyAmount);
        command.Parameters.AddWithValue("rollover", request.RolloverEnabled);
        command.Parameters.AddWithValue("month", month.ToDateTime(TimeOnly.MinValue));
        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return (await GetBudgetsAsync(month)).Single(b => b.Id == id);
    }

    public static async Task<IReadOnlyList<BudgetDto>> GetBudgetsAsync(DateOnly? requestedMonth = null)
    {
        await EnsureRecurringOccurrencesAsync();
        var today = await GetHouseholdTodayAsync();
        var month = requestedMonth.HasValue ? new DateOnly(requestedMonth.Value.Year, requestedMonth.Value.Month, 1) : new(today.Year, today.Month, 1);
        var nextMonth = month.AddMonths(1);
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT b.id, b.category_id, c.name, c.icon_key, c.color_key, b.monthly_amount, b.rollover_enabled,
                coalesce((SELECT sum(ro.expected_amount) FROM recurring_occurrences ro
                    JOIN recurring_items r ON r.id=ro.recurring_item_id
                    WHERE r.category_id=b.category_id AND r.kind='bill' AND r.is_active AND ro.status='expected'
                    AND ro.due_date >= @month AND ro.due_date < @next_month), 0),
                date_trunc('month', b.effective_from)::date
            FROM budget_definitions b JOIN categories c ON c.id=b.category_id
            WHERE b.is_active AND b.effective_from < @next_month ORDER BY c.name
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("month", month.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("next_month", nextMonth.ToDateTime(TimeOnly.MinValue));
        var raw = new List<BudgetDefinitionRow>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) raw.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDecimal(5), reader.GetBoolean(6), reader.GetDecimal(7), DateOnly.FromDateTime(reader.GetDateTime(8))));
        }
        var result = new List<BudgetDto>();
        foreach (var item in raw)
        {
            const string historySql = """
                SELECT months.month,
                    coalesce(bm.base_amount, @current_amount),
                    coalesce(bm.rollover_enabled, @current_rollover),
                    coalesce(sum(CASE WHEN t.amount < 0 AND NOT t.is_transfer AND NOT a.is_archived THEN abs(t.amount) ELSE 0 END), 0)
                FROM generate_series(@effective::date, @month::date, interval '1 month') AS months(month)
                LEFT JOIN budget_months bm ON bm.budget_id=@budget AND bm.month=months.month::date
                LEFT JOIN transactions t ON t.category_id=@category
                    AND t.transaction_date >= months.month AND t.transaction_date < months.month + interval '1 month'
                LEFT JOIN accounts a ON a.id=t.account_id
                GROUP BY months.month, bm.base_amount, bm.rollover_enabled
                ORDER BY months.month
                """;
            await using var history = new NpgsqlCommand(historySql, connection);
            history.Parameters.AddWithValue("effective", item.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
            history.Parameters.AddWithValue("month", month.ToDateTime(TimeOnly.MinValue));
            history.Parameters.AddWithValue("budget", item.Id);
            history.Parameters.AddWithValue("category", item.CategoryId);
            history.Parameters.AddWithValue("current_amount", item.Amount);
            history.Parameters.AddWithValue("current_rollover", item.Rollover);
            var priorRemaining = 0m;
            await using var historyReader = await history.ExecuteReaderAsync();
            while (await historyReader.ReadAsync())
            {
                var rowMonth = DateOnly.FromDateTime(historyReader.GetDateTime(0));
                var baseAmount = historyReader.GetDecimal(1);
                var rollover = historyReader.GetBoolean(2);
                var spent = historyReader.GetDecimal(3);
                var calculated = FinanceMath.CalculateBudget(baseAmount, rollover, priorRemaining, spent);
                if (rowMonth == month)
                    result.Add(new(item.Id, item.CategoryId, item.Name, item.Icon, item.Color, baseAmount, rollover,
                        calculated.RolloverIn, calculated.Available, spent, item.Scheduled,
                        calculated.Remaining - item.Scheduled, calculated.Remaining, calculated.ProgressPercent));
                priorRemaining = calculated.Remaining;
            }
        }
        return result;
    }

    public static async Task<IReadOnlyList<RecurringSuggestionDto>> GetRecurringSuggestionsAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT t.account_id, a.name, coalesce(nullif(trim(t.payee), ''), nullif(trim(t.memo), ''), 'Unknown'),
                t.transaction_date, t.amount
            FROM transactions t JOIN accounts a ON a.id=t.account_id
            WHERE t.transaction_date >= CURRENT_DATE - interval '400 days' AND NOT t.is_transfer
                AND coalesce(t.transaction_type, '') <> 'Initial Deposit' AND NOT a.is_archived
                AND (a.account_type <> 'credit' OR t.amount < 0)
            ORDER BY t.account_id, lower(coalesce(t.payee, t.memo)), t.transaction_date
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var patterns = new List<PatternRow>();
        while (await reader.ReadAsync()) patterns.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4)));
        var existing = (await GetRecurringItemsAsync()).Select(r => (r.AccountId, NormalizeText(r.Name))).ToHashSet();
        var suggestions = new List<RecurringSuggestionDto>();
        var today = await GetHouseholdTodayAsync();
        foreach (var group in patterns.GroupBy(p => (p.AccountId, Key: NormalizeText(p.Name), Kind: p.Amount > 0 ? "income" : "bill")))
        {
            var rows = group.OrderBy(x => x.Date).ToList();
            if (rows.Count < 3 || string.IsNullOrWhiteSpace(group.Key.Key) || existing.Contains((group.Key.AccountId, group.Key.Key))) continue;
            var gaps = rows.Zip(rows.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).ToList();
            var averageGap = gaps.Average();
            var frequency = FrequencyFromGap(averageGap);
            if (frequency is null) continue;
            var tolerance = Math.Max(3, averageGap * .25);
            if (gaps.Any(g => Math.Abs(g - averageGap) > tolerance)) continue;
            var amounts = rows.Select(x => Math.Abs(x.Amount)).ToList();
            var averageAmount = amounts.Average();
            if (averageAmount == 0 || amounts.Any(a => Math.Abs(a - averageAmount) / averageAmount > .15m)) continue;
            var next = NormalizeNextDate(rows[^1].Date, frequency, today);
            var confidence = Math.Min(.99m, .65m + rows.Count * .04m);
            suggestions.Add(new(rows[^1].Name, group.Key.Kind, group.Key.AccountId, rows[^1].AccountName,
                Math.Round(averageAmount, 2), frequency, next, rows.Count, confidence));
        }
        return suggestions.OrderByDescending(s => s.Confidence).Take(12).ToList();
    }

    public static async Task<DashboardDto> GetDashboardAsync()
    {
        var settings = await GetSettingsAsync();
        var accounts = await GetAccountsAsync();
        var safety = await GetAccountSafetyAsync();
        var recurring = await GetRecurringItemsAsync();
        var goals = await GetGoalsAsync();
        var budgets = await GetBudgetsAsync();
        var recent = (await GetTransactionsAsync(null, null, null, "all", null, null, 1, 6)).Items;
        var included = accounts.Where(a => a.AccountType != "credit" && a.IncludeInSafeToSpend).Select(a => a.Id).ToHashSet();
        var includedSafety = safety.Where(s => included.Contains(s.AccountId)).ToList();
        var position = FinanceMath.CalculateHouseholdPosition(accounts.Select(a => a.Balance));
        var today = await GetHouseholdTodayAsync();
        var creditAccountIds = accounts.Where(a => a.AccountType == "credit").Select(a => a.Id).ToHashSet();
        var nextPayday = recurring.Where(r => r.Kind == "income" && !creditAccountIds.Contains(r.AccountId))
            .Select(r => NormalizeNextDate(r.NextDate, r.Frequency, today)).OrderBy(d => d).Select(d => (DateOnly?)d).FirstOrDefault();
        var alerts = new List<string>();
        if (includedSafety.Sum(s => s.Shortfall) > 0) alerts.Add("One or more accounts are below their protected plan.");
        if (budgets.Any(b => b.ProgressPercent >= 90)) alerts.Add("A monthly budget is close to or over its limit.");
        if (accounts.Any(a => a.AccountType == "credit" && a.CreditUtilizationPercent >= 80)) alerts.Add("A credit card is using 80% or more of its limit.");
        var priorityGoal = goals.Items.FirstOrDefault(g => g.Status == "active" && !g.IsFunded) ?? goals.Items.FirstOrDefault(g => g.Status == "active");
        if (priorityGoal is { DaysRemaining: < 0 }) alerts.Add($"{priorityGoal.Name} has passed its target date.");
        return new(settings.HouseholdName, position.NetPosition, position.Assets, position.Debt, includedSafety.Sum(s => s.SafeToSpend),
            includedSafety.Sum(s => s.BufferAmount), includedSafety.Sum(s => s.UpcomingBills), includedSafety.Sum(s => s.Shortfall),
            nextPayday, safety, recent, priorityGoal, budgets.Where(b => b.ProgressPercent >= 80).OrderByDescending(b => b.ProgressPercent).Take(3).ToList(), alerts);
    }

    public static async Task<DateOnly> GetEarliestInsightsDateAsync(DateOnly end)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT min(t.transaction_date)::date
            FROM transactions t
            JOIN accounts a ON a.id = t.account_id
            WHERE NOT a.is_archived AND t.transaction_date <= @end
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("end", end.ToDateTime(TimeOnly.MaxValue));
        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            DateOnly earliest => earliest,
            DateTime earliest => DateOnly.FromDateTime(earliest),
            _ => end,
        };
    }

    public static async Task<InsightsDto> GetInsightsAsync(DateOnly start, DateOnly end)
    {
        if (end < start) throw new ArgumentException("End date must be on or after start date.");
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string transactionSql = """
            SELECT t.transaction_date::date, t.amount, t.category_id, coalesce(c.name, 'Uncategorised'),
                coalesce(c.color_key, 'slate'), t.is_transfer, coalesce(t.transaction_type, ''), a.account_type
            FROM transactions t JOIN accounts a ON a.id=t.account_id LEFT JOIN categories c ON c.id=t.category_id
            WHERE NOT a.is_archived AND t.transaction_date <= @end ORDER BY t.transaction_date, t.id
            """;
        await using var command = new NpgsqlCommand(transactionSql, connection);
        command.Parameters.AddWithValue("end", end.ToDateTime(TimeOnly.MaxValue));
        var balance = 0m;
        var openingBalance = 0m;
        var income = 0m;
        var spending = 0m;
        var uncategorised = 0m;
        var daily = new SortedDictionary<DateOnly, decimal>();
        var incomeDaily = new SortedDictionary<DateOnly, decimal>();
        var spendingDaily = new SortedDictionary<DateOnly, decimal>();
        var category = new Dictionary<(int?, string, string), decimal>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var date = DateOnly.FromDateTime(reader.GetDateTime(0));
                var amount = reader.GetDecimal(1);
                balance += amount;
                if (date < start)
                {
                    openingBalance = balance;
                    continue;
                }
                daily[date] = balance;
                if (reader.GetBoolean(5) || reader.GetString(6) == "Initial Deposit") continue;
                var isCredit = reader.GetString(7) == "credit";
                if (amount > 0 && !isCredit) { income += amount; incomeDaily[date] = incomeDaily.GetValueOrDefault(date) + amount; }
                else if (amount < 0)
                {
                    var spent = Math.Abs(amount);
                    spending += spent;
                    spendingDaily[date] = spendingDaily.GetValueOrDefault(date) + spent;
                    int? categoryId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    var key = (categoryId, reader.GetString(3), reader.GetString(4));
                    category[key] = category.GetValueOrDefault(key) + spent;
                    if (reader.GetString(3) == "Uncategorised") uncategorised += spent;
                }
            }
        }
        var goalProgress = (await GetGoalsAsync()).ProgressPercent;
        var categoryRows = category.OrderByDescending(x => x.Value).Select(x => new CategorySpendDto(
            x.Key.Item1, x.Key.Item2, x.Key.Item3, x.Value, spending == 0 ? 0 : Math.Round(x.Value / spending * 100, 1))).ToList();
        return new(start, end, balance, income, spending, income - spending, income == 0 ? 0 : Math.Round((income - spending) / income * 100, 1),
            FillBalanceTrend(start, end, daily, openingBalance), categoryRows,
            incomeDaily.Select(x => new TrendPointDto(x.Key, x.Value)).ToList(),
            spendingDaily.Select(x => new TrendPointDto(x.Key, x.Value)).ToList(), goalProgress, uncategorised);
    }

    public static async Task<IReadOnlyList<SearchResultDto>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        if (query.Length > 200) throw new ArgumentException("Search text must be no longer than 200 characters.");
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            (SELECT 'transaction' AS type, t.id, coalesce(t.payee, t.memo, 'Transaction') AS title,
                a.name || ' · ' || to_char(t.amount, 'FM999,999,990.00') || ' ' ||
                    (SELECT currency_code FROM household_settings WHERE id=1) AS subtitle, '/transactions' AS route
             FROM transactions t JOIN accounts a ON a.id=t.account_id
             WHERE t.payee ILIKE @query OR t.memo ILIKE @query ORDER BY t.transaction_date DESC LIMIT 6)
            UNION ALL
            (SELECT 'account', a.id, a.name, coalesce(a.institution, 'Account'), '/settings'
             FROM accounts a WHERE NOT a.is_archived AND (a.name ILIKE @query OR a.institution ILIKE @query) LIMIT 4)
            UNION ALL
            (SELECT 'goal', g.id, g.name, coalesce(g.description, 'Savings goal'), '/goals'
             FROM savings_goals g WHERE g.status <> 'archived' AND (g.name ILIKE @query OR g.description ILIKE @query) LIMIT 4)
            UNION ALL
            (SELECT 'plan', r.id, r.name, initcap(r.kind) || ' · ' || initcap(r.frequency), '/plan'
             FROM recurring_items r WHERE r.is_active AND r.name ILIKE @query LIMIT 4)
            LIMIT 16
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("query", $"%{query.Trim()}%");
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<SearchResultDto>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static AccountDto ReadAccount(NpgsqlDataReader reader)
    {
        var primaryHolder = reader.GetString(2);
        var secondaryHolder = reader.IsDBNull(4) ? null : reader.GetString(4);
        var accountType = reader.GetString(5);
        var balance = reader.GetDecimal(8);
        decimal? creditLimit = reader.IsDBNull(12) ? null : reader.GetDecimal(12);
        var credit = accountType == "credit"
            ? FinanceMath.CalculateCreditPosition(balance, creditLimit)
            : new CreditPositionResult(0, 0, null, null);
        return new(reader.GetInt32(0), reader.GetString(1), primaryHolder, reader.GetBoolean(3), primaryHolder,
            secondaryHolder, accountType, reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), creditLimit, balance, credit.DebtBalance, credit.CreditBalance,
            credit.AvailableCredit, credit.UtilizationPercent, reader.GetDecimal(9), reader.GetBoolean(10), reader.GetBoolean(11));
    }

    private static TransactionDtoV2 ReadTransaction(NpgsqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)),
        reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.GetString(11), reader.GetString(12), reader.GetBoolean(13),
        reader.IsDBNull(14) ? null : reader.GetString(14), reader.GetDecimal(15),
        reader.IsDBNull(16) ? null : reader.GetInt32(16));

    private static RecurringItemDto ReadRecurring(NpgsqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDecimal(7),
        reader.GetString(8), DateOnly.FromDateTime(reader.GetDateTime(9)), reader.GetString(10), reader.GetBoolean(11),
        reader.IsDBNull(12) ? null : reader.GetString(12), reader.GetDecimal(13), reader.GetInt16(14), reader.GetString(15),
        reader.IsDBNull(16) ? null : DateOnly.FromDateTime(reader.GetDateTime(16)));

    private static void AddTransactionFilters(NpgsqlCommand command, int? accountId, int? categoryId, string? search, DateOnly? start, DateOnly? end)
    {
        if (accountId.HasValue) command.Parameters.AddWithValue("account_id", accountId.Value);
        if (categoryId.HasValue) command.Parameters.AddWithValue("category_id", categoryId.Value);
        if (!string.IsNullOrWhiteSpace(search)) command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
        if (start.HasValue) command.Parameters.AddWithValue("start_date", start.Value.ToDateTime(TimeOnly.MinValue));
        if (end.HasValue) command.Parameters.AddWithValue("end_date", end.Value.ToDateTime(TimeOnly.MinValue));
    }

    private static IReadOnlyList<TrendPointDto> FillBalanceTrend(DateOnly start, DateOnly end, SortedDictionary<DateOnly, decimal> changes, decimal openingBalance)
    {
        var points = new List<TrendPointDto>();
        var known = openingBalance;
        var dayCount = end.DayNumber - start.DayNumber + 1;
        var sampleInterval = Math.Max(1, (int)Math.Ceiling(dayCount / 365d));
        for (var offset = 0; offset < dayCount; offset++)
        {
            var date = start.AddDays(offset);
            if (changes.TryGetValue(date, out var value)) known = value;
            if (offset == 0 || offset == dayCount - 1 || offset % sampleInterval == 0) points.Add(new(date, known));
        }
        return points;
    }

    private static decimal SumOccurrences(decimal amount, DateOnly next, string frequency, DateOnly horizon)
    {
        var total = 0m;
        for (var occurrence = 0; ; occurrence++)
        {
            var date = OccurrenceDate(next, frequency, occurrence);
            if (date > horizon) break;
            total += amount;
        }
        return total;
    }

    private static DateOnly NormalizeNextDate(DateOnly date, string frequency, DateOnly today)
    {
        for (var occurrence = 0; ; occurrence++)
        {
            var candidate = OccurrenceDate(date, frequency, occurrence);
            if (candidate >= today) return candidate;
        }
    }

    private static DateOnly OccurrenceDate(DateOnly anchor, string frequency, int occurrence) => frequency switch
    {
        "weekly" => anchor.AddDays(7 * occurrence),
        "fortnightly" => anchor.AddDays(14 * occurrence),
        "quarterly" => anchor.AddMonths(3 * occurrence),
        "yearly" => anchor.AddYears(occurrence),
        _ => anchor.AddMonths(occurrence),
    };

    private static string? FrequencyFromGap(double days) => days switch
    {
        >= 5 and <= 9 => "weekly",
        >= 11 and <= 17 => "fortnightly",
        >= 24 and <= 36 => "monthly",
        >= 75 and <= 105 => "quarterly",
        >= 330 and <= 400 => "yearly",
        _ => null,
    };

    private static void ValidateAccount(string type, decimal safeZone, string name, bool isShared, string? primaryHolder, string? secondaryHolder, decimal? creditLimit)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160) throw new ArgumentException("Account name must be between 1 and 160 characters.");
        if (string.IsNullOrWhiteSpace(primaryHolder) || primaryHolder.Length > 160) throw new ArgumentException("An account holder name is required and must be no longer than 160 characters.");
        if (isShared && string.IsNullOrWhiteSpace(secondaryHolder)) throw new ArgumentException("Both account holder names are required for a joint account.");
        if (secondaryHolder?.Length > 160) throw new ArgumentException("Account holder names must be no longer than 160 characters.");
        if (!AccountTypes.Contains(type)) throw new ArgumentException("Unsupported account type.");
        if (safeZone < 0) throw new ArgumentException("Safe-zone amount cannot be negative.");
        if (creditLimit < 0) throw new ArgumentException("Credit limit cannot be negative.");
    }

    private static void ValidateRecurring(SaveRecurringItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Amount <= 0) throw new ArgumentException("Name and positive amount are required.");
        if (request.Kind is not ("bill" or "income")) throw new ArgumentException("Recurring kind must be bill or income.");
        if (!Frequencies.Contains(request.Frequency)) throw new ArgumentException("Unsupported frequency.");
        if (request.Source is not ("manual" or "suggestion" or "transaction")) throw new ArgumentException("Unsupported recurring source.");
        if (request.AmountTolerance < 0) throw new ArgumentException("Amount tolerance cannot be negative.");
        if (request.DateWindowDays is < 0 or > 31) throw new ArgumentException("Date window must be between 0 and 31 days.");
    }

    private static void ValidateLastFour(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is not null && (cleaned.Length != 4 || !cleaned.All(char.IsDigit)))
            throw new ArgumentException("Last four digits must contain exactly four numbers.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeText(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return "image/webp";
        return null;
    }

    private sealed record GoalRow(int Id, string Name, string? Description, decimal TargetAmount, DateOnly? TargetDate,
        int AccountId, string AccountName, int Priority, string IconKey, string ColorKey, int? ImageId, string Status);
    private sealed record PatternRow(int AccountId, string AccountName, string Name, DateOnly Date, decimal Amount);
    private sealed record BudgetDefinitionRow(int Id, int CategoryId, string Name, string Icon, string Color,
        decimal Amount, bool Rollover, decimal Scheduled, DateOnly EffectiveFrom);
}
