using System.Net.Http.Headers;
using System.Net.Http.Json;
using financesApi.models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace financesApi.Tests;

[Collection("Finova database integration")]
public sealed class ImportFixtureIntegrationTests : IAsyncLifetime
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
    public async Task PersistedOfxFixture_ParsesStableIdentifiersAndAmounts()
    {
        await using var stream = File.OpenRead(FixturePath("valid-statement.ofx"));
        var result = financesApi.services.FinancialFileParserService.ParseRows(
            stream, "valid-statement.ofx");

        Assert.Equal("OFX", result.FileType);
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.NotNull(row.Transaction));
        Assert.Equal("ofx-market-1", ((OfxTransactionDto)result.Rows[0].Transaction!).FitId);
        Assert.Equal(-25.50m, result.Rows[0].Transaction!.Amount);
        Assert.Equal(120.00m, result.Rows[1].Transaction!.Amount);
    }

    [Fact]
    public async Task MixedQifFixture_PersistsPreviewRowsAndCommitsOnlyValidRows()
    {
        var account = await CreateAccountAsync();
        var preview = await UploadAsync(account.Id, "mixed-statement.qif");

        Assert.Equal("QIF", preview.FileType);
        Assert.Equal("preview", preview.Status);
        Assert.Equal(3, preview.Total);
        Assert.Equal(2, preview.Importable);
        Assert.Equal(1, preview.Rejected);
        Assert.Equal(1000.00m, preview.StartingBalance);

        var previewRows = await GetRowsAsync(preview.Id);
        Assert.Equal(3, previewRows.TotalItems);
        Assert.Equal(974.50m, previewRows.Items[0].BalanceAfter);
        Assert.Equal(1094.50m, previewRows.Items[1].BalanceAfter);
        Assert.Equal("rejected", previewRows.Items[2].Outcome);
        Assert.Equal("invalid_amount", previewRows.Items[2].ReasonCode);
        Assert.Contains("invalid amount", previewRows.Items[2].ReasonMessage!,
            StringComparison.OrdinalIgnoreCase);

        var completed = await PostAsync<ImportBatchSummary>(
            $"/transactions/imports/{preview.Id}/commit");
        Assert.Equal("completed", completed.Status);
        Assert.Equal(2, completed.Imported);
        Assert.Equal(1, completed.Rejected);

        var completedRows = await GetRowsAsync(preview.Id);
        Assert.Equal(2, completedRows.Items.Count(item => item.Outcome == "imported"));
        Assert.Single(completedRows.Items, item => item.Outcome == "rejected");

        var duplicatePreview = await UploadAsync(account.Id, "mixed-statement.qif");
        Assert.Equal(0, duplicatePreview.Importable);
        Assert.Equal(2, duplicatePreview.Skipped);
        Assert.Equal(1, duplicatePreview.Rejected);
        var duplicateRows = await GetRowsAsync(duplicatePreview.Id);
        Assert.All(duplicateRows.Items.Where(item => item.Amount.HasValue), item =>
            Assert.Equal("skipped", item.Outcome));

        var persistedAccount = (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!
            .Single(item => item.Id == account.Id);
        Assert.Equal(1094.50m, persistedAccount.Balance);
    }

    private async Task<AccountDto> CreateAccountAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/accounts", new
        {
            name = $"Import account {suffix}",
            isShared = false,
            accountType = "current",
            institution = "Import Bank",
            lastFour = "5678",
            openingBalance = 1000.00m,
            openingDate = new DateOnly(2026, 1, 1),
            safeZoneAmount = 0m,
            includeInSafeToSpend = true,
            primaryHolderName = $"Import Tester {suffix}",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private async Task<ImportBatchSummary> UploadAsync(int accountId, string fixture)
    {
        await using var stream = File.OpenRead(FixturePath(fixture));
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "OfxContent", fixture);
        content.Add(new StringContent(accountId.ToString()), "AccountId");

        var response = await client.PostAsync("/transactions/import/preview", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportBatchSummary>())!;
    }

    private async Task<PagedImportRows> GetRowsAsync(long batchId) =>
        (await client.GetFromJsonAsync<PagedImportRows>(
            $"/transactions/imports/{batchId}/rows"))!;

    private async Task<T> PostAsync<T>(string uri)
    {
        var response = await client.PostAsync(uri, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Imports", name);
}
