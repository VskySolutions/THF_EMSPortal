using global::Hangfire;
using EmsPortal.Application;
using EmsPortal.Infrastructure;
using EmsPortal.Infrastructure.Hangfire;
using EmsPortal.Infrastructure.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Structured logging to SQL Server, enriched with correlation ID, service, environment.
builder.Services.AddSerilog((_, loggerConfiguration) =>
    SerilogConfigurator.Configure(
        loggerConfiguration,
        builder.Configuration,
        "EmsPortal.Workers",
        builder.Environment.EnvironmentName));

// Clean Architecture composition root.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Hangfire server: the same composition the API runs by default (Hangfire:ServerEnabled). This host is for
// a deployment that runs the jobs apart from the web process, and runs them whatever that setting says.
if (HangfireStorageConfigurator.IsConfigured(builder.Configuration))
{
    // Reconstruct ITenantContext from each job payload before the handler runs.
    GlobalJobFilters.Filters.Add(new TenantHangfireJobFilter());

    builder.Services.AddHangfire(config => HangfireStorageConfigurator.Configure(config, builder.Configuration));
    builder.Services.AddEmsPortalHangfireServer(builder.Configuration);
}

var host = builder.Build();
host.Run();
