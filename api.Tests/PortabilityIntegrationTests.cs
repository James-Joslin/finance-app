using System.Net.Http.Headers;
using System.Net.Http.Json;
using financesApi.models;
using financesApi.services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace financesApi.Tests;

[Collection("Finova database integration")]
public sealed class PortabilityIntegrationTests : IAsyncLifetime
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
    public async Task FullArchive_ExportsAndRestoresTheSameHousehold()
    {
        var settingsBefore = await client.GetFromJsonAsync<HouseholdSettingsDto>("/settings");
        var export = await client.GetAsync("/portability/export/archive");
        export.EnsureSuccessStatusCode();
        var archive = await export.Content.ReadAsByteArrayAsync();

        Assert.NotEmpty(archive);
        Assert.True(archive.LongLength <= PortabilityService.MaxArchiveUploadBytes);

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(archive);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "archive", "household.zip");

        var import = await client.PostAsync("/portability/import", form);
        import.EnsureSuccessStatusCode();
        var summary = await import.Content.ReadFromJsonAsync<PortableImportSummary>();

        Assert.NotNull(summary);
        Assert.Equal("finova-portable", summary.Format);
        Assert.Equal(1, summary.Version);

        var settingsAfter = await client.GetFromJsonAsync<HouseholdSettingsDto>("/settings");
        Assert.Equal(settingsBefore, settingsAfter);
    }
}
