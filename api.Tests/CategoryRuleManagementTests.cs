using System.Net;
using System.Net.Http.Json;
using financesApi.models;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace financesApi.Tests;

[Collection("Finova database integration")]
public sealed class CategoryRuleManagementTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HashSet<int> categoryIds = [];
    private readonly HashSet<int> ruleIds = [];
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
        await DeleteOwnedRowsAsync();
    }

    [Fact]
    public async Task Categories_SupportCrudArchiveRestoreAndProtection()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = await PostAsync<CategoryDto>("/categories", new
        {
            name = "Regression category " + suffix,
            kind = "expense",
            iconKey = "tag",
            colorKey = "blue",
        });
        categoryIds.Add(category.Id);

        var duplicate = await client.PostAsJsonAsync("/categories", new
        {
            name = category.Name.ToUpperInvariant(),
            kind = "expense",
            iconKey = "tag",
            colorKey = "blue",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var rule = await PostAsync<TransactionRuleDto>("/categories/rules", new
        {
            matchText = "Regression reference " + suffix,
            direction = "out",
            categoryId = category.Id,
            priority = 20,
            isActive = true,
        });
        ruleIds.Add(rule.Id);

        var archived = await PutAsync<CategoryDto>($"/categories/{category.Id}", new
        {
            name = "Renamed category " + suffix,
            kind = "expense",
            iconKey = "receipt",
            colorKey = "cyan",
            isArchived = true,
        });
        Assert.True(archived.IsArchived);
        Assert.Equal("Renamed category " + suffix, archived.Name);

        var rulesAfterArchive = await GetAsync<List<TransactionRuleDto>>("/categories/rules");
        Assert.False(rulesAfterArchive.Single(item => item.Id == rule.Id).IsActive);
        Assert.DoesNotContain(await GetAsync<List<CategoryDto>>("/categories"),
            item => item.Id == category.Id);
        Assert.Contains(await GetAsync<List<CategoryDto>>("/categories?includeArchived=true"),
            item => item.Id == category.Id && item.IsArchived);

        var restore = await PutAsync<CategoryDto>($"/categories/{category.Id}", new
        {
            name = archived.Name,
            kind = archived.Kind,
            iconKey = archived.IconKey,
            colorKey = archived.ColorKey,
            isArchived = false,
        });
        Assert.False(restore.IsArchived);

        var deleteWhileReferenced = await client.DeleteAsync($"/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteWhileReferenced.StatusCode);

        var system = (await GetAsync<List<CategoryDto>>("/categories?includeArchived=true"))
            .First(item => item.IsSystem);
        var systemUpdate = await client.PutAsJsonAsync($"/categories/{system.Id}", new
        {
            name = system.Name,
            kind = system.Kind,
            iconKey = system.IconKey,
            colorKey = system.ColorKey,
            isArchived = false,
        });
        Assert.Equal(HttpStatusCode.BadRequest, systemUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.DeleteAsync($"/categories/{system.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/categories/rules/{rule.Id}")).StatusCode);
        ruleIds.Remove(rule.Id);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/categories/{category.Id}")).StatusCode);
        categoryIds.Remove(category.Id);
    }

    [Theory]
    [InlineData("bad", 100, "Rule direction must be in, out, or any.")]
    [InlineData("out", 0, "Rule priority must be between 1 and 100000.")]
    [InlineData("out", 100001, "Rule priority must be between 1 and 100000.")]
    public async Task Rules_RejectInvalidDirectionAndPriority(
        string direction, int priority, string message)
    {
        var response = await client.PostAsJsonAsync("/categories/rules", new
        {
            matchText = "invalid regression rule",
            direction,
            categoryId = 1,
            priority,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rules_SupportCreateEditDuplicateActivationAndArchivedCategoryRejection()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var firstCategory = await CreateCategoryAsync("Rule category A " + suffix);
        var secondCategory = await CreateCategoryAsync("Rule category B " + suffix);

        var created = await PostAsync<TransactionRuleDto>("/categories/rules", new
        {
            matchText = "Rule reference " + suffix,
            direction = "out",
            categoryId = firstCategory.Id,
            priority = 50,
            isActive = true,
        });
        ruleIds.Add(created.Id);
        Assert.Equal("out", created.Direction);
        Assert.Equal(50, created.Priority);
        Assert.True(created.IsActive);

        var duplicate = await PostAsync<TransactionRuleDto>("/categories/rules", new
        {
            matchText = created.ReferenceText,
            direction = "out",
            categoryId = secondCategory.Id,
            priority = 10,
            isActive = false,
        });
        Assert.Equal(created.Id, duplicate.Id);
        Assert.Equal(secondCategory.Id, duplicate.CategoryId);
        Assert.Equal(10, duplicate.Priority);
        Assert.False(duplicate.IsActive);

        var edited = await PutAsync<TransactionRuleDto>($"/categories/rules/{created.Id}", new
        {
            matchText = created.ReferenceText,
            direction = "in",
            categoryId = firstCategory.Id,
            priority = 3,
            isActive = true,
        });
        Assert.Equal("in", edited.Direction);
        Assert.Equal(3, edited.Priority);
        Assert.Equal(firstCategory.Id, edited.CategoryId);
        Assert.True(edited.IsActive);

        var archived = await PutAsync<CategoryDto>($"/categories/{secondCategory.Id}", new
        {
            name = secondCategory.Name,
            kind = secondCategory.Kind,
            iconKey = secondCategory.IconKey,
            colorKey = secondCategory.ColorKey,
            isArchived = true,
        });
        Assert.True(archived.IsArchived);
        var rejected = await client.PostAsJsonAsync("/categories/rules", new
        {
            matchText = "Archived rule " + suffix,
            direction = "out",
            categoryId = secondCategory.Id,
            priority = 100,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/categories/rules/{created.Id}")).StatusCode);
        ruleIds.Remove(created.Id);
    }

    private async Task<CategoryDto> CreateCategoryAsync(string name)
    {
        var category = await PostAsync<CategoryDto>("/categories", new
        {
            name,
            kind = "expense",
            iconKey = "tag",
            colorKey = "violet",
        });
        categoryIds.Add(category.Id);
        return category;
    }

    private async Task<T> PostAsync<T>(string uri, object body)
    {
        var response = await client.PostAsJsonAsync(uri, body);
        response.EnsureSuccessStatusCode();
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

    private async Task DeleteOwnedRowsAsync()
    {
        await using var connection = BuildConnection();
        await connection.OpenAsync();
        foreach (var ruleId in ruleIds)
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM transaction_rules WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", ruleId);
            await command.ExecuteNonQueryAsync();
        }
        foreach (var categoryId in categoryIds)
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM categories WHERE id=@id AND NOT is_system", connection);
            command.Parameters.AddWithValue("id", categoryId);
            await command.ExecuteNonQueryAsync();
        }
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
