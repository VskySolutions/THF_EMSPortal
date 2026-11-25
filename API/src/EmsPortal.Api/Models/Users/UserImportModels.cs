namespace EmsPortal.Api.Models.Users;

/// <summary>A CSV file of users, sent as text, with the one temporary password every new account gets.</summary>
public sealed class UserImportRequest
{
    public string? FileName { get; set; }

    /// <summary>The file's text.</summary>
    public string? Content { get; set; }

    /// <summary>Needed to import a file that creates users; not to check, and not for updates.</summary>
    public string? TemporaryPassword { get; set; }

    /// <summary>A line whose email is already a user of this tenant updates that user instead of failing.</summary>
    public bool UpdateExisting { get; set; }
}

/// <summary>
/// One line of the file. Status is Ready / Update / Unchanged / Error on a check, and Created / Updated /
/// Unchanged / Failed on an import. AccountStatus is the Status cell as typed; Note says what an update changes.
/// </summary>
public sealed record UserImportRowResult(
    int RowNumber,
    string? MaconomyNumber,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Department,
    string? Roles,
    string Status,
    string? Error,
    Guid? UserId,
    string? AccountStatus = null,
    string? Note = null);

/// <summary>What the Role and Department cells may hold, for the template's second sheet.</summary>
public sealed record UserImportOptions(IReadOnlyList<string> Roles, IReadOnlyList<string> Departments);

/// <summary>ReadyCount is every line an import would act on: the ones it creates and the ones it updates.</summary>
public sealed record UserImportCheckResponse(
    int TotalCount, int ReadyCount, int ErrorCount, IReadOnlyList<UserImportRowResult> Rows,
    int CreateCount = 0, int UpdateCount = 0, int UnchangedCount = 0);

public sealed record UserImportSummary(
    Guid Id, string FileName, int TotalCount, int CreatedCount, int FailedCount, string? ImportedBy, DateTime ImportedOnUtc,
    int UpdatedCount = 0);

public sealed record UserImportDetail(UserImportSummary Import, IReadOnlyList<UserImportRowResult> Rows);
