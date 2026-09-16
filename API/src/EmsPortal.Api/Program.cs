using FluentValidation;
using FluentValidation.AspNetCore;
using global::Hangfire;
using EmsPortal.Api.Filters;
using EmsPortal.Api.Hangfire;
using EmsPortal.Api.Logging;
using EmsPortal.Api.Middleware;
using EmsPortal.Api.OpenApi;
using EmsPortal.Api.Security;
using EmsPortal.Api.Tenancy;
using Scalar.AspNetCore;
using EmsPortal.Application;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Infrastructure;
using EmsPortal.Infrastructure.Hangfire;
using EmsPortal.Infrastructure.Logging;
using EmsPortal.Infrastructure.Persistence;
using EmsPortal.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Secrets such as the Microsoft sign-in keys live in a .env file: beside the project under Visual Studio /
// `dotnet run`, beside EmsPortal.Api.dll on a server. Its lines become environment variables before
// configuration is built, so `Authentication__Microsoft__ClientId=…` sets Authentication:Microsoft:ClientId.
var dotEnvPath = DotEnv.Load(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

// Structured logging to SQL Server, enriched with correlation ID, service, environment.
builder.Host.UseSerilog((context, _, loggerConfiguration) =>
    SerilogConfigurator.Configure(
        loggerConfiguration,
        context.Configuration,
        "EmsPortal.Api",
        context.HostingEnvironment.EnvironmentName));

// API host services.
builder.Services.AddControllers(options => options.Filters.Add<ValidationActionFilter>());
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(
    options => options.SuppressModelStateInvalidFilter = true);

// FluentValidation: auto-validate request DTOs into ModelState, which the
// ValidationActionFilter then turns into the ApiResponseFactory.ValidationError envelope.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddEmsPortalOpenApi();

// Authentication (JWT + API key), the AnyOf composite scheme, and RBAC policies.
builder.Services.AddEmsPortalAuthentication(builder.Configuration);

// Hangfire storage so the API can host the monitoring dashboard (jobs run in the Worker).
builder.Services.AddEmsPortalHangfireDashboard(builder.Configuration);

// Clean Architecture composition root.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// File uploads: one local-disk store for every module, plus the resolver that names the folder a
// record owns in the tree (media-uploads/{tenant}/{EntityType}/{recordKey}/{purpose}/).
builder.Services.AddScoped<EmsPortal.Application.Abstractions.Storage.IFileStorage, EmsPortal.Api.Storage.LocalFileStorage>();
builder.Services.AddScoped<EmsPortal.Api.Storage.IUploadRecordKeyResolver, EmsPortal.Api.Storage.UploadRecordKeyResolver>();

// Dashboard (WO-72): short-lived in-process cache (singleton) + scoped read-model aggregator.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EmsPortal.Api.Dashboard.IDashboardCacheService, EmsPortal.Api.Dashboard.DashboardCacheService>();
builder.Services.AddScoped<EmsPortal.Api.Dashboard.IDashboardQueryService, EmsPortal.Api.Dashboard.DashboardQueryService>();
// STATIC-APPROVAL-POLICY
builder.Services.AddScoped<EmsPortal.Api.Approval.IRemsApprovalPolicy, EmsPortal.Api.Approval.RemsApprovalPolicy>();

// CORS for the browser SPA (WEB/). Allowed origins come from configuration
// (Cors:AllowedOrigins); falls back to the local Quasar dev server ports.
const string SpaCorsPolicy = "SpaCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:9000", "http://localhost:9001", "https://thf.vskyapplications.com"];
builder.Services.AddCors(options =>
    options.AddPolicy(SpaCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

if (dotEnvPath is not null)
{
    app.Logger.LogInformation("Loaded environment overrides from {DotEnvPath}", dotEnvPath);
}

// The Integration API owns the application schema and applies EF Core migrations
// on startup. The Background Worker and MCP Server must not run migrations.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EmsPortalDbContext>();
    dbContext.Database.Migrate();

    // Seed a bootstrap Super Admin on first run so the platform is usable out of the box.
    await EmsPortal.Api.Startup.BootstrapSeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);
}

// OpenAPI spec (/openapi/v1.json) and the Scalar UI (/scalar/v1) are exposed only in
// Development and Staging.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("EMS Portal API"));
}

// Correlation ID is established first so every downstream log entry — and the 500 error body — carries it.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// CORS runs before authentication so preflight OPTIONS requests succeed.
app.UseCors(SpaCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Resolve the active tenant from the authenticated JWT and populate ITenantContext.
app.UseMiddleware<TenantResolutionMiddleware>();

// Propagate the active tenant into Hangfire job payloads at enqueue time.
var httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
GlobalJobFilters.Filters.Add(new TenantHangfireJobFilter(() =>
{
    var tenant = httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();
    return tenant?.IsResolved == true
        ? new TenantSnapshot(tenant.TenantId, tenant.TenantIdentifier)
        : (TenantSnapshot?)null;
}));

// Hangfire dashboard at /hangfire, gated by the TenantAdminOrAbove policy.
app.UseEmsPortalHangfireDashboard(builder.Configuration);

// Controllers are delivered in later phases; routing is wired here.
app.MapControllers();

app.Run();

/// <summary>Exposed so the integration test host (WebApplicationFactory&lt;Program&gt;) can bootstrap the app.</summary>
public partial class Program;
