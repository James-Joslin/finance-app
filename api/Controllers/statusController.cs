using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Reflection;
using financesApi.utilities;

namespace financesApi.controllers
{
    [ApiController]
    [Route("[controller]")]
    public class statusController : ControllerBase
    {
        private readonly IWebHostEnvironment environment;

        public statusController(IWebHostEnvironment environment)
        {
            this.environment = environment;
        }

        [HttpGet("health")]
        public async Task<IActionResult> health()
        {
            try
            {
                await using var connection = PostgreSqlQuerier.BuildConnection();
                await connection.OpenAsync();
                await using var command = new Npgsql.NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync();
                return Ok(new { status = "healthy", timestamp = DateTime.UtcNow, uptime = GetUptime(), version = GetVersion(), database = "healthy" });
            }
            catch
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { status = "unhealthy", timestamp = DateTime.UtcNow, database = "unavailable" });
            }
        }

        [HttpGet("ping")]
        public IActionResult ping()
        {
            return Ok(new { message = "pong", timestamp = DateTime.UtcNow });
        }

        [HttpGet("detailed")]
        public IActionResult detailed()
        {
            if (!environment.IsDevelopment()) return NotFound();
            var detailedStatus = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                uptime = GetUptime(),
                version = GetVersion(),
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                machineName = Environment.MachineName,
                processId = Environment.ProcessId,
                workingSet = GC.GetTotalMemory(false),
                gcCollections = new
                {
                    gen0 = GC.CollectionCount(0),
                    gen1 = GC.CollectionCount(1),
                    gen2 = GC.CollectionCount(2)
                }
            };

            return Ok(detailedStatus);
        }

        private string GetUptime()
        {
            var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
            return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }

        private string GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        }
    }
}
