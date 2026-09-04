using System.Collections.Immutable;
using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using financesApi.models;
using financesApi.utilities;
using Npgsql;
using NpgsqlTypes;

namespace financesApi.services;

public static class PortabilityService
{
    private const string Format = "finova-portable";
    private const int Version = 1;
    private const string JsonLinesContentType = "application/x-ndjson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private sealed record Column(string Name, NpgsqlDbType Type, bool Nullable = false);
    private sealed record ForeignKey(string Column, string Table, string ReferencedColumn = "id");
    private sealed record TableSpec(
        string Name,
        string Entry,
        IReadOnlyList<Column> Columns,
        string? IdColumn = "id",
        IReadOnlyList<ForeignKey>? ForeignKeys = null,
        bool HasSequence = true,
        bool ExternalImageContent = false)
    {
        public IReadOnlyList<ForeignKey> References => ForeignKeys ?? [];
    }

    private sealed record ImageData(int Id, string ContentType, string FileName, byte[] Content, string Hash, DateTimeOffset CreatedAt);
    private sealed record ArchiveEntry(string Path, string Sha256, long Bytes, int Rows);
    private sealed record Manifest(string Format, int Version, DateTimeOffset ExportedAt, IReadOnlyList<ArchiveEntry> Entries);
    private sealed record ParsedArchive(Manifest Manifest, Dictionary<string, List<Dictionary<string, JsonElement>>> Tables, Dictionary<int, byte[]> Images);

