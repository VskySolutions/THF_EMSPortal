using System.Text.Json;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Shared.Contracts;

namespace EmsPortal.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions before they reach the caller, logs the full exception (type, message,
/// stack trace, correlation ID) via Serilog.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const string GenericMessage = "An unexpected error occurred";
    private const string IncludeDetailsKey = "ErrorHandling:IncludeExceptionDetails";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly bool _includeExceptionDetails;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        // Configurable via appsettings; defaults to on in Development.
        _includeExceptionDetails = configuration.GetValue<bool?>(IncludeDetailsKey) ?? environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = correlationContext.CorrelationId;
            _logger.LogError(
                ex,
                "Unhandled exception {ExceptionType} CorrelationId={CorrelationId}",
                ex.GetType().FullName,
                correlationId);

            if (context.Response.HasStarted)
            {
                // Headers already flushed — cannot rewrite the response.
                throw;
            }

            var details = _includeExceptionDetails
                ? $"{correlationId} | {ex.GetType().Name}: {ex.Message}"
                : correlationId;

            var envelope = ApiResponseFactory.Error(ApiErrorCodes.InternalError, GenericMessage, details);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, SerializerOptions), context.RequestAborted);
        }
    }
}
