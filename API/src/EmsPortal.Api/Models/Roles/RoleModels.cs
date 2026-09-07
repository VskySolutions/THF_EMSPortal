namespace EmsPortal.Api.Models.Roles;

public sealed class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public sealed class UpdateRoleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }
}

public sealed class AssignRoleToTenantRequest
{
    public Guid RoleId { get; set; }
}

/// <summary>A single role.</summary>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    Guid? TenantId,
    string? TenantName,
    bool CanManage,
    IReadOnlyList<string> Permissions,
    RecordAudit Audit);

/// <summary>A role as the list shows it.</summary>
public sealed record RoleSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    Guid? TenantId,
    string? TenantName,
    bool CanManage,
    int PermissionCount,
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

// ---- Role membership (who holds a role in a tenant) ----

/// <summary>Somebody holding the role in this tenant.</summary>
public sealed record RoleMemberResponse(
    Guid UserId,
    string DisplayName,
    string? Email,
    bool IsActive,
    IReadOnlyList<string> OtherRoles,
    bool IsOnlyRole);

/// <summary>A user the role could be given to: someone already in the tenant who does not hold it yet.</summary>
public sealed record RoleMemberCandidateResponse(Guid UserId, string DisplayName, string? Email);

public sealed class AddRoleMembersRequest
{
    public List<Guid> UserIds { get; set; } = new();
}
