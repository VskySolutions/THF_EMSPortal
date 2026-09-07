using EmsPortal.Application.Abstractions.Security;
using Serilog.Context;

namespace EmsPortal.Api.Security;

/// <summary>
/// Registered first in the pipeline (before auth) so the correlation ID is available to every log
/// entry, including those written during auth failures (REQ-INF-006).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var provided)
            && !string.IsNullOrWhiteSpace(provided)
                ? provided.ToString()
                : Guid.NewGuid().ToString();

        correlationContext.Set(correlationId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Enrich every log entry written during this request with the correlation ID.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
