using financesApi.services;
using financesApi.models;
using financesApi.health;
using financesApi.middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});
builder.Logging.AddFilter(
    "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IDatabaseReadinessProbe, PostgreSqlReadinessProbe>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(3));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.UseMiddleware<StructuredRequestLoggingMiddleware>();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            InvalidDataException => StatusCodes.Status400BadRequest,
            NotSupportedException => StatusCodes.Status400BadRequest,
            ImportBatchConflictException => StatusCodes.Status409Conflict,
            ImportBatchExpiredException => StatusCodes.Status410Gone,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ResourceNotFoundException => StatusCodes.Status404NotFound,
            ResourceConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Finova.ExceptionHandler");
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled request failure with trace ID {TraceId}", context.TraceIdentifier);
        else
            logger.LogWarning("Handled {ExceptionType} as HTTP {StatusCode} with trace ID {TraceId}",
                exception?.GetType().Name ?? "UnknownException", context.Response.StatusCode, context.TraceIdentifier);
        await context.Response.WriteAsJsonAsync(new
        {
            error = exception is ArgumentException or InvalidDataException or NotSupportedException
                or ImportBatchConflictException or ImportBatchExpiredException
                or KeyNotFoundException or ResourceNotFoundException or ResourceConflictException
                ? exception.Message
                : "Finova could not complete that request.",
            traceId = context.TraceIdentifier,
        });
    });
});

app.MapControllers();

var readinessOptions = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = OperationalHealthResponseWriter.WriteAsync,
};
app.MapHealthChecks("/status/ready", readinessOptions);
app.MapHealthChecks("/status/health", readinessOptions);
app.MapHealthChecks("/status/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = OperationalHealthResponseWriter.WriteAsync,
});

app.Run();

public partial class Program;
