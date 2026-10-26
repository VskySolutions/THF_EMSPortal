using global::Hangfire;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmsPortal.Infrastructure.Hangfire;

/// <summary>
/// The Hangfire server as either host runs it: the worker threads that drain the shared queue, plus the
/// platform's recurring jobs. The API hosts it by default so one publish carries everything; the Workers
/// host exists for a deployment that would rather run it apart from the web process.
/// </summary>
public static class HangfireServerExtensions
{
    /// <summary>Whether the API should run the server: storage configured and <see cref="HangfireOptions.ServerEnabled"/>.</summary>
    public static bool ServerEnabled(IConfiguration configuration)
        => HangfireStorageConfigurator.IsConfigured(configuration) && Options(configuration).ServerEnabled;

    /// <summary>Registers the server and the recurring jobs. Storage must be registered as well.</summary>
    public static IServiceCollection AddEmsPortalHangfireServer(this IServiceCollection services, IConfiguration configuration)
    {
        var options = Options(configuration);
        services.AddHangfireServer(server =>
        {
            server.WorkerCount = options.WorkerCount > 0 ? options.WorkerCount : Environment.ProcessorCount * 5;
            if (!string.IsNullOrWhiteSpace(options.ServerName))
            {
                server.ServerName = options.ServerName;
            }
        });
        services.AddHostedService<UniversalFeaturesRecurringJobs>();
        return services;
    }

    private static HangfireOptions Options(IConfiguration configuration)
        => configuration.GetSection(ConfigurationSections.Hangfire).Get<HangfireOptions>() ?? new HangfireOptions();
}
