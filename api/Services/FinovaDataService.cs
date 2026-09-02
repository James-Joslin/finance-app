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

    public static async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(bool includeArchived = false)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = "SELECT id, name, kind, icon_key, color_key, is_system, is_archived FROM categories WHERE (@include_archived OR NOT is_archived) ORDER BY kind, name";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("include_archived", includeArchived);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<CategoryDto>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6)));
        return rows;
    }

    public static async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request)
    {
        ValidateCategory(request.Name, request.Kind, request.IconKey, request.ColorKey);
        var name = request.Name.Trim();
        var kind = request.Kind.Trim().ToLowerInvariant();
        var icon = request.IconKey.Trim();
        var color = request.ColorKey.Trim();
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using (var duplicate = new NpgsqlCommand("SELECT 1 FROM categories WHERE lower(name)=lower(@name)", connection))
        {
            duplicate.Parameters.AddWithValue("name", name);
            if (await duplicate.ExecuteScalarAsync() is not null)
                throw new ResourceConflictException("A category with that name already exists.");
        }
        const string sql = """
            INSERT INTO categories (name, kind, icon_key, color_key) VALUES (@name, @kind, @icon, @color)
            RETURNING id, name, kind, icon_key, color_key, is_system, is_archived
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
            return new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException("A category with that name already exists.");
        }
    }

    public static async Task<CategoryDto> UpdateCategoryAsync(int id, UpdateCategoryRequest request)
    {
        ValidateCategory(request.Name, request.Kind, request.IconKey, request.ColorKey);
        var name = request.Name.Trim();
        var kind = request.Kind.Trim().ToLowerInvariant();
        var icon = request.IconKey.Trim();
        var color = request.ColorKey.Trim();
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using (var existing = new NpgsqlCommand("SELECT is_system FROM categories WHERE id=@id FOR UPDATE", connection, transaction))
            {
                existing.Parameters.AddWithValue("id", id);
                var system = await existing.ExecuteScalarAsync();
                if (system is null) throw new ResourceNotFoundException("Category was not found.");
                if ((bool)system) throw new ArgumentException("System categories cannot be edited or archived.");
            }
            await using (var duplicate = new NpgsqlCommand("SELECT 1 FROM categories WHERE lower(name)=lower(@name) AND id<>@id", connection, transaction))
            {
                duplicate.Parameters.AddWithValue("id", id);
                duplicate.Parameters.AddWithValue("name", name);
                if (await duplicate.ExecuteScalarAsync() is not null)
                    throw new ResourceConflictException("A category with that name already exists.");
            }
            await using (var update = new NpgsqlCommand("""
                UPDATE categories SET name=@name, kind=@kind, icon_key=@icon, color_key=@color, is_archived=@archived
                WHERE id=@id
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("id", id);
                update.Parameters.AddWithValue("name", name);
                update.Parameters.AddWithValue("kind", kind);
                update.Parameters.AddWithValue("icon", icon);
                update.Parameters.AddWithValue("color", color);
                update.Parameters.AddWithValue("archived", request.IsArchived);
                await update.ExecuteNonQueryAsync();
            }
            await using (var sync = new NpgsqlCommand("""
                UPDATE transactions t SET is_transfer = EXISTS (
                    SELECT 1 FROM categories c WHERE c.id=t.category_id AND c.kind='transfer'
                ) OR EXISTS (
                    SELECT 1 FROM transaction_transfer_pairs p
                    WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id
                ) WHERE t.category_id=@id
                """, connection, transaction))
            {
                sync.Parameters.AddWithValue("id", id);
                await sync.ExecuteNonQueryAsync();
            }
            if (request.IsArchived)
            {
                await using var deactivate = new NpgsqlCommand(
                    "UPDATE transaction_rules SET is_active=false, updated_at=CURRENT_TIMESTAMP WHERE category_id=@id",
                    connection, transaction);
                deactivate.Parameters.AddWithValue("id", id);
                await deactivate.ExecuteNonQueryAsync();
            }
        });
        return (await GetCategoriesAsync(true)).Single(category => category.Id == id);
    }

    public static async Task<bool> DeleteCategoryAsync(int id)
    {
        var deleted = false;
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using (var existing = new NpgsqlCommand("SELECT is_system FROM categories WHERE id=@id FOR UPDATE", connection, transaction))
            {
                existing.Parameters.AddWithValue("id", id);
                var system = await existing.ExecuteScalarAsync();
                if (system is null) return;
                if ((bool)system) throw new ArgumentException("System categories cannot be deleted.");
            }
            const string referencesSql = """
                SELECT (SELECT count(*) FROM transactions WHERE category_id=@id)
                     + (SELECT count(*) FROM transaction_rules WHERE category_id=@id)
                     + (SELECT count(*) FROM transaction_splits WHERE category_id=@id)
                     + (SELECT count(*) FROM budget_definitions WHERE category_id=@id)
                     + (SELECT count(*) FROM recurring_items WHERE category_id=@id)
                """;
            await using (var references = new NpgsqlCommand(referencesSql, connection, transaction))
            {
                references.Parameters.AddWithValue("id", id);
                if (Convert.ToInt64(await references.ExecuteScalarAsync()) > 0)
                    throw new ResourceConflictException("This category is still used by transactions or planning rules. Archive it instead.");
            }
            await using var delete = new NpgsqlCommand("DELETE FROM categories WHERE id=@id", connection, transaction);
            delete.Parameters.AddWithValue("id", id);
            deleted = await delete.ExecuteNonQueryAsync() > 0;
        });
        return deleted;
    }

    public static async Task<IReadOnlyList<TransactionRuleDto>> GetTransactionRulesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT r.id, r.match_text, r.direction, r.category_id, c.name, r.priority, r.is_active
            FROM transaction_rules r JOIN categories c ON c.id = r.category_id
            ORDER BY r.priority, r.match_text, r.direction
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TransactionRuleDto>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5), reader.GetBoolean(6)));
        return rows;
    }

    public static async Task<TransactionRuleDto> SaveTransactionRuleAsync(int? id, SaveTransactionRuleRequest request)
    {
        var matchText = request.MatchText?.Trim() ?? string.Empty;
        var direction = request.Direction?.Trim().ToLowerInvariant() ?? string.Empty;
        if (matchText.Length is < 1 or > 200) throw new ArgumentException("Rule reference must be between 1 and 200 characters.");
        if (direction is not ("in" or "out" or "any")) throw new ArgumentException("Rule direction must be in, out, or any.");
        if (request.Priority < 1 || request.Priority > 100000) throw new ArgumentException("Rule priority must be between 1 and 100000.");
        int savedId = 0;
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using (var category = new NpgsqlCommand("SELECT 1 FROM categories WHERE id=@id AND NOT is_archived", connection, transaction))
            {
                category.Parameters.AddWithValue("id", request.CategoryId);
                if (await category.ExecuteScalarAsync() is null) throw new ArgumentException("An active category is required for a rule.");
            }
            if (id.HasValue)
            {
                await using var existing = new NpgsqlCommand("SELECT 1 FROM transaction_rules WHERE id=@id", connection, transaction);
                existing.Parameters.AddWithValue("id", id.Value);
                if (await existing.ExecuteScalarAsync() is null) throw new ResourceNotFoundException("Rule was not found.");
                await using var duplicate = new NpgsqlCommand("SELECT id FROM transaction_rules WHERE lower(trim(match_text))=lower(trim(@match)) AND direction=@direction AND id<>@id", connection, transaction);
                duplicate.Parameters.AddWithValue("id", id.Value);
                duplicate.Parameters.AddWithValue("match", matchText);
                duplicate.Parameters.AddWithValue("direction", direction);
                if (await duplicate.ExecuteScalarAsync() is not null) throw new ResourceConflictException("A rule with that reference and direction already exists.");
                savedId = id.Value;
                await using var update = new NpgsqlCommand("""
                    UPDATE transaction_rules SET match_text=@match, direction=@direction, category_id=@category,
                        priority=@priority, is_active=@active, updated_at=CURRENT_TIMESTAMP WHERE id=@id
                    """, connection, transaction);
                update.Parameters.AddWithValue("id", savedId);
                update.Parameters.AddWithValue("match", matchText);
                update.Parameters.AddWithValue("direction", direction);
                update.Parameters.AddWithValue("category", request.CategoryId);
                update.Parameters.AddWithValue("priority", request.Priority);
                update.Parameters.AddWithValue("active", request.IsActive);
                await update.ExecuteNonQueryAsync();
            }
            else
            {
                await using var duplicate = new NpgsqlCommand("SELECT id FROM transaction_rules WHERE lower(trim(match_text))=lower(trim(@match)) AND direction=@direction", connection, transaction);
                duplicate.Parameters.AddWithValue("match", matchText);
                duplicate.Parameters.AddWithValue("direction", direction);
                var existingId = await duplicate.ExecuteScalarAsync();
                if (existingId is not null) savedId = Convert.ToInt32(existingId);
                if (existingId is not null)
                {
                    await using var update = new NpgsqlCommand("""
                        UPDATE transaction_rules SET category_id=@category, priority=@priority, is_active=@active,
                            updated_at=CURRENT_TIMESTAMP WHERE id=@id
                        """, connection, transaction);
                    update.Parameters.AddWithValue("id", savedId);
                    update.Parameters.AddWithValue("category", request.CategoryId);
                    update.Parameters.AddWithValue("priority", request.Priority);
                    update.Parameters.AddWithValue("active", request.IsActive);
                    await update.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var insert = new NpgsqlCommand("""
                        INSERT INTO transaction_rules (match_text, direction, category_id, priority, is_active)
                        VALUES (@match, @direction, @category, @priority, @active) RETURNING id
                        """, connection, transaction);
                    insert.Parameters.AddWithValue("match", matchText);
                    insert.Parameters.AddWithValue("direction", direction);
                    insert.Parameters.AddWithValue("category", request.CategoryId);
                    insert.Parameters.AddWithValue("priority", request.Priority);
                    insert.Parameters.AddWithValue("active", request.IsActive);
                    savedId = Convert.ToInt32(await insert.ExecuteScalarAsync());
                }
            }
        });
        return (await GetTransactionRulesAsync()).Single(rule => rule.Id == savedId);
    }

    public static async Task<bool> DeleteTransactionRuleAsync(int id) =>
        await PostgreSqlQuerier.ExecuteNonQueryAsync("DELETE FROM transaction_rules WHERE id = @id", new() { ["id"] = id }) > 0;

    public static async Task<IReadOnlyList<TransferCandidateDto>> GetTransferCandidatesAsync(int transactionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sourceSql = "SELECT account_id, transaction_date, amount FROM transactions WHERE id=@id";
        int accountId;
        DateOnly date;
        decimal amount;
        await using (var source = new NpgsqlCommand(sourceSql, connection))
        {
            source.Parameters.AddWithValue("id", transactionId);
            await using var reader = await source.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction was not found.");
            accountId = reader.GetInt32(0);
            date = DateOnly.FromDateTime(reader.GetDateTime(1));
            amount = reader.GetDecimal(2);
        }
        const string sql = """
            SELECT t.id, t.account_id, a.name, t.transaction_date, t.amount, t.payee, t.memo
            FROM transactions t JOIN accounts a ON a.id=t.account_id
            WHERE t.account_id<>@account AND NOT a.is_archived
              AND t.amount=-@amount
              AND NOT EXISTS (SELECT 1 FROM transaction_transfer_pairs p WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id)
            ORDER BY abs(t.transaction_date::date-@date::date), t.id
            LIMIT 50
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
        await using var result = await command.ExecuteReaderAsync();
        var rows = new List<TransferCandidateDto>();
        while (await result.ReadAsync())
            rows.Add(new(result.GetInt32(0), result.GetInt32(1), result.GetString(2),
                DateOnly.FromDateTime(result.GetDateTime(3)), result.GetDecimal(4),
                result.IsDBNull(5) ? null : result.GetString(5), result.IsDBNull(6) ? null : result.GetString(6)));
        return rows;
    }

    public static async Task<TransferPairDto> PairTransferAsync(int transactionId, int pairedTransactionId)
    {
        if (transactionId == pairedTransactionId) throw new ArgumentException("A transaction cannot be paired with itself.");
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            const string sql = "SELECT id, account_id, transaction_date, amount FROM transactions WHERE id = ANY(@ids) FOR UPDATE";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("ids", new[] { transactionId, pairedTransactionId });
            var rows = new List<TransferTransactionRow>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetDecimal(3)));
            }
            if (rows.Count != 2) throw new ResourceNotFoundException("One or both transactions were not found.");
            var first = rows.Single(row => row.Id == transactionId);
            var second = rows.Single(row => row.Id == pairedTransactionId);
            ValidateTransferPair(first, second);
            await EnsureUnpairedAsync(connection, transaction, transactionId);
            await EnsureUnpairedAsync(connection, transaction, pairedTransactionId);
            await InsertTransferPairAsync(connection, transaction, transactionId, pairedTransactionId);
            await SetTransferFlagAsync(connection, transaction, transactionId, true);
            await SetTransferFlagAsync(connection, transaction, pairedTransactionId, true);
        });
        return await GetTransferPairAsync(transactionId);
    }

    public static async Task UnpairTransferAsync(int transactionId)
    {
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            int pairId;
            int firstId;
            int secondId;
            await using (var find = new NpgsqlCommand("SELECT id, transaction_id_a, transaction_id_b FROM transaction_transfer_pairs WHERE transaction_id_a=@id OR transaction_id_b=@id FOR UPDATE", connection, transaction))
            {
                find.Parameters.AddWithValue("id", transactionId);
                await using var reader = await find.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction is not paired.");
                pairId = reader.GetInt32(0);
                firstId = reader.GetInt32(1);
                secondId = reader.GetInt32(2);
            }
            await using (var delete = new NpgsqlCommand("DELETE FROM transaction_transfer_pairs WHERE id=@pair", connection, transaction))
            {
                delete.Parameters.AddWithValue("pair", pairId);
                await delete.ExecuteNonQueryAsync();
            }
            await SetTransferFlagAsync(connection, transaction, firstId, false);
            await SetTransferFlagAsync(connection, transaction, secondId, false);
        });
    }

    public static async Task<TransferPairDto> GetTransferPairAsync(int transactionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT p.id, @id,
                CASE WHEN p.transaction_id_a=@id THEN p.transaction_id_b ELSE p.transaction_id_a END,
                t.account_id, a.name, t.transaction_date, t.amount, t.payee, t.memo
            FROM transaction_transfer_pairs p
            JOIN transactions t ON t.id=CASE WHEN p.transaction_id_a=@id THEN p.transaction_id_b ELSE p.transaction_id_a END
            JOIN accounts a ON a.id=t.account_id
            WHERE p.transaction_id_a=@id OR p.transaction_id_b=@id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", transactionId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction is not paired.");
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4),
            DateOnly.FromDateTime(reader.GetDateTime(5)), reader.GetDecimal(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    public static async Task AutoPairImportedTransfersAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<int> importedIds)
    {
        foreach (var importedId in importedIds)
        {
            const string sourceSql = """
                SELECT account_id, transaction_date::date, amount,
                    regexp_replace(lower(coalesce(nullif(trim(payee), ''), memo)), '[^a-z0-9]+', '', 'g')
                FROM transactions WHERE id=@id
                """;
            int accountId;
            DateOnly date;
            decimal amount;
            string sourceReference;
            await using (var source = new NpgsqlCommand(sourceSql, connection, transaction))
            {
                source.Parameters.AddWithValue("id", importedId);
                await using var reader = await source.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) continue;
                accountId = reader.GetInt32(0);
                date = DateOnly.FromDateTime(reader.GetDateTime(1));
                amount = reader.GetDecimal(2);
                sourceReference = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            }
            if (amount == 0 || sourceReference.Length == 0) continue;
            const string candidateSql = """
                SELECT t.id
                FROM transactions t
                WHERE t.account_id<>@account AND t.transaction_date::date=@date::date AND t.amount=-@amount
                  AND NOT EXISTS (SELECT 1 FROM transaction_transfer_pairs p WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id)
                  AND regexp_replace(lower(coalesce(nullif(trim(t.payee), ''), t.memo)), '[^a-z0-9]+', '', 'g') <> ''
                  AND (
                    regexp_replace(lower(coalesce(nullif(trim(t.payee), ''), t.memo)), '[^a-z0-9]+', '', 'g') = @reference
                    OR regexp_replace(lower(coalesce(nullif(trim(t.payee), ''), t.memo)), '[^a-z0-9]+', '', 'g') LIKE '%' || @reference || '%'
                    OR @reference LIKE '%' || regexp_replace(lower(coalesce(nullif(trim(t.payee), ''), t.memo)), '[^a-z0-9]+', '', 'g') || '%'
                  )
                ORDER BY t.id
                LIMIT 1
                """;
            int? candidateId = null;
            await using (var candidate = new NpgsqlCommand(candidateSql, connection, transaction))
            {
                candidate.Parameters.AddWithValue("account", accountId);
                candidate.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
                candidate.Parameters.AddWithValue("amount", amount);
                candidate.Parameters.AddWithValue("reference", sourceReference);
                candidateId = (int?)await candidate.ExecuteScalarAsync();
            }
            if (!candidateId.HasValue) continue;
            await InsertTransferPairAsync(connection, transaction, importedId, candidateId.Value);
            await SetTransferFlagAsync(connection, transaction, importedId, true);
            await SetTransferFlagAsync(connection, transaction, candidateId.Value, true);
        }
    }

    private static async Task EnsureUnpairedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int transactionId)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM transaction_transfer_pairs WHERE transaction_id_a=@id OR transaction_id_b=@id", connection, transaction);
        command.Parameters.AddWithValue("id", transactionId);
        if (await command.ExecuteScalarAsync() is not null) throw new ResourceConflictException("One of these transactions is already paired.");
    }

    private static async Task InsertTransferPairAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int firstId, int secondId)
    {
        await using var command = new NpgsqlCommand("INSERT INTO transaction_transfer_pairs (transaction_id_a, transaction_id_b) VALUES (@a,@b)", connection, transaction);
        command.Parameters.AddWithValue("a", Math.Min(firstId, secondId));
        command.Parameters.AddWithValue("b", Math.Max(firstId, secondId));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetTransferFlagAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int transactionId, bool paired)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE transactions t SET is_transfer = @paired OR EXISTS (
                SELECT 1 FROM categories c WHERE c.id=t.category_id AND c.kind='transfer'
            ) OR EXISTS (
                SELECT 1 FROM transaction_transfer_pairs p WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id
            ) WHERE t.id=@id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", transactionId);
        command.Parameters.AddWithValue("paired", paired);
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateTransferPair(TransferTransactionRow first, TransferTransactionRow second)
    {
        if (first.AccountId == second.AccountId) throw new ArgumentException("Transfers must belong to different accounts.");
        if (first.Amount == 0 || first.Amount != -second.Amount) throw new ArgumentException("Transfers must have equal and opposite amounts.");
    }

    public static async Task<TransactionDetailDto> CreateManualTransactionAsync(SaveManualTransactionRequest request)
    {
        var signedAmount = ValidateManualTransactionRequest(request);
        var splits = request.Splits ?? Array.Empty<SaveTransactionSplitRequest>();
        var transactionId = 0;
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await EnsureManualAccountAsync(connection, transaction, request.AccountId);
            await ValidateManualCategoriesAsync(connection, transaction, request.Direction, request.CategoryId, splits);

            const string insertSql = """
                INSERT INTO transactions (
                    account_id, transaction_date, amount, payee, memo, transaction_type,
                    source_file_type, category_id, status, is_transfer
                )
                VALUES (@account, @date, @amount, @payee, @memo, 'Manual', 'MANUAL', @category, 'completed', false)
                RETURNING id
                """;
            await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("account", request.AccountId);
            insert.Parameters.AddWithValue("date", request.Date.ToDateTime(TimeOnly.MinValue));
            insert.Parameters.AddWithValue("amount", signedAmount);
            AddNullable(insert, "payee", CleanManualText(request.Payee, 200));
            AddNullable(insert, "memo", CleanManualText(request.Memo, 500));
            AddNullable(insert, "category", splits.Count == 0 ? request.CategoryId : null);
            transactionId = Convert.ToInt32(await insert.ExecuteScalarAsync());
            await InsertTransactionSplitsAsync(connection, transaction, transactionId, splits);
        });
        return await GetTransactionDetailsAsync(transactionId);
    }

    public static async Task<TransactionDetailDto> UpdateManualTransactionAsync(int id, SaveManualTransactionRequest request)
    {
        var signedAmount = ValidateManualTransactionRequest(request);
        var splits = request.Splits ?? Array.Empty<SaveTransactionSplitRequest>();
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            int existingAccountId;
            decimal existingAmount;
            string? sourceFileType;
            string? fitId;
            long? importBatchId;
            bool isPaired;
            bool hasRecurringOccurrence;
            await using (var existing = new NpgsqlCommand("""
                SELECT account_id, amount, source_file_type, fitid, import_batch_id,
                    EXISTS (SELECT 1 FROM transaction_transfer_pairs p
                        WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id),
                    EXISTS (SELECT 1 FROM recurring_occurrences ro WHERE ro.transaction_id=t.id)
                FROM transactions t WHERE t.id=@id FOR UPDATE
                """, connection, transaction))
            {
                existing.Parameters.AddWithValue("id", id);
                await using var reader = await existing.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction was not found.");
                existingAccountId = reader.GetInt32(0);
                existingAmount = reader.GetDecimal(1);
                sourceFileType = reader.IsDBNull(2) ? null : reader.GetString(2);
                fitId = reader.IsDBNull(3) ? null : reader.GetString(3);
                importBatchId = reader.IsDBNull(4) ? null : reader.GetInt64(4);
                isPaired = reader.GetBoolean(5);
                hasRecurringOccurrence = reader.GetBoolean(6);
            }
            EnsureEditableManualTransaction(sourceFileType, fitId, importBatchId);
            if (isPaired && (request.AccountId != existingAccountId || signedAmount != existingAmount))
                throw new ResourceConflictException("Unpair this transfer before changing its account or amount.");
            if (isPaired && splits.Count > 0)
                throw new ResourceConflictException("Paired transfers cannot be split.");
            if (hasRecurringOccurrence && request.AccountId != existingAccountId)
                throw new ResourceConflictException("A recurring-linked transaction cannot be moved to another account.");

            await EnsureManualAccountAsync(connection, transaction, request.AccountId);
            await ValidateManualCategoriesAsync(connection, transaction, request.Direction, request.CategoryId, splits);

            const string updateSql = """
                UPDATE transactions SET account_id=@account, transaction_date=@date, amount=@amount,
                    payee=@payee, memo=@memo, transaction_type='Manual', source_file_type='MANUAL',
                    category_id=@category, is_transfer=EXISTS (
                        SELECT 1 FROM transaction_transfer_pairs p
                        WHERE p.transaction_id_a=transactions.id OR p.transaction_id_b=transactions.id
                    )
                WHERE id=@id
                """;
            await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("id", id);
                update.Parameters.AddWithValue("account", request.AccountId);
                update.Parameters.AddWithValue("date", request.Date.ToDateTime(TimeOnly.MinValue));
                update.Parameters.AddWithValue("amount", signedAmount);
                AddNullable(update, "payee", CleanManualText(request.Payee, 200));
                AddNullable(update, "memo", CleanManualText(request.Memo, 500));
                AddNullable(update, "category", splits.Count == 0 ? request.CategoryId : null);
                await update.ExecuteNonQueryAsync();
            }

            await using (var deleteSplits = new NpgsqlCommand(
                "DELETE FROM transaction_splits WHERE transaction_id=@transaction", connection, transaction))
            {
                deleteSplits.Parameters.AddWithValue("transaction", id);
                await deleteSplits.ExecuteNonQueryAsync();
            }
            await InsertTransactionSplitsAsync(connection, transaction, id, splits);

            if (hasRecurringOccurrence)
            {
                await using var occurrence = new NpgsqlCommand("""
                    UPDATE recurring_occurrences SET actual_amount=abs(@amount), updated_at=CURRENT_TIMESTAMP
                    WHERE transaction_id=@transaction AND status IN ('matched', 'paid')
                    """, connection, transaction);
                occurrence.Parameters.AddWithValue("amount", signedAmount);
                occurrence.Parameters.AddWithValue("transaction", id);
                await occurrence.ExecuteNonQueryAsync();
            }
        });
        return await GetTransactionDetailsAsync(id);
    }

    public static async Task DeleteManualTransactionAsync(int id)
    {
        await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            string? sourceFileType;
            string? fitId;
            long? importBatchId;
            int? pairedId = null;
            await using (var existing = new NpgsqlCommand("""
                SELECT t.source_file_type, t.fitid, t.import_batch_id
                FROM transactions t WHERE t.id=@id FOR UPDATE
                """, connection, transaction))
            {
                existing.Parameters.AddWithValue("id", id);
                await using var reader = await existing.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new ResourceNotFoundException("Transaction was not found.");
                sourceFileType = reader.IsDBNull(0) ? null : reader.GetString(0);
                fitId = reader.IsDBNull(1) ? null : reader.GetString(1);
                importBatchId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            }
            await using var paired = new NpgsqlCommand("""
                SELECT CASE WHEN transaction_id_a=@id THEN transaction_id_b ELSE transaction_id_a END
                FROM transaction_transfer_pairs
                WHERE transaction_id_a=@id OR transaction_id_b=@id LIMIT 1
                """, connection, transaction);
            paired.Parameters.AddWithValue("id", id);
            var pairValue = await paired.ExecuteScalarAsync();
            pairedId = pairValue is null or DBNull ? null : Convert.ToInt32(pairValue);
            EnsureEditableManualTransaction(sourceFileType, fitId, importBatchId);

            await using (var resetOccurrence = new NpgsqlCommand("""
                UPDATE recurring_occurrences SET status='expected', transaction_id=NULL,
                    actual_amount=NULL, matched_at=NULL, updated_at=CURRENT_TIMESTAMP
                WHERE transaction_id=@transaction
                """, connection, transaction))
            {
                resetOccurrence.Parameters.AddWithValue("transaction", id);
                await resetOccurrence.ExecuteNonQueryAsync();
            }

            await using (var delete = new NpgsqlCommand("DELETE FROM transactions WHERE id=@id", connection, transaction))
            {
                delete.Parameters.AddWithValue("id", id);
                await delete.ExecuteNonQueryAsync();
            }

            if (pairedId.HasValue)
            {
                await using var reconcile = new NpgsqlCommand("""
                    UPDATE transactions t SET is_transfer =
                        EXISTS (SELECT 1 FROM categories c WHERE c.id=t.category_id AND c.kind='transfer')
                        OR EXISTS (SELECT 1 FROM transaction_transfer_pairs p
                            WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id)
                    WHERE t.id=@id
                    """, connection, transaction);
                reconcile.Parameters.AddWithValue("id", pairedId.Value);
                await reconcile.ExecuteNonQueryAsync();
            }
        });
    }

    public static async Task<TransactionDetailDto> GetTransactionDetailsAsync(int id)
    {
        int accountId;
        await using (var connection = PostgreSqlQuerier.BuildConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT account_id FROM transactions WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", id);
            var value = await command.ExecuteScalarAsync();
            if (value is null) throw new ResourceNotFoundException("Transaction was not found.");
            accountId = Convert.ToInt32(value);
        }

        var page = await GetTransactionsAsync(accountId, null, null, "all", null, null, 1, 10000);
        var item = page.Items.SingleOrDefault(transaction => transaction.Id == id);
        if (item is null) throw new ResourceNotFoundException("Transaction was not found.");
        var splits = await GetTransactionSplitsAsync(id);
        return new(item, splits);
    }

    private static async Task<IReadOnlyList<TransactionSplitDto>> GetTransactionSplitsAsync(int transactionId)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT s.id, s.category_id, c.name, s.amount, s.memo, s.line_order
            FROM transaction_splits s JOIN categories c ON c.id=s.category_id
            WHERE s.transaction_id=@transaction ORDER BY s.line_order, s.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("transaction", transactionId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TransactionSplitDto>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt16(5)));
        return rows;
    }

    private static async Task InsertTransactionSplitsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int transactionId,
        IReadOnlyList<SaveTransactionSplitRequest> splits)
    {
        for (var index = 0; index < splits.Count; index++)
        {
            var split = splits[index];
            await using var command = new NpgsqlCommand("""
                INSERT INTO transaction_splits (transaction_id, category_id, amount, memo, line_order)
                VALUES (@transaction, @category, @amount, @memo, @line_order)
                """, connection, transaction);
            command.Parameters.AddWithValue("transaction", transactionId);
            command.Parameters.AddWithValue("category", split.CategoryId);
            command.Parameters.AddWithValue("amount", decimal.Round(split.Amount, 2));
            AddNullable(command, "memo", CleanManualText(split.Memo, 500));
            command.Parameters.AddWithValue("line_order", index);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureManualAccountAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM accounts WHERE id=@id AND NOT is_archived", connection, transaction);
        command.Parameters.AddWithValue("id", accountId);
        if (await command.ExecuteScalarAsync() is null)
            throw new ArgumentException("The selected account was not found or is archived.");
    }

    private static async Task ValidateManualCategoriesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string direction,
        int? categoryId, IReadOnlyList<SaveTransactionSplitRequest> splits)
    {
        var kind = direction.Trim().ToLowerInvariant() == "income" ? "income" : "expense";
        var ids = splits.Count == 0
            ? categoryId.HasValue ? new[] { categoryId.Value } : Array.Empty<int>()
            : splits.Select(split => split.CategoryId).Distinct().ToArray();
        if (ids.Length == 0) throw new ArgumentException("Choose a category or add split lines.");
        await using var command = new NpgsqlCommand(
            "SELECT id, kind FROM categories WHERE id = ANY(@ids) AND NOT is_archived", connection, transaction);
        command.Parameters.AddWithValue("ids", ids);
        var found = new Dictionary<int, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) found[reader.GetInt32(0)] = reader.GetString(1);
        }
        if (found.Count != ids.Length || found.Values.Any(value => value == "transfer") ||
            found.Values.Any(value => value != kind))
            throw new ArgumentException($"Choose active {kind} categories; transfer categories are not valid for manual transactions.");
        if (splits.Count > 0 && categoryId.HasValue)
            throw new ArgumentException("A split transaction cannot also have a parent category.");
    }

    private static decimal ValidateManualTransactionRequest(SaveManualTransactionRequest request)
    {
        var direction = request.Direction?.Trim().ToLowerInvariant();
        if (direction is not ("income" or "expense"))
            throw new ArgumentException("Transaction direction must be income or expense.");
        if (request.Amount <= 0 || request.Amount != decimal.Round(request.Amount, 2))
            throw new ArgumentException("Transaction amount must be positive and have no more than two decimal places.");
        if (request.Amount > 9999999999.99m)
            throw new ArgumentException("Transaction amount is too large.");
        var splits = request.Splits ?? Array.Empty<SaveTransactionSplitRequest>();
        if (splits.Count == 1 || splits.Count > 50)
            throw new ArgumentException("A split transaction must contain between 2 and 50 lines.");
        if (splits.Any(split => split.Amount <= 0 || split.Amount != decimal.Round(split.Amount, 2)))
            throw new ArgumentException("Split amounts must be positive and have no more than two decimal places.");
        if (splits.Count > 0 && decimal.Round(splits.Sum(split => split.Amount), 2) != request.Amount)
            throw new ArgumentException("Split amounts must add up exactly to the transaction amount.");
        return direction == "income" ? request.Amount : -request.Amount;
    }

    private static void EnsureEditableManualTransaction(string? sourceFileType, string? fitId, long? importBatchId)
    {
        if (!string.Equals(sourceFileType, "MANUAL", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(fitId) || importBatchId.HasValue)
            throw new ResourceConflictException("Only user-created manual transactions can be edited or deleted.");
    }

    private static string? CleanManualText(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length > maxLength
            ? throw new ArgumentException($"Transaction text must be no longer than {maxLength} characters.")
            : value.Trim();

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

    private static void AddNullable(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

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
        if (categoryId.HasValue) filters.Add("(tx.category_id = @category_id OR EXISTS (SELECT 1 FROM transaction_splits s WHERE s.transaction_id=tx.id AND s.category_id=@category_id))");
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
                tx.transaction_type, tc.meaning, tx.category_id, CASE WHEN EXISTS (SELECT 1 FROM transaction_splits s WHERE s.transaction_id=tx.id) THEN 'Split transaction' ELSE coalesce(c.name, 'Uncategorised') END, tx.status, tx.is_transfer,
                tx.source_file_type, tx.running_balance,
                (SELECT ro.recurring_item_id FROM recurring_occurrences ro WHERE ro.transaction_id = tx.id LIMIT 1),
                (SELECT p.id FROM transaction_transfer_pairs p WHERE p.transaction_id_a=tx.id OR p.transaction_id_b=tx.id LIMIT 1),
                (SELECT CASE WHEN p.transaction_id_a=tx.id THEN p.transaction_id_b ELSE p.transaction_id_a END
                    FROM transaction_transfer_pairs p WHERE p.transaction_id_a=tx.id OR p.transaction_id_b=tx.id LIMIT 1),
                (SELECT a2.name FROM transaction_transfer_pairs p
                    JOIN transactions t2 ON t2.id=CASE WHEN p.transaction_id_a=tx.id THEN p.transaction_id_b ELSE p.transaction_id_a END
                    JOIN accounts a2 ON a2.id=t2.account_id
                    WHERE p.transaction_id_a=tx.id OR p.transaction_id_b=tx.id LIMIT 1),
                coalesce(tx.source_file_type = 'MANUAL', false) AS is_manual,
                coalesce(tx.source_file_type = 'MANUAL' AND tx.fitid IS NULL AND tx.import_batch_id IS NULL, false) AS is_editable,
                (SELECT count(*) FROM transaction_splits s WHERE s.transaction_id=tx.id)::int AS split_count,
                EXISTS (SELECT 1 FROM transaction_splits s WHERE s.transaction_id=tx.id) AS is_split
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
            await using (var split = new NpgsqlCommand(
                "SELECT 1 FROM transaction_splits WHERE transaction_id=@transaction LIMIT 1", connection, transaction))
            {
                split.Parameters.AddWithValue("transaction", id);
                if (await split.ExecuteScalarAsync() is not null)
                    throw new ResourceConflictException("Split transactions must be edited through the transaction editor.");
            }
            const string updateSql = """
                UPDATE transactions t SET category_id = @category,
                    is_transfer = EXISTS (SELECT 1 FROM categories WHERE id = @category AND kind = 'transfer')
                        OR EXISTS (SELECT 1 FROM transaction_transfer_pairs p WHERE p.transaction_id_a=t.id OR p.transaction_id_b=t.id)
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
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ReconcileRecurringTransactionsAsync(connection, transaction, accountId, start, end);
        await transaction.CommitAsync();
    }

    public static async Task ReconcileRecurringTransactionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int accountId, DateOnly start, DateOnly end)
    {
        var ids = new List<int>();
        await using (var recurring = new NpgsqlCommand(
            "SELECT id FROM recurring_items WHERE is_active", connection, transaction))
        await using (var reader = await recurring.ExecuteReaderAsync())
            while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        foreach (var id in ids) await PopulateRecurringOccurrencesAsync(connection, transaction, id, false);

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
        await using var command = new NpgsqlCommand(sql, connection, transaction);
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
                    coalesce(sum(CASE WHEN t.amount < 0 AND NOT t.is_transfer AND NOT a.is_archived
                        THEN coalesce(posting.amount, 0) ELSE 0 END), 0)
                FROM generate_series(@effective::date, @month::date, interval '1 month') AS months(month)
                LEFT JOIN budget_months bm ON bm.budget_id=@budget AND bm.month=months.month::date
                LEFT JOIN transactions t ON t.transaction_date >= months.month
                    AND t.transaction_date < months.month + interval '1 month'
                LEFT JOIN accounts a ON a.id=t.account_id
                LEFT JOIN LATERAL (
                    SELECT s.category_id, s.amount
                    FROM transaction_splits s WHERE s.transaction_id=t.id
                    UNION ALL
                    SELECT t.category_id, abs(t.amount)
                    WHERE NOT EXISTS (SELECT 1 FROM transaction_splits s WHERE s.transaction_id=t.id)
                ) posting ON posting.category_id=@category
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
        var today = await GetHouseholdTodayAsync();
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT t.account_id, a.name, coalesce(nullif(trim(t.payee), ''), nullif(trim(t.memo), ''), 'Unknown'),
                t.transaction_date, t.amount
            FROM transactions t JOIN accounts a ON a.id=t.account_id
            WHERE t.transaction_date >= @cutoff AND NOT t.is_transfer
                AND coalesce(t.transaction_type, '') <> 'Initial Deposit' AND NOT a.is_archived
                AND (a.account_type <> 'credit' OR t.amount < 0)
            ORDER BY t.account_id, lower(coalesce(t.payee, t.memo)), t.transaction_date
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cutoff", today.AddDays(-400).ToDateTime(TimeOnly.MinValue));
        await using var reader = await command.ExecuteReaderAsync();
        var patterns = new List<PatternRow>();
        while (await reader.ReadAsync()) patterns.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4)));
        var existing = (await GetRecurringItemsAsync()).Select(r => (r.AccountId, NormalizeText(r.Name))).ToHashSet();
        var suggestions = new List<RecurringSuggestionDto>();
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
                coalesce(c.color_key, 'slate'), t.is_transfer, coalesce(t.transaction_type, ''), a.account_type,
                EXISTS (SELECT 1 FROM transaction_splits s WHERE s.transaction_id=t.id)
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
                    if (reader.GetBoolean(8)) continue;
                    int? categoryId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    var key = (categoryId, reader.GetString(3), reader.GetString(4));
                    category[key] = category.GetValueOrDefault(key) + spent;
                    if (reader.GetString(3) == "Uncategorised") uncategorised += spent;
                }
            }
        }
        const string splitCategorySql = """
            SELECT t.transaction_date::date, s.amount, s.category_id, c.name, c.color_key
            FROM transactions t
            JOIN accounts a ON a.id=t.account_id
            JOIN transaction_splits s ON s.transaction_id=t.id
            JOIN categories c ON c.id=s.category_id
            WHERE NOT a.is_archived AND t.amount < 0 AND NOT t.is_transfer
                AND t.transaction_date >= @start AND t.transaction_date <= @end
            ORDER BY t.transaction_date, t.id, s.line_order
            """;
        await using (var splitCommand = new NpgsqlCommand(splitCategorySql, connection))
        {
            splitCommand.Parameters.AddWithValue("start", start.ToDateTime(TimeOnly.MinValue));
            splitCommand.Parameters.AddWithValue("end", end.ToDateTime(TimeOnly.MaxValue));
            await using var splitReader = await splitCommand.ExecuteReaderAsync();
            while (await splitReader.ReadAsync())
            {
                var key = (splitReader.GetInt32(2), splitReader.GetString(3), splitReader.GetString(4));
                var amount = splitReader.GetDecimal(1);
                category[key] = category.GetValueOrDefault(key) + amount;
                if (key.Item2 == "Uncategorised") uncategorised += amount;
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
        reader.IsDBNull(16) ? null : reader.GetInt32(16), reader.IsDBNull(17) ? null : reader.GetInt32(17),
        reader.IsDBNull(18) ? null : reader.GetInt32(18), reader.IsDBNull(19) ? null : reader.GetString(19),
        reader.GetBoolean(20), reader.GetBoolean(21), reader.GetBoolean(23), reader.GetInt32(22));

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

    private static void ValidateCategory(string name, string kind, string icon, string color)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Category name must be between 1 and 120 characters.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().ToLowerInvariant() is not ("income" or "expense" or "transfer"))
            throw new ArgumentException("Category kind must be income, expense, or transfer.");
        if (string.IsNullOrWhiteSpace(icon) || icon.Trim().Length > 40 || string.IsNullOrWhiteSpace(color) || color.Trim().Length > 24)
            throw new ArgumentException("Category icon or colour is invalid.");
    }

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

    private sealed record TransferTransactionRow(int Id, int AccountId, DateOnly Date, decimal Amount);
    private sealed record GoalRow(int Id, string Name, string? Description, decimal TargetAmount, DateOnly? TargetDate,
        int AccountId, string AccountName, int Priority, string IconKey, string ColorKey, int? ImageId, string Status);
    private sealed record PatternRow(int AccountId, string AccountName, string Name, DateOnly Date, decimal Amount);
    private sealed record BudgetDefinitionRow(int Id, int CategoryId, string Name, string Icon, string Color,
        decimal Amount, bool Rollover, decimal Scheduled, DateOnly EffectiveFrom);
}
