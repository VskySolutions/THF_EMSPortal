namespace EmsPortal.Api.Models.Tenants;

public sealed class CreateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
}

public sealed class UpdateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string? TimeZoneId { get; set; }
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
    RecordAudit Audit);
