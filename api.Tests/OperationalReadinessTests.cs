using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using financesApi.health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Microsoft.Extensions.Logging;

namespace financesApi.Tests;

public sealed class OperationalReadinessTests
{
    [Fact]
    public async Task Liveness_DoesNotCallDatabaseProbe()
    {
        var probe = new RecordingProbe(new InvalidOperationException("must not be called"));
        await using var factory = new FinovaFactory(probe);

        using var response = await factory.CreateClient().GetAsync("/status/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, probe.Calls);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Empty(document.RootElement.GetProperty("checks").EnumerateObject());
        AssertCommonHealthFields(document.RootElement);
    }

    [Theory]
    [InlineData("/status/ready")]
    [InlineData("/status/health")]
    public async Task ReadinessEndpoints_ReturnDatabaseStatus(string path)
    {
        var probe = new RecordingProbe();
        await using var factory = new FinovaFactory(probe);

        using var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal("healthy", root.GetProperty("database").GetString());
        Assert.Equal("healthy", root.GetProperty("checks").GetProperty("database").GetString());
        AssertCommonHealthFields(root);
    }

    [Fact]
    public async Task Readiness_ReturnsServiceUnavailableWithoutLeakingFailure()
    {
        var probe = new RecordingProbe(new InvalidOperationException("secret connection detail"));
        await using var factory = new FinovaFactory(probe);

        using var response = await factory.CreateClient().GetAsync("/status/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("unavailable", document.RootElement.GetProperty("database").GetString());
    }

    [Fact]
    public async Task Readiness_CancelsProbeAfterConfiguredTimeout()
    {
        var probe = new RecordingProbe(delayUntilCancelled: true);
        await using var factory = new FinovaFactory(probe);
        var startedAt = DateTimeOffset.UtcNow;

        using var response = await factory.CreateClient().GetAsync("/status/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5));
        Assert.True(probe.WasCancelled);
    }

    [Fact]
    public async Task RequestAndExceptionLogs_AreStructuredAndCorrelated()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new FinovaFactory(new RecordingProbe(), logs, includeTestController: true);

        using var response = await factory.CreateClient().GetAsync("/__operational-test/error");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var traceId = document.RootElement.GetProperty("traceId").GetString();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        var requestLog = Assert.Single(logs.Entries, entry => entry.EventId.Id == 1000);
        Assert.Equal("GET", requestLog.State["RequestMethod"]);
        Assert.Equal("/__operational-test/error", requestLog.State["RequestPath"]);
        Assert.Equal(500, Convert.ToInt32(requestLog.State["StatusCode"]));
        Assert.Contains(requestLog.Scopes, scope =>
            scope.TryGetValue("TraceId", out var value) && Convert.ToString(value) == traceId);
        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Error
            && entry.State.TryGetValue("TraceId", out var value)
            && Convert.ToString(value) == traceId);
    }

    private static void AssertCommonHealthFields(JsonElement root)
    {
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.True(root.TryGetProperty("uptime", out _));
        Assert.True(root.TryGetProperty("version", out _));
    }

    private sealed class FinovaFactory(
        IDatabaseReadinessProbe probe,
        CapturingLoggerProvider? logs = null,
        bool includeTestController = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseReadinessProbe>();
                services.AddSingleton(probe);
                if (includeTestController)
                    services.AddControllers().AddApplicationPart(typeof(OperationalTestController).Assembly);
            });
            if (logs is not null)
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(logs);
                });
            }
        }
    }

    private sealed class RecordingProbe(
        Exception? exception = null,
        bool delayUntilCancelled = false) : IDatabaseReadinessProbe
    {
        public int Calls { get; private set; }
        public bool WasCancelled { get; private set; }

        public async Task CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (delayUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
            }
            if (exception is not null) throw exception;
        }
    }

    private sealed record CapturedLog(
        LogLevel Level,
        EventId EventId,
        IReadOnlyDictionary<string, object?> State,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();
        public ConcurrentBag<CapturedLog> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
        public void Dispose() { }
        public void SetScopeProvider(IExternalScopeProvider provider) => scopeProvider = provider;

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                owner.scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var values = AsDictionary(state);
                var scopes = new List<IReadOnlyDictionary<string, object?>>();
                owner.scopeProvider.ForEachScope((scope, collection) =>
                    collection.Add(AsDictionary(scope)), scopes);
                owner.Entries.Add(new(logLevel, eventId, values, scopes));
            }

            private static IReadOnlyDictionary<string, object?> AsDictionary<TState>(TState state)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                    return values.ToDictionary(item => item.Key, item => item.Value);
                return new Dictionary<string, object?> { ["Message"] = state?.ToString() };
            }
        }
    }
}

[ApiController]
[Route("__operational-test")]
public sealed class OperationalTestController : ControllerBase
{
    [HttpGet("error")]
    public IActionResult Error() => throw new InvalidOperationException("test failure");
}
