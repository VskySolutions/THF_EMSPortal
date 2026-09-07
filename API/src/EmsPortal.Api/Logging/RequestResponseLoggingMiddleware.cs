using System.Diagnostics;
using EmsPortal.Application.Abstractions.Security;

namespace EmsPortal.Api.Logging;

/// <summary>
/// Logs a structured entry for each request (method, path, redacted headers, correlation ID) and
/// response (status code, duration).
/// </summary>
public sealed class RequestResponseLoggingMiddleware
{
    private static readonly string[] RedactedHeaders = { "Authorization", "X-Api-Key" };
    private const string RedactedValue = "***REDACTED***";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = correlationContext.CorrelationId;
        var headers = RedactHeaders(context.Request.Headers);

        _logger.LogInformation(
            "HTTP request {Method} {Path} CorrelationId={CorrelationId} Headers={@Headers}",
            context.Request.Method,
            context.Request.Path.Value,
            correlationId,
            headers);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "HTTP response {StatusCode} for {Method} {Path} CorrelationId={CorrelationId} DurationMs={DurationMs}",
                context.Response.StatusCode,
                context.Request.Method,
                context.Request.Path.Value,
                correlationId,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static Dictionary<string, string> RedactHeaders(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var isSensitive = RedactedHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase);
            result[header.Key] = isSensitive ? RedactedValue : header.Value.ToString();
        }

        return result;
    }
}
