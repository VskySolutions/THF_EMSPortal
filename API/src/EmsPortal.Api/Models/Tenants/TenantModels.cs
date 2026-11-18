namespace EmsPortal.Api.Models.Tenants;

public sealed class CreateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Who the portal emails when an integration it runs for the tenant breaks. Up to twenty.</summary>
    public List<string>? DeveloperEmails { get; set; }
}

public sealed class UpdateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string? TimeZoneId { get; set; }

    /// <summary>Null leaves the list as it is; an empty list clears it.</summary>
    public List<string>? DeveloperEmails { get; set; }
}

public sealed class UpdateTenantStatusRequest
{
    public bool IsActive { get; set; }
}

public sealed record TenantResponse(Guid TenantId, string Identifier, string Status);

public sealed record TenantSummary(
    Guid TenantId,
    string Name,
    string Identifier,
    string Status,
    string TimeZoneId,
    IReadOnlyList<string> DeveloperEmails,
    string? CreatedBy,
    string? UpdatedBy,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

/// <summary>One tenant, as its detail page reads it.</summary>
public sealed record TenantDetail(
    Guid TenantId,
    string Name,
    string Identifier,
    string Status,
    string TimeZoneId,
    IReadOnlyList<string> DeveloperEmails,
    RecordAudit Audit);
