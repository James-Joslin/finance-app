using financesApi.services;
using financesApi.models;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

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
        await context.Response.WriteAsJsonAsync(new
        {
            error = exception is ArgumentException or InvalidDataException or NotSupportedException
                or ImportBatchConflictException or ImportBatchExpiredException
                or KeyNotFoundException or ResourceNotFoundException or ResourceConflictException
                ? exception.Message
                : "Finova could not complete that request."
        });
    });
});

app.MapControllers();

app.Run();
