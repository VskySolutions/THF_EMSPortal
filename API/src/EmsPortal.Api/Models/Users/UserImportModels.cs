namespace EmsPortal.Api.Models.Users;

/// <summary>A CSV file of users, sent as text, with the one temporary password every new account gets.</summary>
public sealed class UserImportRequest
{
    public string? FileName { get; set; }

    /// <summary>The file's text.</summary>
    public string? Content { get; set; }

    /// <summary>Needed to import, not to check.</summary>
    public string? TemporaryPassword { get; set; }
}

/// <summary>One line of the file. Status is Ready / Error on a check, Created / Failed on an import.</summary>
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
    Guid? UserId);

/// <summary>What the Role and Department cells may hold, for the template's second sheet.</summary>
public sealed record UserImportOptions(IReadOnlyList<string> Roles, IReadOnlyList<string> Departments);

public sealed record UserImportCheckResponse(
    int TotalCount, int ReadyCount, int ErrorCount, IReadOnlyList<UserImportRowResult> Rows);

public sealed record UserImportSummary(
    Guid Id, string FileName, int TotalCount, int CreatedCount, int FailedCount, string? ImportedBy, DateTime ImportedOnUtc);

public sealed record UserImportDetail(UserImportSummary Import, IReadOnlyList<UserImportRowResult> Rows);
