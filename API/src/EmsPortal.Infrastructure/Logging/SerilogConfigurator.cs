using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace EmsPortal.Infrastructure.Logging;

/// <summary>
/// Centralized Serilog configuration shared by all three hosts (Api, Workers,
/// McpServer). Writes structured logs to the shared SQL Server (auto-provisioning
/// the log table) and enriches every entry with the correlation ID (from
/// <c>LogContext</c>), service name, and environment (Audit &amp; Logging blueprint).
/// </summary>
public static class SerilogConfigurator
{
    public const string LogTableName = "Logs";

    /// <summary>
    /// Applies the platform's standard sinks and enrichers to a logger configuration.
    /// The SQL table takes Error and Fatal only.
    /// Log level defaults to Debug in Development and Information elsewhere, and can be
    /// overridden via the <c>Serilog:MinimumLevel</c> configuration value.
    /// </summary>
    public static void Configure(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string serviceName,
        string environmentName)
    {
        var minimumLevel = ResolveMinimumLevel(configuration, environmentName);
        var connectionString = configuration.GetConnectionString(ConfigurationSections.SqlServerConnection);

        loggerConfiguration
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Environment", environmentName)
            .WriteTo.Console();

        // The table keeps errors only; the console still shows everything at the minimum level.
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            loggerConfiguration.WriteTo.MSSqlServer(
                connectionString: connectionString,
                restrictedToMinimumLevel: LogEventLevel.Error,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = LogTableName,
                    AutoCreateSqlTable = true,
                });
        }
    }

    private static LogEventLevel ResolveMinimumLevel(IConfiguration configuration, string environmentName)
    {
        var configured = configuration.GetSection(ConfigurationSections.Serilog)["MinimumLevel"];
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse<LogEventLevel>(configured, ignoreCase: true, out var level))
        {
            return level;
        }

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Debug
            : LogEventLevel.Information;
    }
}
