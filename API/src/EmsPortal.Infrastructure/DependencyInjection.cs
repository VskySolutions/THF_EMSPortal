using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Infrastructure.Auditing;
using EmsPortal.Infrastructure.Tenancy;
using EmsPortal.Infrastructure.Persistence;
using EmsPortal.Infrastructure.Persistence.Repositories;
using EmsPortal.Infrastructure.Security;
using EmsPortal.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EmsPortal.Infrastructure;

/// <summary>
/// Composition-root entry point for the Infrastructure layer. Host projects
/// (Api, Workers, McpServer) call <see cref="AddInfrastructure"/> to register
/// data access and infrastructure-bound options.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the Infrastructure layer services and binds shared configuration
    /// sections into strongly-typed options.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HangfireOptions>(configuration.GetSection(ConfigurationSections.Hangfire));
        services.Configure<AuthenticationOptions>(configuration.GetSection(ConfigurationSections.Authentication));
        services.Configure<ApiKeysOptions>(configuration.GetSection(ConfigurationSections.ApiKeys));
        services.Configure<AppOptions>(configuration.GetSection(ConfigurationSections.App));
        services.Configure<RemsWebhookOptions>(configuration.GetSection(ConfigurationSections.RemsEmailWebhook));
        // STATIC-APPROVAL-POLICY
        services.Configure<RemsApprovalPolicyOptions>(configuration.GetSection(ConfigurationSections.RemsApprovalPolicy));
        services.Configure<MicrosoftSsoOptions>(configuration.GetSection(ConfigurationSections.MicrosoftSso));

        services.AddSecurity();
        services.AddEmail();

        // Field-level change-history capture (Universal Features — Modified Log).
        services.AddSingleton<Persistence.ModifiedLog.IFieldValueFormatter, Persistence.ModifiedLog.FieldValueFormatter>();
        services.AddScoped<Persistence.ModifiedLog.FieldChangeInterceptor>();

        services.AddDbContext<EmsPortalDbContext>((sp, options) =>
            options.UseSqlServer(
                    configuration.GetConnectionString(ConfigurationSections.SqlServerConnection),
                    sql => sql.MigrationsAssembly(typeof(EmsPortalDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<Persistence.ModifiedLog.FieldChangeInterceptor>()));

        services.AddPersistence();

        // Persist the Data Protection key ring to SQL Server so every API/Worker instance
        // shares the same keys (Multi-Tenancy ADR-002). Application name isolates the ring.
        services.AddDataProtection()
            .PersistKeysToDbContext<EmsPortalDbContext>()
            .SetApplicationName("EmsPortal");

        return services;
    }

    /// <summary>
    /// Registers the unit of work and EF Core repositories. All share the scoped
    /// <see cref="EmsPortalDbContext"/> so writes commit in a single transaction.
    /// </summary>
    private static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditTrailRepository, AuditTrailRepository>();
        services.AddScoped<IAuditTrailService, AuditTrailService>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IUserGroupRepository, UserGroupRepository>();
        services.AddScoped<IUserDepartmentRepository, UserDepartmentRepository>();
        services.AddScoped<IAddressRepository, AddressRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IPermissionGroupRepository, PermissionGroupRepository>();
        services.AddScoped<IDashboardLayoutRepository, DashboardLayoutRepository>();
        services.AddScoped<ISmtpAccountRepository, SmtpAccountRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IOptionSetRepository, OptionSetRepository>();

        // Universal Features (Phase 14) repositories.
        services.AddScoped<IActivityEventRepository, ActivityEventRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IConversationMessageRepository, ConversationMessageRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IReminderRepository, ReminderRepository>();
        services.AddScoped<IPinRepository, PinRepository>();
        services.AddScoped<IColourCodeRepository, ColourCodeRepository>();
        services.AddScoped<IChecklistRepository, ChecklistRepository>();
        services.AddScoped<IStickyNoteRepository, StickyNoteRepository>();
        services.AddScoped<IDeletedRecordsRepository, DeletedRecordsRepository>();
        services.AddScoped<IRetentionConfigRepository, RetentionConfigRepository>();
        services.AddScoped<IModifiedLogRepository, ModifiedLogRepository>();

        // Universal Features recurring jobs (Hangfire resolves them from DI).
        services.AddScoped<Jobs.ReminderDispatchJob>();
        services.AddScoped<Jobs.StickyNoteExpiryJob>();

        // REMS (WO-110) repositories and per-tenant request-number generation.
        services.AddScoped<IRemsRepository, RemsRepository>();
        services.AddScoped<IRemsFormRepository, RemsFormRepository>();
        services.AddScoped<IRemsClientRepository, RemsClientRepository>();
        services.AddScoped<IRemsEngagementRepository, RemsEngagementRepository>();
        services.AddScoped<IRemsDelegationRepository, RemsDelegationRepository>();
        services.AddScoped<IRemsApprovalRepository, RemsApprovalRepository>();
        services.AddScoped<IRemsSettingsRepository, RemsSettingsRepository>();

        // Bubbles a child write's timestamp up to the aggregate root it belongs to (see AggregateRootTouch).
        services.AddScoped<Application.Abstractions.UniversalFeatures.IAggregateRootTouch, Persistence.AggregateRootTouch>();
        services.AddScoped<IRemsNumberGenerator, Persistence.RemsNumberGenerator>();

        return services;
    }

    /// <summary>
    /// Registers the email infrastructure: the low-level SMTP sender used by the SMTP account
    /// management service for test sends (and by notification flows for production sends).
    /// </summary>
    private static IServiceCollection AddEmail(this IServiceCollection services)
    {
        // Factory registration so the sender uses its built-in 15-second default timeout (the optional
        // constructor parameter is not resolvable by the DI container).
        services.AddScoped<Application.Abstractions.Email.ISmtpEmailSender>(sp =>
            new Email.SmtpEmailSender(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Email.SmtpEmailSender>>()));
        services.AddScoped<Application.Abstractions.Email.IEmailNotificationService, Email.EmailNotificationService>();

        // Transactional emails are queued and delivered on a Hangfire worker so requests never block on SMTP.
        services.AddScoped<Application.Abstractions.Email.IEmailDispatcher, Jobs.EmailDispatcher>();
        services.AddScoped<Jobs.EmailSendJob>();
        return services;
    }

    /// <summary>
    /// Registers cross-cutting security services shared by the API and Worker hosts:
    /// the scoped correlation/tenant context, the token-version store, the API key validator,
    /// and the Data Protection-backed credential encryption used for SMTP passwords.
    /// </summary>
    private static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITokenVersionValidator, DbTokenVersionValidator>();
        services.AddSingleton<IApiKeyValidator, Pbkdf2ApiKeyValidator>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISigningKeyProvider, RsaSigningKeyProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        // "Login with Microsoft": one client for the process, so Entra's signing keys are fetched once and cached.
        services.AddHttpClient(MicrosoftIdentityClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IMicrosoftIdentityClient, MicrosoftIdentityClient>();
        // Symmetric encryption for stored SMTP account passwords (Data Protection key ring).
        services.AddSingleton<ICredentialEncryptionService, DataProtectionCredentialEncryptionService>();
        // Default actor identity is the system; the API replaces this with an
        // HttpContext-based accessor that resolves the authenticated user.
        services.TryAddScoped<IActorAccessor, SystemActorAccessor>();
        return services;
    }
}
