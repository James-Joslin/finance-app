using System.Reflection;
using financesApi.utilities;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace financesApi.health;

public interface IDatabaseReadinessProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}

public sealed class PostgreSqlReadinessProbe : IDatabaseReadinessProbe
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = PostgreSqlQuerier.BuildConnection(connectionTimeoutSeconds: 2);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}

public sealed class DatabaseReadinessHealthCheck(IDatabaseReadinessProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await probe.CheckAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database readiness check timed out.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}

public static class OperationalHealthResponseWriter
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status == HealthStatus.Healthy ? "healthy" : "unavailable");
        var response = new Dictionary<string, object?>
        {
            ["status"] = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["uptime"] = FormatUptime(DateTimeOffset.UtcNow - StartedAt),
            ["version"] = Version,
            ["checks"] = checks,
        };
        if (checks.TryGetValue("database", out var database)) response["database"] = database;

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(response);
    }

    private static string FormatUptime(TimeSpan uptime) =>
        $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
}
