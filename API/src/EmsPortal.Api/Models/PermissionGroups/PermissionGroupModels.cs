namespace EmsPortal.Api.Models.PermissionGroups;

// ---- Requests ----

/// <summary>Create a Permission Group.</summary>
public sealed class CreateGroupRequest
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> PermissionKeys { get; set; } = new();

    /// <summary>Optional capacity limit (WO-119); null/omitted = unlimited.</summary>
    public int? CapacityLimit { get; set; }
}

public sealed class UpdateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> PermissionKeys { get; set; } = new();

    /// <summary>Optional capacity limit (WO-119); null = unlimited.</summary>
    public int? CapacityLimit { get; set; }
}

public sealed class SetGroupStatusRequest
{
    public bool IsActive { get; set; }
}

/// <summary>Super Admin: create/update a group template.</summary>
public sealed class SaveTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> PermissionKeys { get; set; } = new();
}

/// <summary>Assign one or more Permission Groups to a Role.</summary>
public sealed class AssignGroupsRequest
{
    public List<Guid> GroupIds { get; set; } = new();
}

// ---- Responses ----

public sealed record PermissionGroupSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    int PermissionCount,
    int RolesUsingCount,
    bool IsActive,
    Guid TenantId,
    string? TenantName,
    // Capacity limits (WO-119): null limit = unlimited; CurrentUsage = distinct active members; IsFull = at/over limit.
    int? CapacityLimit,
    int CurrentUsage,
    bool IsFull,
    // The audit trail every list offers as hidden-by-default columns; *By resolved by the controller.
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

public sealed record RoleUsingGroupResponse(Guid RoleId, string RoleName);

public sealed record PermissionGroupAuditEntryResponse(
    string Action,
    /// <summary>Who did it, by name — the stored user id resolved through AuditActorNames.</summary>
    string? PerformedBy,
    DateTime PerformedOnUtc,
    string? Details);

public sealed record PermissionGroupDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    Guid TenantId,
    string? TenantName,
    IReadOnlyList<string> PermissionKeys,
    IReadOnlyList<RoleUsingGroupResponse> RolesUsing,
    IReadOnlyList<PermissionGroupAuditEntryResponse> AuditTrail,
    bool CanDelete,
    // Not the AuditTrail above: that is every action ever taken on the group, this is the record itself —
    // who made it and who last touched it, the block every detail page ends with.
    RecordAudit Audit,
    // Capacity limits (WO-119): null limit = unlimited; CurrentUsage = distinct active members; IsFull = at/over limit.
    int? CapacityLimit,
    int CurrentUsage,
    bool IsFull);

public sealed record PermissionGroupTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> PermissionKeys,
    bool IsSeeded);

public sealed record EffectivePermissionSourceResponse(
    Guid GroupId,
    string GroupName,
    bool IsActive,
    IReadOnlyList<string> Keys);

public sealed record RolePermissionPreviewResponse(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<EffectivePermissionSourceResponse> Sources);

public sealed record RoleGroupsResponse(
    Guid RoleId,
    IReadOnlyList<PermissionGroupSummaryResponse> Groups,
    IReadOnlyList<string> EffectivePermissions);

/// <summary>Result of removing a group from a role; surfaces permissions that lost their only source.</summary>
public sealed record RemoveGroupFromRoleResponse(
    IReadOnlyList<string> EffectivePermissions,
    IReadOnlyList<string> LostPermissions);
