using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Reflection;

namespace financesApi.controllers
{
    [ApiController]
    [Route("[controller]")]
    public class statusController : ControllerBase
    {
        [HttpGet("health")]
        public async Task<IActionResult> health()
        {
            var healthStatus = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                uptime = GetUptime(),
                version = GetVersion()
            };

            return Ok(healthStatus);
        }

        [HttpGet("ping")]
        public async Task<IActionResult> ping()
        {
            return Ok(new { message = "pong", timestamp = DateTime.UtcNow });
        }

        [HttpGet("detailed")]
        public async Task<IActionResult> detailed()
        {
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