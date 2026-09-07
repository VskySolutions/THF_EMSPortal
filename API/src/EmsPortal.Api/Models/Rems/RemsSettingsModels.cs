namespace EmsPortal.Api.Models.Rems;

// ---------------------------------------------------------------------------------------------------
// WO-114 — per-tenant REMS settings.

/// <summary>The tenant's REMS settings (WO-114): the department-director map.</summary>
public sealed record RemsSettingsView(IReadOnlyList<RemsDepartmentDirectorView> DepartmentDirectors);

/// <summary>One department-to-director mapping row.</summary>
public sealed record RemsDepartmentDirectorView(string Department, RemsUserRef Director);

/// <summary>Update the tenant's REMS settings.</summary>
public sealed class UpdateRemsSettingsRequest
{
    /// <summary>
    /// The full department-to-director map; reconciled against the stored rows (upsert + remove
    /// absent).
    /// </summary>
    public List<RemsDepartmentDirectorInput> DepartmentDirectors { get; set; } = new();
}

/// <summary>A single department-to-director mapping input.</summary>
public sealed class RemsDepartmentDirectorInput
{
    public string Department { get; set; } = string.Empty;
    public Guid DirectorUserId { get; set; }
}
