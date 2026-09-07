namespace EmsPortal.Api.Models.Users;

public sealed class CreateUserRequest
{
    /// <summary>The existing Person to promote to a login account.</summary>
    public Guid PersonId { get; set; }
    /// <summary>Login email/username.</summary>
    public string? Email { get; set; }
    /// <summary>Optional phone; when supplied it is written back to the person's mobile number.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Optional dial code for <see cref="PhoneNumber"/>, written back to the person.</summary>
    public string? CountryCode { get; set; }
    /// <summary>Target tenant.</summary>
    public Guid? TenantId { get; set; }
    /// <summary>The RBAC roles to assign in the tenant (multi-role).</summary>
    public List<Guid> RoleIds { get; set; } = new();
    /// <summary>Legacy single RBAC role.</summary>
    public Guid? RoleId { get; set; }
    /// <summary>Legacy fixed-tier role name.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>
    /// When true, email the new user an invitation with their temporary password (via the tenant's
    /// active SMTP account).
    /// </summary>
    public bool SendInvitation { get; set; }
}

public sealed class UpdateUserRequest
{
    /// <summary>The generational particle on the person's name (Jr., III, …).</summary>
    public string? Suffix { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}

public sealed class UpdateProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}

/// <summary>Reconciles the full set of roles a user holds in a tenant (multi-role).</summary>
public sealed class AssignTenantRoleRequest
{
    public Guid TenantId { get; set; }
    /// <summary>The RBAC roles the user should hold in the tenant.</summary>
    public List<Guid> RoleIds { get; set; } = new();
    /// <summary>Legacy single RBAC role.</summary>
    public Guid? RoleId { get; set; }
    /// <summary>Legacy fixed-tier role name.</summary>
    public string? Role { get; set; }
}

/// <summary>A user's roles within a single tenant (grouped — multi-role).</summary>
public sealed record TenantAssignmentDto(Guid TenantId, IReadOnlyList<TenantAssignmentRoleDto> Roles);

/// <summary>One role held in a tenant: its RBAC id/name plus the legacy fixed-tier shadow.</summary>
public sealed record TenantAssignmentRoleDto(Guid RoleId, string? RoleName, string Role);

// ---- User groups ----

/// <summary>Lightweight reference to a user group (used on user summaries/details).</summary>
public sealed record UserGroupDto(Guid Id, string Name);

/// <summary>A user group with its member count + provenance (for the groups picker / management list).</summary>
public sealed record UserGroupResponse(
    Guid Id, string Name, string? Description, int MemberCount,
    string? CreatedBy, DateTime CreatedOnUtc, string? UpdatedBy, DateTime UpdatedOnUtc);

public sealed class CreateUserGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Replaces a user's group memberships with the given set.</summary>
public sealed class AssignUserGroupsRequest
{
    public List<Guid> GroupIds { get; set; } = new();
}

// ---- Departments ----

/// <summary>Sets (or clears) the user's department within the caller's active tenant.</summary>
public sealed class SetUserDepartmentRequest
{
    /// <summary>Department code (option-set <c>REMS.Department</c>), or null/empty to unassign the user.</summary>
    public string? Department { get; set; }

    /// <summary>True to make this user the department's head.</summary>
    public bool IsHead { get; set; }
}

/// <summary>One selectable department for the picker.</summary>
public sealed record DepartmentOptionDto(string Value, string Label);

/// <summary>The current head of a department in the active tenant.</summary>
public sealed record DepartmentHeadDto(string Department, Guid UserId, string FullName);

/// <summary>
/// Picker data for the user's department section: the tenant's departments and the head of each —
/// what the UI needs to name the incumbent before a headship is taken over.
/// </summary>
public sealed record DepartmentOptionsResponse(
    IReadOnlyList<DepartmentOptionDto> Departments,
    IReadOnlyList<DepartmentHeadDto> Heads);

/// <summary>
/// The saved placement, plus the name of the head this change displaced (null when nobody was demoted)
/// so the caller can report the handover.
/// </summary>
public sealed record SetUserDepartmentResponse(string? Department, bool IsHead, string? DemotedHeadName);

/// <summary>A member (user) of a group, with who added them and when — for the group's members list.</summary>
public sealed record UserGroupMemberResponse(
    Guid UserId, string FullName, string? Email, bool IsActive, string? AddedBy, DateTime AddedOnUtc);

/// <summary>Adds the given users to a group (members already present are ignored).</summary>
public sealed class AddGroupMembersRequest
{
    public List<Guid> UserIds { get; set; } = new();
}

public sealed record CreateUserResponse(Guid UserId, string TemporaryPassword, bool InvitationEmailSent);

public sealed record ResetPasswordResponse(Guid UserId, string TemporaryPassword, bool EmailSent);

public sealed record UserSummary(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string? TenantName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserGroupDto> Groups,
    bool IsActive,
    // The department held in the caller's active tenant — already resolved to its option-set label, and null
    // when the user is unplaced (or the caller has no active tenant).
    string? Department,
    bool IsDepartmentHead,
    string? CreatedBy,
    string? UpdatedBy,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record UserDetail(
    Guid UserId,
    Guid? PersonId,
    string Email,
    /// <summary>The generational particle on their name, from their Person record; null when they have none.</summary>
    string? Suffix,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string DisplayName,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<TenantAssignmentDto> Assignments,
    IReadOnlyList<UserGroupDto> Groups,
    // The department held in the active tenant (null when unassigned), and whether the user heads it —
    // a head is also the department's REMS director.
    string? Department,
    bool IsDepartmentHead,
    // The profile picture from the person's own record, or null — in which case the UI shows initials.
    string? ProfileMediaUrl,
    RecordAudit Audit);
