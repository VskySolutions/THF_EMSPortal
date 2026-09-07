namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Canonical names of the <c>appsettings.json</c> configuration sections used across
/// the EmsPortal platform. Centralizing the section names here keeps binding code
/// (in the Infrastructure layer) and the configuration templates in sync.
/// </summary>
public static class ConfigurationSections
{
    /// <summary>Connection string key for the shared SQL Server database.</summary>
    public const string SqlServerConnection = "SqlServer";

    /// <summary>General application settings (e.g. the public SPA base URL used in emails).</summary>
    public const string App = "App";

    /// <summary>Hangfire background queue configuration.</summary>
    public const string Hangfire = "Hangfire";

    /// <summary>Serilog structured logging configuration.</summary>
    public const string Serilog = "Serilog";

    /// <summary>Authentication (JWT / API key) configuration.</summary>
    public const string Authentication = "Authentication";

    /// <summary>Registered machine-to-machine API keys.</summary>
    public const string ApiKeys = "ApiKeys";

    /// <summary>REMS provider email-event webhook (shared secret) configuration (WO-121).</summary>
    public const string RemsEmailWebhook = "Rems:EmailWebhook";

    // STATIC-APPROVAL-POLICY
    public const string RemsApprovalPolicy = "Rems:ApprovalPolicy";
}
