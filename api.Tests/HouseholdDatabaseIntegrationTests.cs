using System.Net.Http.Json;
using financesApi.models;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace financesApi.Tests;

[Collection("Finova database integration")]
public sealed class HouseholdDatabaseIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new();
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        client = factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task HouseholdData_PersistsAcrossEnrollmentAccountTransactionAndBudgetApis()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var householdName = $"Integration household {suffix}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var enrollment = await PutAsync<EnrollmentStatusDto>("/enrollment", new
        {
            firstName = "Taylor",
            lastName = $"Tester {suffix}",
            householdName,
        });

        Assert.True(enrollment.IsEnrolled);
        Assert.Equal(householdName, enrollment.Profile!.HouseholdName);

        var settings = await PutAsync<HouseholdSettingsDto>("/settings", new
        {
            householdName,
            currencyCode = "GBP",
            locale = "en-GB",
            timezone = "UTC",
        });
        Assert.Equal("UTC", settings.Timezone);

        var account = await PostAsync<AccountDto>("/accounts", new
        {
            name = $"Household current {suffix}",
            isShared = true,
            accountType = "current",
            institution = "Integration Bank",
            lastFour = "1234",
            openingBalance = 1000.00m,
            openingDate = today,
            safeZoneAmount = 100.00m,
            includeInSafeToSpend = true,
            primaryHolderName = "Taylor Tester",
            secondaryHolderName = "Jordan Tester",
        });

        Assert.True(account.IsShared);
        Assert.Equal("Taylor Tester", account.PrimaryHolderName);
        Assert.Equal("Jordan Tester", account.SecondaryHolderName);
        Assert.Equal(1000.00m, account.Balance);

        var category = (await GetAsync<List<CategoryDto>>("/categories"))
            .Single(item => item.Name == "Food & Groceries");
        var transaction = await PostAsync<TransactionDetailDto>("/transactions", new
        {
            date = today,
            accountId = account.Id,
            direction = "expense",
            amount = 42.50m,
            payee = "Integration Market",
            memo = "Database integration transaction",
            categoryId = category.Id,
            splits = Array.Empty<object>(),
        });

        Assert.Equal(-42.50m, transaction.Transaction.Amount);
        Assert.Equal(category.Id, transaction.Transaction.CategoryId);

        var budget = await PutAsync<BudgetDto>("/plan/budgets", new
        {
            categoryId = category.Id,
            monthlyAmount = 300.00m,
            rolloverEnabled = true,
        });

        Assert.Equal(category.Id, budget.CategoryId);
        Assert.True(budget.RolloverEnabled);
        Assert.Equal(257.50m, budget.RemainingAmount);

        var persisted = await QueryAsync(
            """
            SELECT
                (SELECT count(*) FROM user_profiles WHERE id = 1),
                (SELECT count(*) FROM accounts WHERE id = @account),
                (SELECT count(*) FROM transactions WHERE id = @transaction),
                (SELECT count(*) FROM budget_definitions WHERE category_id = @category AND rollover_enabled)
            """,
            command =>
            {
                command.Parameters.AddWithValue("account", account.Id);
                command.Parameters.AddWithValue("transaction", transaction.Transaction.Id);
                command.Parameters.AddWithValue("category", category.Id);
            });

        Assert.Equal(1L, persisted[0]);
        Assert.Equal(1L, persisted[1]);
        Assert.Equal(1L, persisted[2]);
        Assert.Equal(1L, persisted[3]);

        var dashboard = await GetAsync<DashboardDto>("/dashboard");
        Assert.Equal(householdName, dashboard.HouseholdName);
        Assert.Contains(dashboard.Accounts, item => item.AccountId == account.Id);
    }

    private async Task<T> PostAsync<T>(string uri, object body)
    {
        var response = await client.PostAsJsonAsync(uri, body);
        response.EnsureSuccessStatusCode();
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/", response.Headers.Location!.OriginalString);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T> PutAsync<T>(string uri, object body)
    {
        var response = await client.PutAsJsonAsync(uri, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T> GetAsync<T>(string uri) =>
        (await client.GetFromJsonAsync<T>(uri))!;

    private static async Task<long[]> QueryAsync(
        string sql, Action<NpgsqlCommand> configure)
    {
        await using var connection = BuildConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetInt64)
            .ToArray();
    }

    private static NpgsqlConnection BuildConnection() =>
        new(new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("POSTGRES_HOST"),
            Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
            Database = Environment.GetEnvironmentVariable("POSTGRES_DB"),
            Username = Environment.GetEnvironmentVariable("POSTGRES_USER"),
            Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
            SslMode = Enum.Parse<SslMode>(
                Environment.GetEnvironmentVariable("POSTGRES_SSL_MODE") ?? "Prefer"),
        }.ConnectionString);
}