    private static readonly IReadOnlyList<TableSpec> Tables =
    [
        new("people", "data/people.jsonl", [C("id", NpgsqlDbType.Integer), C("created_at", NpgsqlDbType.Timestamp, true), C("first_name", NpgsqlDbType.Text), C("last_name", NpgsqlDbType.Text)]),
        new("user_profiles", "data/user_profiles.jsonl", [C("id", NpgsqlDbType.Smallint), C("first_name", NpgsqlDbType.Varchar), C("last_name", NpgsqlDbType.Varchar), C("created_at", NpgsqlDbType.TimestampTz), C("updated_at", NpgsqlDbType.TimestampTz)]),
        new("household_settings", "data/household_settings.jsonl", [C("id", NpgsqlDbType.Smallint), C("household_name", NpgsqlDbType.Text), C("currency_code", NpgsqlDbType.Varchar), C("locale", NpgsqlDbType.Varchar), C("timezone", NpgsqlDbType.Varchar), C("updated_at", NpgsqlDbType.TimestampTz)]),
        new("transaction_type_codes", "data/transaction_type_codes.jsonl", [C("code", NpgsqlDbType.Varchar), C("meaning", NpgsqlDbType.Text), C("institution", NpgsqlDbType.Text), C("is_active", NpgsqlDbType.Boolean)], IdColumn: "code", HasSequence: false),
        new("categories", "data/categories.jsonl", [C("id", NpgsqlDbType.Integer), C("name", NpgsqlDbType.Text), C("kind", NpgsqlDbType.Varchar), C("icon_key", NpgsqlDbType.Varchar), C("color_key", NpgsqlDbType.Varchar), C("is_system", NpgsqlDbType.Boolean), C("is_archived", NpgsqlDbType.Boolean)]),
        new("accounts", "data/accounts.jsonl", [C("id", NpgsqlDbType.Integer), C("name", NpgsqlDbType.Text), C("owner_id", NpgsqlDbType.Integer, true), C("is_shared", NpgsqlDbType.Boolean, true), C("created_at", NpgsqlDbType.Timestamp, true), C("account_type", NpgsqlDbType.Varchar), C("institution", NpgsqlDbType.Text, true), C("last_four", NpgsqlDbType.Varchar, true), C("safe_zone_amount", NpgsqlDbType.Numeric), C("include_in_safe_to_spend", NpgsqlDbType.Boolean), C("is_archived", NpgsqlDbType.Boolean), C("primary_holder_name", NpgsqlDbType.Text, true), C("secondary_holder_name", NpgsqlDbType.Text, true), C("credit_limit", NpgsqlDbType.Numeric, true)], ForeignKeys: [new("owner_id", "people")]),
        new("transaction_import_batches", "data/transaction_import_batches.jsonl", [C("id", NpgsqlDbType.Bigint), C("account_id", NpgsqlDbType.Integer), C("file_name", NpgsqlDbType.Text), C("file_type", NpgsqlDbType.Varchar), C("file_size", NpgsqlDbType.Bigint), C("file_sha256", NpgsqlDbType.Varchar), C("status", NpgsqlDbType.Varchar), C("total_rows", NpgsqlDbType.Integer), C("importable_rows", NpgsqlDbType.Integer), C("imported_rows", NpgsqlDbType.Integer), C("skipped_rows", NpgsqlDbType.Integer), C("rejected_rows", NpgsqlDbType.Integer), C("created_at", NpgsqlDbType.TimestampTz), C("expires_at", NpgsqlDbType.TimestampTz), C("completed_at", NpgsqlDbType.TimestampTz, true), C("undone_at", NpgsqlDbType.TimestampTz, true), C("starting_balance", NpgsqlDbType.Numeric)], ForeignKeys: [new("account_id", "accounts")]),
        new("transactions", "data/transactions.jsonl", [C("id", NpgsqlDbType.Integer), C("account_id", NpgsqlDbType.Integer), C("transaction_date", NpgsqlDbType.Timestamp), C("amount", NpgsqlDbType.Numeric), C("payee", NpgsqlDbType.Text, true), C("memo", NpgsqlDbType.Text, true), C("category", NpgsqlDbType.Text, true), C("source_file", NpgsqlDbType.Text, true), C("created_at", NpgsqlDbType.Timestamp, true), C("fitid", NpgsqlDbType.Text, true), C("transaction_type", NpgsqlDbType.Text, true), C("check_number", NpgsqlDbType.Varchar, true), C("source_file_type", NpgsqlDbType.Varchar, true), C("category_id", NpgsqlDbType.Integer, true), C("status", NpgsqlDbType.Varchar), C("is_transfer", NpgsqlDbType.Boolean), C("import_fingerprint", NpgsqlDbType.Varchar, true), C("import_batch_id", NpgsqlDbType.Bigint, true), C("cleared", NpgsqlDbType.Boolean), C("is_reconciliation_adjustment", NpgsqlDbType.Boolean)], ForeignKeys: [new("account_id", "accounts"), new("category_id", "categories"), new("import_batch_id", "transaction_import_batches")]),
        new("transaction_import_rows", "data/transaction_import_rows.jsonl", [C("id", NpgsqlDbType.Bigint), C("batch_id", NpgsqlDbType.Bigint), C("ordinal", NpgsqlDbType.Integer), C("source_label", NpgsqlDbType.Text), C("transaction_date", NpgsqlDbType.Timestamp, true), C("display_date", NpgsqlDbType.Text, true), C("amount", NpgsqlDbType.Numeric, true), C("display_amount", NpgsqlDbType.Text, true), C("payee", NpgsqlDbType.Text, true), C("memo", NpgsqlDbType.Text, true), C("fitid", NpgsqlDbType.Text, true), C("transaction_type", NpgsqlDbType.Text, true), C("category", NpgsqlDbType.Text, true), C("check_number", NpgsqlDbType.Varchar, true), C("source_file_type", NpgsqlDbType.Varchar), C("statement_balance", NpgsqlDbType.Numeric, true), C("fingerprint", NpgsqlDbType.Varchar, true), C("outcome", NpgsqlDbType.Varchar), C("reason_code", NpgsqlDbType.Varchar, true), C("reason_message", NpgsqlDbType.Text, true), C("transaction_id", NpgsqlDbType.Integer, true), C("balance_after", NpgsqlDbType.Numeric, true)], ForeignKeys: [new("batch_id", "transaction_import_batches"), new("transaction_id", "transactions")]),
        new("transaction_rules", "data/transaction_rules.jsonl", [C("id", NpgsqlDbType.Integer), C("match_text", NpgsqlDbType.Text), C("category_id", NpgsqlDbType.Integer), C("priority", NpgsqlDbType.Integer), C("is_active", NpgsqlDbType.Boolean), C("direction", NpgsqlDbType.Varchar), C("created_at", NpgsqlDbType.TimestampTz), C("updated_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("category_id", "categories")]),
        new("goal_images", "data/goal_images.jsonl", [C("id", NpgsqlDbType.Integer), C("content_type", NpgsqlDbType.Varchar), C("file_name", NpgsqlDbType.Text), C("content_hash", NpgsqlDbType.Varchar), C("created_at", NpgsqlDbType.TimestampTz)], ExternalImageContent: true),
        new("savings_goals", "data/savings_goals.jsonl", [C("id", NpgsqlDbType.Integer), C("name", NpgsqlDbType.Text), C("description", NpgsqlDbType.Text, true), C("target_amount", NpgsqlDbType.Numeric), C("target_date", NpgsqlDbType.Date, true), C("account_id", NpgsqlDbType.Integer), C("priority_order", NpgsqlDbType.Integer), C("icon_key", NpgsqlDbType.Varchar), C("color_key", NpgsqlDbType.Varchar), C("image_id", NpgsqlDbType.Integer, true), C("status", NpgsqlDbType.Varchar), C("created_at", NpgsqlDbType.TimestampTz), C("updated_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("account_id", "accounts"), new("image_id", "goal_images")]),
        new("recurring_items", "data/recurring_items.jsonl", [C("id", NpgsqlDbType.Integer), C("name", NpgsqlDbType.Text), C("kind", NpgsqlDbType.Varchar), C("account_id", NpgsqlDbType.Integer), C("category_id", NpgsqlDbType.Integer, true), C("amount", NpgsqlDbType.Numeric), C("frequency", NpgsqlDbType.Varchar), C("next_date", NpgsqlDbType.Date), C("source", NpgsqlDbType.Varchar), C("is_active", NpgsqlDbType.Boolean), C("created_at", NpgsqlDbType.TimestampTz), C("match_text", NpgsqlDbType.Text, true), C("amount_tolerance", NpgsqlDbType.Numeric), C("date_window_days", NpgsqlDbType.Smallint), C("source_transaction_id", NpgsqlDbType.Integer, true), C("updated_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("account_id", "accounts"), new("category_id", "categories"), new("source_transaction_id", "transactions")]),
        new("recurring_occurrences", "data/recurring_occurrences.jsonl", [C("id", NpgsqlDbType.Integer), C("recurring_item_id", NpgsqlDbType.Integer), C("due_date", NpgsqlDbType.Date), C("expected_amount", NpgsqlDbType.Numeric), C("status", NpgsqlDbType.Varchar), C("transaction_id", NpgsqlDbType.Integer, true), C("actual_amount", NpgsqlDbType.Numeric, true), C("matched_at", NpgsqlDbType.TimestampTz, true), C("note", NpgsqlDbType.Text, true), C("created_at", NpgsqlDbType.TimestampTz), C("updated_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("recurring_item_id", "recurring_items"), new("transaction_id", "transactions")]),
        new("budget_definitions", "data/budget_definitions.jsonl", [C("id", NpgsqlDbType.Integer), C("category_id", NpgsqlDbType.Integer), C("monthly_amount", NpgsqlDbType.Numeric), C("rollover_enabled", NpgsqlDbType.Boolean), C("effective_from", NpgsqlDbType.Date), C("is_active", NpgsqlDbType.Boolean), C("updated_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("category_id", "categories")]),
        new("budget_months", "data/budget_months.jsonl", [C("id", NpgsqlDbType.Integer), C("budget_id", NpgsqlDbType.Integer), C("month", NpgsqlDbType.Date), C("base_amount", NpgsqlDbType.Numeric), C("rollover_in", NpgsqlDbType.Numeric), C("spent_amount", NpgsqlDbType.Numeric), C("rollover_enabled", NpgsqlDbType.Boolean), C("available_amount", NpgsqlDbType.Numeric, true), C("scheduled_amount", NpgsqlDbType.Numeric, true), C("remaining_after_scheduled", NpgsqlDbType.Numeric, true), C("remaining_amount", NpgsqlDbType.Numeric, true), C("progress_percent", NpgsqlDbType.Numeric, true)], ForeignKeys: [new("budget_id", "budget_definitions")]),
        new("budget_month_closures", "data/budget_month_closures.jsonl", [C("month", NpgsqlDbType.Date), C("closed_at", NpgsqlDbType.TimestampTz)], IdColumn: "month", HasSequence: false),
        new("transaction_splits", "data/transaction_splits.jsonl", [C("id", NpgsqlDbType.Integer), C("transaction_id", NpgsqlDbType.Integer), C("category_id", NpgsqlDbType.Integer), C("amount", NpgsqlDbType.Numeric), C("memo", NpgsqlDbType.Text, true), C("line_order", NpgsqlDbType.Smallint)], ForeignKeys: [new("transaction_id", "transactions"), new("category_id", "categories")]),
        new("transaction_transfer_pairs", "data/transaction_transfer_pairs.jsonl", [C("id", NpgsqlDbType.Integer), C("transaction_id_a", NpgsqlDbType.Integer), C("transaction_id_b", NpgsqlDbType.Integer), C("created_at", NpgsqlDbType.TimestampTz)], ForeignKeys: [new("transaction_id_a", "transactions"), new("transaction_id_b", "transactions")]),
        new("statement_sessions", "data/statement_sessions.jsonl", [C("id", NpgsqlDbType.Integer), C("account_id", NpgsqlDbType.Integer), C("period_start", NpgsqlDbType.Date), C("period_end", NpgsqlDbType.Date), C("statement_opening_balance", NpgsqlDbType.Numeric), C("statement_closing_balance", NpgsqlDbType.Numeric), C("status", NpgsqlDbType.Varchar), C("created_at", NpgsqlDbType.TimestampTz), C("closed_at", NpgsqlDbType.TimestampTz, true)], ForeignKeys: [new("account_id", "accounts")]),
        new("statement_session_transactions", "data/statement_session_transactions.jsonl", [C("session_id", NpgsqlDbType.Integer), C("transaction_id", NpgsqlDbType.Integer), C("created_at", NpgsqlDbType.TimestampTz)], IdColumn: null, ForeignKeys: [new("session_id", "statement_sessions"), new("transaction_id", "transactions")], HasSequence: false),
    ];

    private static Column C(string name, NpgsqlDbType type, bool nullable = false) => new(name, type, nullable);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TableSpec>> EntityTables =
        new Dictionary<string, IReadOnlyList<TableSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            ["profile"] = Tables.Where(t => t.Name is "people" or "user_profiles").ToArray(),
            ["settings"] = Tables.Where(t => t.Name == "household_settings").ToArray(),
            ["accounts"] = Tables.Where(t => t.Name == "accounts").ToArray(),
            ["categories"] = Tables.Where(t => t.Name == "categories").ToArray(),
            ["rules"] = Tables.Where(t => t.Name == "transaction_rules").ToArray(),
            ["goals"] = Tables.Where(t => t.Name == "savings_goals" || t.Name == "goal_images").ToArray(),
            ["recurring"] = Tables.Where(t => t.Name is "recurring_items" or "recurring_occurrences").ToArray(),
            ["budgets"] = Tables.Where(t => t.Name is "budget_definitions" or "budget_months" or "budget_month_closures").ToArray(),
            ["transactions"] = Tables.Where(t => t.Name is "transactions" or "transaction_splits" or "transaction_transfer_pairs").ToArray(),
        };

    public static async Task<PortableExportResult> ExportArchiveAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var entries = new List<ArchiveEntry>();
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in Tables)
                entries.Add(await AddTableEntryAsync(archive, connection, transaction, table));

            var images = await ReadImagesAsync(connection, transaction);
            foreach (var image in images)
            {
                var path = $"images/{image.Id}.bin";
                entries.Add(AddBinaryEntry(archive, path, image.Content));
            }

            var manifest = new Manifest(Format, Version, DateTimeOffset.UtcNow, entries);
            AddJsonEntry(archive, "manifest.json", manifest);
        }
        await transaction.CommitAsync();
        return new(output.ToArray(), "application/zip", $"finova-portable-{DateTime.UtcNow:yyyyMMdd}.zip");
    }

    public static async Task<PortableExportResult> ExportEntityAsync(string entity)
    {
        if (entity.Equals("images", StringComparison.OrdinalIgnoreCase))
            return await ExportImagesAsync();
        if (!EntityTables.TryGetValue(entity, out var selected))
            throw new KeyNotFoundException($"Unknown portable export entity '{entity}'.");

        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var payload = new Dictionary<string, object?>();
        foreach (var table in selected)
            payload[table.Name] = await ReadTableAsync(connection, transaction, table);
        await transaction.CommitAsync();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return new(bytes, "application/json", $"finova-{entity.ToLowerInvariant()}.json");
    }

    public static async Task<PortableExportResult> ExportImagesAsync()
    {
        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var images = await ReadImagesAsync(connection, transaction);
        await transaction.CommitAsync();

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var metadata = images.Select(image => new
            {
                id = image.Id,
                content_type = image.ContentType,
                file_name = image.FileName,
                content_hash = image.Hash,
                created_at = image.CreatedAt,
                path = $"images/{image.Id}.bin",
            }).ToArray();
            AddJsonEntry(archive, "manifest.json", new { format = Format, version = Version, images = metadata });
            foreach (var image in images)
                AddBinaryEntry(archive, $"images/{image.Id}.bin", image.Content);
        }
        return new(output.ToArray(), "application/zip", $"finova-images-{DateTime.UtcNow:yyyyMMdd}.zip");
    }

    public static async Task<PortableImportSummary> ImportArchiveAsync(Stream input)
    {
        if (!input.CanRead) throw new ArgumentException("The uploaded archive cannot be read.");
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        var parsed = ParseArchive(buffer.ToArray());

        await using var connection = PostgreSqlQuerier.BuildConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var truncate = new NpgsqlCommand($"TRUNCATE TABLE {string.Join(", ", Tables.Select(t => t.Name))} RESTART IDENTITY CASCADE", connection, transaction))
            await truncate.ExecuteNonQueryAsync();

        var inserted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var table in ImportOrder)
        {
            var rows = parsed.Tables[table.Name];
            foreach (var row in rows)
                await InsertRowAsync(connection, transaction, table, row, parsed.Images);
            inserted[table.Name] = rows.Count;
        }
        await ResetSequencesAsync(connection, transaction);
        await transaction.CommitAsync();
        return new(inserted, parsed.Images.Count, parsed.Manifest.Format, parsed.Manifest.Version);
    }

    private static readonly IReadOnlyList<TableSpec> ImportOrder =
    [
        Tables.Single(t => t.Name == "people"),
        Tables.Single(t => t.Name == "user_profiles"),
        Tables.Single(t => t.Name == "household_settings"),
        Tables.Single(t => t.Name == "transaction_type_codes"),
        Tables.Single(t => t.Name == "categories"),
        Tables.Single(t => t.Name == "accounts"),
        Tables.Single(t => t.Name == "transaction_import_batches"),
        Tables.Single(t => t.Name == "transactions"),
        Tables.Single(t => t.Name == "transaction_import_rows"),
        Tables.Single(t => t.Name == "transaction_rules"),
        Tables.Single(t => t.Name == "goal_images"),
        Tables.Single(t => t.Name == "savings_goals"),
        Tables.Single(t => t.Name == "recurring_items"),
        Tables.Single(t => t.Name == "recurring_occurrences"),
        Tables.Single(t => t.Name == "budget_definitions"),
        Tables.Single(t => t.Name == "budget_months"),
        Tables.Single(t => t.Name == "budget_month_closures"),
        Tables.Single(t => t.Name == "transaction_splits"),
        Tables.Single(t => t.Name == "transaction_transfer_pairs"),
        Tables.Single(t => t.Name == "statement_sessions"),
        Tables.Single(t => t.Name == "statement_session_transactions"),
    ];

    private static async Task<ArchiveEntry> AddTableEntryAsync(ZipArchive archive, NpgsqlConnection connection, NpgsqlTransaction transaction, TableSpec table)
    {
        var content = await ReadTableJsonLinesAsync(connection, transaction, table);
        var entry = archive.CreateEntry(table.Entry, CompressionLevel.Optimal);
        await using (var stream = entry.Open())
            await stream.WriteAsync(content);
        return new(table.Entry, Hash(content), content.LongLength, CountLines(content));
    }

    private static async Task<byte[]> ReadTableJsonLinesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TableSpec table)
    {
        await using var command = new NpgsqlCommand($"SELECT {string.Join(", ", table.Columns.Select(c => c.Name))} FROM {table.Name} ORDER BY {table.IdColumn ?? table.Columns[0].Name}", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        await using var output = new MemoryStream();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < table.Columns.Count; i++)
                row[table.Columns[i].Name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            await JsonSerializer.SerializeAsync(output, row, JsonOptions);
            await output.WriteAsync("\n"u8.ToArray());
        }
        return output.ToArray();
    }

    private static async Task<List<Dictionary<string, object?>>> ReadTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TableSpec table)
    {
        var bytes = await ReadTableJsonLinesAsync(connection, transaction, table);
        return ParseJsonLines(bytes).Select(row => row.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)).ToList();
    }

    private static async Task<List<ImageData>> ReadImagesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        const string sql = "SELECT id, content_type, file_name, content, content_hash, created_at FROM goal_images ORDER BY id";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<ImageData>();
        while (await reader.ReadAsync())
        {
            var content = (byte[])reader.GetValue(3);
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), content, reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return result;
    }

    private static ArchiveEntry AddBinaryEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
        return new(path, Hash(content), content.LongLength, 0);
    }

    private static void AddJsonEntry<T>(ZipArchive archive, string path, T value)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static ParsedArchive ParseArchive(byte[] bytes)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.ToArray();
        if (entries.Any(entry => entry.FullName.StartsWith("/", StringComparison.Ordinal) || entry.FullName.Contains("..", StringComparison.Ordinal) || entry.FullName.Contains('\\')))
            throw new InvalidDataException("The archive contains an unsafe path.");
        if (entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("The archive contains duplicate entries.");

        var manifestEntry = entries.SingleOrDefault(entry => entry.FullName == "manifest.json")
            ?? throw new InvalidDataException("The archive is missing manifest.json.");
        Manifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("The archive manifest is invalid.");
        if (manifest.Format != Format || manifest.Version != Version)
            throw new NotSupportedException("This Finova archive format version is not supported.");

        var tablePaths = Tables.Select(table => table.Entry).ToHashSet(StringComparer.Ordinal);
        var manifestPaths = manifest.Entries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        var actualPaths = entries.Where(entry => entry.FullName != "manifest.json").Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        if (manifest.Entries.Count != entries.Length - 1 || manifestPaths.Count != manifest.Entries.Count || !manifestPaths.SetEquals(actualPaths) || manifestPaths.Any(path => !tablePaths.Contains(path) && !path.StartsWith("images/", StringComparison.Ordinal)))
            throw new InvalidDataException("The archive manifest does not match its contents.");

        var tableRows = new Dictionary<string, List<Dictionary<string, JsonElement>>>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            var entry = entries.SingleOrDefault(item => item.FullName == table.Entry)
                ?? throw new InvalidDataException($"The archive is missing {table.Entry}.");
            var content = ReadEntry(entry);
            var manifestItem = manifest.Entries.SingleOrDefault(item => item.Path == table.Entry)
                ?? throw new InvalidDataException($"The archive manifest is missing {table.Entry}.");
            VerifyEntry(manifestItem, content);
            tableRows[table.Name] = ParseJsonLines(content);
            ValidateRows(table, tableRows[table.Name]);
        }

        var images = new Dictionary<int, byte[]>();
        foreach (var table in Tables.Where(table => table.Name == "goal_images"))
        {
            foreach (var row in tableRows[table.Name])
            {
                var id = row["id"].GetInt32();
                var imageEntry = entries.SingleOrDefault(entry => entry.FullName == $"images/{id}.bin")
                    ?? throw new InvalidDataException($"The archive is missing image {id}.");
                var content = ReadEntry(imageEntry);
                var imageManifest = manifest.Entries.SingleOrDefault(item => item.Path == imageEntry.FullName)
                    ?? throw new InvalidDataException($"The archive manifest is missing image {id}.");
                VerifyEntry(imageManifest, content);
                var hash = row["content_hash"].GetString();
                if (!string.Equals(hash, Hash(content), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Image {id} failed its content hash check.");
                images[id] = content;
            }
        }
        ValidateForeignKeys(tableRows);
        if (tableRows["household_settings"].Count != 1 || tableRows["household_settings"][0]["id"].GetInt32() != 1)
            throw new InvalidDataException("The archive must contain exactly one household settings record with id 1.");
        return new(manifest, tableRows, images);
    }

    private static void ValidateRows(TableSpec table, IReadOnlyList<Dictionary<string, JsonElement>> rows)
    {
        var columns = table.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Keys.Any(key => !columns.Contains(key)) || table.Columns.Any(column => !row.ContainsKey(column.Name)))
                throw new InvalidDataException($"A record in {table.Name} has the wrong shape.");
            foreach (var column in table.Columns)
                if (row[column.Name].ValueKind == JsonValueKind.Null && !column.Nullable)
                    throw new InvalidDataException($"A required value is missing from {table.Name}.{column.Name}.");
        }
        if (table.IdColumn is not null)
        {
            var ids = rows.Select(row => row[table.IdColumn].ToString()).ToArray();
            if (ids.Length != ids.Distinct(StringComparer.Ordinal).Count())
                throw new InvalidDataException($"The archive contains duplicate {table.Name} identifiers.");
        }
        ValidateKnownValues(table, rows);
    }

    private static void ValidateKnownValues(TableSpec table, IReadOnlyList<Dictionary<string, JsonElement>> rows)
    {
        var allowed = table.Name switch
        {
            "accounts" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["account_type"] = ["current", "savings", "credit", "cash", "investment"] },
            "transaction_rules" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["direction"] = ["in", "out", "any"] },
            "savings_goals" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["status"] = ["active", "completed", "archived"] },
            "recurring_items" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["kind"] = ["bill", "income", "transfer"], ["frequency"] = ["weekly", "fortnightly", "monthly", "quarterly", "yearly"] },
            "recurring_occurrences" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["status"] = ["expected", "matched", "paid", "skipped"] },
            "transaction_import_batches" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["status"] = ["preview", "completed", "undone"] },
            "transaction_import_rows" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["outcome"] = ["ready", "imported", "skipped", "rejected", "undone"] },
            "statement_sessions" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["status"] = ["open", "closed"] },
            _ => new Dictionary<string, string[]>(StringComparer.Ordinal),
        };
        foreach (var (column, values) in allowed)
            if (rows.Any(row => !values.Contains(row[column].GetString(), StringComparer.Ordinal)))
                throw new InvalidDataException($"The archive contains an invalid {table.Name}.{column} value.");
    }

    private static void ValidateForeignKeys(Dictionary<string, List<Dictionary<string, JsonElement>>> tables)
    {
        foreach (var table in Tables)
        {
            foreach (var reference in table.References)
            {
                var referenced = tables[reference.Table].Select(row => row[reference.ReferencedColumn].ToString()).ToHashSet(StringComparer.Ordinal);
                foreach (var row in tables[table.Name])
                    if (row[reference.Column].ValueKind != JsonValueKind.Null && !referenced.Contains(row[reference.Column].ToString()))
                        throw new InvalidDataException($"The archive contains a dangling {table.Name}.{reference.Column} reference.");
            }
        }
    }

    private static async Task InsertRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TableSpec table, Dictionary<string, JsonElement> row, Dictionary<int, byte[]> images)
    {
        var columns = table.ExternalImageContent ? table.Columns.Select(column => column.Name).Append("content").ToArray() : table.Columns.Select(column => column.Name).ToArray();
        var parameters = columns.Select((_, index) => $"@p{index}").ToArray();
        await using var command = new NpgsqlCommand($"INSERT INTO {table.Name} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})", connection, transaction);
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var column = table.Columns[index];
            var parameter = command.Parameters.Add($"p{index}", column.Type);
            parameter.Value = ToDbValue(row[column.Name], column.Type, column.Nullable);
        }
        if (table.ExternalImageContent)
        {
            var parameter = command.Parameters.Add("p" + table.Columns.Count, NpgsqlDbType.Bytea);
            parameter.Value = images[row["id"].GetInt32()];
        }
        await command.ExecuteNonQueryAsync();
    }

    private static object ToDbValue(JsonElement element, NpgsqlDbType type, bool nullable, byte[]? imageContent = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return DBNull.Value;
        if (imageContent is not null) return imageContent;
        return type switch
        {
            NpgsqlDbType.Integer => element.GetInt32(),
            NpgsqlDbType.Smallint => element.GetInt16(),
            NpgsqlDbType.Bigint => element.GetInt64(),
            NpgsqlDbType.Numeric => element.GetDecimal(),
            NpgsqlDbType.Boolean => element.GetBoolean(),
            NpgsqlDbType.Date => DateOnly.Parse(element.GetString()!),
            NpgsqlDbType.Timestamp => DateTime.Parse(element.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind),
            NpgsqlDbType.TimestampTz => DateTimeOffset.Parse(element.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind),
            _ => element.GetString()!,
        };
    }

    private static async Task ResetSequencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        foreach (var table in Tables.Where(table => table.HasSequence && table.IdColumn is not null))
        {
            var sql = $"SELECT setval(pg_get_serial_sequence('{table.Name}', '{table.IdColumn}'), COALESCE(MAX({table.IdColumn}), 0) + 1, false) FROM {table.Name}";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteScalarAsync();
        }
    }

    private static List<Dictionary<string, JsonElement>> ParseJsonLines(byte[] content)
    {
        var rows = new List<Dictionary<string, JsonElement>>();
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Archive data entries must contain JSON objects.");
            rows.Add(document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal));
        }
        return rows;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void VerifyEntry(ArchiveEntry expected, byte[] content)
    {
        if (expected.Bytes != content.LongLength || !string.Equals(expected.Sha256, Hash(content), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Archive entry {expected.Path} failed its integrity check.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int CountLines(byte[] bytes) => bytes.Length == 0 ? 0 : bytes.Count(value => value == (byte)'\n');
}
