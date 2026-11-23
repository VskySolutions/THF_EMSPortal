using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// One CSV file of users somebody loaded: who and when are the audit fields, the outcome of each line
/// is in <see cref="Rows"/>. Written once, never edited. The temporary password is not kept.
/// </summary>
public class UserImport : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    /// <summary>Existing users the file changed. A matched line with nothing to change is not counted.</summary>
    public int UpdatedCount { get; set; }

    public int FailedCount { get; set; }

    public ICollection<UserImportRow> Rows { get; set; } = new List<UserImportRow>();
}

/// <summary>One line of an imported file, as typed, with what became of it.</summary>
public class UserImportRow : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserImportId { get; set; }

    /// <summary>The line's position among the file's data lines, from 1.</summary>
    public int RowNumber { get; set; }

    public string? MaconomyNumber { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Department { get; set; }

    /// <summary>The Role cell as typed.</summary>
    public string? Roles { get; set; }

    /// <summary>The Status cell as typed: Active, Inactive or blank.</summary>
    public string? AccountStatus { get; set; }

    public UserImportRowStatus Status { get; set; }

    public string? Error { get; set; }

    /// <summary>The account this line created; null when it failed.</summary>
    public Guid? CreatedUserId { get; set; }
}
