using Microsoft.Extensions.DependencyInjection;

namespace EmsPortal.Application;

/// <summary>
/// Composition-root entry point for the Application layer. Host projects
/// (Api, Workers, McpServer) call <see cref="AddApplication"/> to register
/// the access, email, option-set, and universal-feature services.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Permission Groups: effective-permission cache computation.
        services.AddScoped<Abstractions.Security.IPermissionGroupEffectivePermissionService, Security.PermissionGroupEffectivePermissionService>();

        // SMTP Email Accounts: account management business logic.
        services.AddScoped<Abstractions.Email.ISmtpAccountService, Email.SmtpAccountService>();

        // Maconomy: the tenant's connection, the token that keeps it alive, and the customer lookup.
        services.AddScoped<Abstractions.Integrations.Maconomy.IMaconomyConnectionService, Integrations.Maconomy.MaconomyConnectionService>();
        services.AddScoped<Abstractions.Integrations.Maconomy.IMaconomySessionManager, Integrations.Maconomy.MaconomySessionManager>();
        services.AddScoped<Abstractions.Integrations.Maconomy.IMaconomyCustomerService, Integrations.Maconomy.MaconomyCustomerService>();

        // Email templates: management + rendering.
        services.AddScoped<Abstractions.Email.IEmailTemplateService, Email.EmailTemplateService>();

        // REMS external emails (WO-124): typed background dispatch for WO-112/113.
        services.AddScoped<Abstractions.Email.IRemsEmailNotifier, Email.RemsEmailNotifier>();

        // Swallowed delivery failures surface as a Failed event on the REMS form's email log.
        services.AddScoped<Abstractions.Email.IEmailDeliveryFailureSink, Email.RemsEmailDeliveryFailureSink>();

        // Option Sets: tenant-configurable input value lists.
        services.AddScoped<Abstractions.OptionSets.IOptionSetService, OptionSets.OptionSetService>();
        // Turns the option-item ids stored on a row back into the codes the application branches on, and
        // back again. Scoped, because it resolves against the CALLER's tenant; the cache behind it is the
        // shared IMemoryCache, so the reads are shared across requests.
        services.AddScoped<Abstractions.OptionSets.IOptionCodeResolver, OptionSets.OptionCodeResolver>();

        // Universal Features (Phase 14): cross-cutting activity writer + notification dispatcher.
        services.AddScoped<Abstractions.UniversalFeatures.IActivityEventWriter, UniversalFeatures.ActivityEventWriter>();
        services.AddScoped<Abstractions.UniversalFeatures.INotificationDispatcher, UniversalFeatures.NotificationDispatcher>();

        return services;
    }
}
