using System.Text.Json;
using System.Threading.RateLimiting;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace EmsPortal.Api.RateLimiting;

/// <summary>
/// The ceilings the API runs under: rate limits, request timeouts and the request-body size, all bound from
/// <c>Limits</c> in appsettings.json. Counting is in memory, which is right for the one IIS server this runs on.
/// </summary>
public static class RateLimitingExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IServiceCollection AddEmsPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(ConfigurationSections.Limits).Get<LimitsOptions>() ?? new LimitsOptions();
        var rate = limits.RateLimiting;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // Every request, by caller. The named policies stack on top of it for the endpoints that carry them.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Window(rate.Enabled, CallerKey(context), rate.General));

            options.AddPolicy(RateLimitPolicies.Login, context => Window(rate.Enabled, ClientIp(context), rate.Login));
            options.AddPolicy(RateLimitPolicies.PasswordReset, context => Window(rate.Enabled, ClientIp(context), rate.PasswordReset));
            options.AddPolicy(RateLimitPolicies.PublicFormRead, context => Window(rate.Enabled, ClientIp(context), rate.PublicFormRead));
            options.AddPolicy(RateLimitPolicies.PublicFormWrite, context => Window(rate.Enabled, ClientIp(context), rate.PublicFormWrite));
            options.AddPolicy(RateLimitPolicies.PublicContent, context => Window(rate.Enabled, ClientIp(context), rate.PublicContent));
            options.AddPolicy(RateLimitPolicies.Email, context => Window(rate.Enabled, CallerKey(context), rate.Email));
            options.AddPolicy(RateLimitPolicies.MaconomySearch, context => Window(rate.Enabled, CallerKey(context), rate.MaconomySearch));
            options.AddPolicy(RateLimitPolicies.Upload, context => rate.Enabled && rate.UploadConcurrency > 0
                ? RateLimitPartition.GetConcurrencyLimiter(CallerKey(context), _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = rate.UploadConcurrency,
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(CallerKey(context)));
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = TimeoutPolicy(limits.Timeouts.DefaultSeconds);
            options.AddPolicy(RequestTimeoutPolicies.LongRunning, TimeoutPolicy(limits.Timeouts.LongRunningSeconds));
        });

        // One body ceiling whichever host is serving. IIS has another of its own in web.config, above this one.
        var maxBody = limits.Uploads.MaxRequestBodyBytes;
        services.Configure<KestrelServerOptions>(kestrel => kestrel.Limits.MaxRequestBodySize = maxBody);
        services.Configure<IISServerOptions>(iis => iis.MaxRequestBodySize = maxBody);

        return services;
    }

    /// <summary>After authentication, so a signed-in caller is counted as themselves rather than as their address.</summary>
    public static IApplicationBuilder UseEmsPortalRateLimiting(this WebApplication app)
    {
        app.UseRateLimiter();
        app.UseRequestTimeouts();
        return app;
    }

    /// <summary>The limits in force, in one line for the startup log.</summary>
    public static string Describe(this LimitsOptions limits)
    {
        var rate = limits.RateLimiting;
        static string Each(RateLimitWindow window) => window.PermitLimit > 0 ? $"{window.PermitLimit}/{window.WindowSeconds}s" : "off";
        var rates = rate.Enabled
            ? $"rate limits general {Each(rate.General)}, login {Each(rate.Login)}, password reset {Each(rate.PasswordReset)}, " +
              $"public form {Each(rate.PublicFormRead)} read / {Each(rate.PublicFormWrite)} write, email {Each(rate.Email)}, " +
              $"Maconomy {Each(rate.MaconomySearch)}, public content {Each(rate.PublicContent)}, {rate.UploadConcurrency} uploads at once"
            : "rate limits OFF";
        return $"{rates}; page size max {limits.Paging.MaxPageSize}, bulk ids max {limits.Paging.MaxBulkIds}; " +
               $"uploads {limits.Uploads.MaxFileMb} MB (branding {limits.Uploads.BrandingImageMb}/{limits.Uploads.BrandingBackgroundMb} MB), " +
               $"body {limits.Uploads.MaxRequestBodyMb} MB; timeouts {limits.Timeouts.DefaultSeconds}s, long-running {limits.Timeouts.LongRunningSeconds}s";
    }

    // The signed-in user or API key; anybody else is their address.
    private static string CallerKey(HttpContext context)
    {
        var subject = context.User.FindFirst(ClaimTypeNames.Subject)?.Value;
        return string.IsNullOrEmpty(subject) ? ClientIp(context) : "user:" + subject;
    }

    private static string ClientIp(HttpContext context)
        => "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static RateLimitPartition<string> Window(bool enabled, string key, RateLimitWindow window)
        => enabled && window.PermitLimit > 0
            ? RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = window.PermitLimit,
                Window = TimeSpan.FromSeconds(Math.Max(1, window.WindowSeconds)),
                SegmentsPerWindow = 6,
                QueueLimit = Math.Max(0, window.QueueLimit),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            })
            : RateLimitPartition.GetNoLimiter(key);

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        // A window knows when it frees up; a concurrency cap does not, and a few seconds is the honest guess there.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : 5;
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "general";

        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitingExtensions))
            .LogWarning("Rate limit {Policy} refused {Method} {Path} for {Caller}; retry after {RetryAfter}s",
                policy, http.Request.Method, http.Request.Path.Value, CallerKey(http), retryAfter);

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = retryAfter.ToString();
        http.Response.ContentType = "application/json";
        var envelope = ApiResponseFactory.Error(ApiErrorCodes.TooManyRequests, "Too many requests.", $"Try again in {retryAfter} seconds.");
        await http.Response.WriteAsync(JsonSerializer.Serialize(envelope, SerializerOptions), cancellationToken);
    }

    private static RequestTimeoutPolicy TimeoutPolicy(int seconds) => new()
    {
        Timeout = TimeSpan.FromSeconds(Math.Max(1, seconds)),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        WriteTimeoutResponse = async http =>
        {
            http.Response.ContentType = "application/json";
            var envelope = ApiResponseFactory.Error(ApiErrorCodes.RequestTimeout, "The request took too long.",
                "The server stopped waiting; try again, or ask for less at once.");
            await http.Response.WriteAsync(JsonSerializer.Serialize(envelope, SerializerOptions));
        },
    };
}
