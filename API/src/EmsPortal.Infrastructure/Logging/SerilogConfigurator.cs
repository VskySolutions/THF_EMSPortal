using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using System.Collections.ObjectModel;
using System.Data;
using Serilog;
using Serilog.Core;
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

    /// <summary>The log property, and the table column, naming the tenant an entry belongs to.</summary>
    public const string TenantIdProperty = "TenantId";

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
        string environmentName,
        params ILogEventEnricher[] enrichers)
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

        if (enrichers.Length > 0)
        {
            loggerConfiguration.Enrich.With(enrichers);
        }

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
                },
                columnOptions: LogColumns());
        }
    }

    // TenantId is null for what no tenant caused: startup, anonymous requests, platform jobs.
    private static ColumnOptions LogColumns()
    {
        var columns = new ColumnOptions
        {
            AdditionalColumns = new Collection<SqlColumn>
            {
                new()
                {
                    ColumnName = TenantIdProperty,
                    PropertyName = TenantIdProperty,
                    DataType = SqlDbType.UniqueIdentifier,
                    AllowNull = true,
                },
            },
        };
        columns.TimeStamp.ConvertToUtc = true;
        return columns;
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
