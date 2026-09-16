namespace EmsPortal.Shared.Contracts;

/// <summary>Platform-wide stable error codes used in <see cref="ApiError.Code"/>.</summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string DuplicateIdentifier = "DUPLICATE_IDENTIFIER";
    public const string TenantInactive = "TENANT_INACTIVE";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string TenantArchived = "TENANT_ARCHIVED";

    // Maconomy integration
    /// <summary>The tenant has no Maconomy connection, or it is switched off.</summary>
    public const string MaconomyNotConfigured = "MACONOMY_NOT_CONFIGURED";
    /// <summary>Maconomy refused the stored credentials, or a token it had just issued.</summary>
    public const string MaconomyAuthFailed = "MACONOMY_AUTH_FAILED";
    /// <summary>Maconomy could not be reached, timed out, or answered an error.</summary>
    public const string MaconomyUnavailable = "MACONOMY_UNAVAILABLE";

    // Permission Groups
    public const string DuplicateGroupName = "DUPLICATE_GROUP_NAME";
    public const string PermissionCeilingExceeded = "PERMISSION_CEILING_EXCEEDED";
    public const string GroupInUse = "GROUP_IN_USE";

    // Permission Group capacity limits (WO-119)
    /// <summary>A new capacity limit is below the group's current usage (edit rejected, AC-PG-003.4).</summary>
    public const string CapacityBelowUsage = "CAPACITY_BELOW_USAGE";
    /// <summary>An action would push a group's usage past its capacity limit (add rejected, AC-PG-013.2).</summary>
    public const string CapacityLimitReached = "CAPACITY_LIMIT_REACHED";

    public const string InternalError = "INTERNAL_ERROR";
}
