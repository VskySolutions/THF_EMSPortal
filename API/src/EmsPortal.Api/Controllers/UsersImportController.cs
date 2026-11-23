using System.Text.RegularExpressions;
using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Users;
using EmsPortal.Api.Security;
using EmsPortal.Api.Users;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Users loaded in bulk from a CSV file, into the caller's active tenant. A check writes nothing; an
/// import creates the lines that pass, reports the rest and logs both. No email is sent: every new
/// account gets the one temporary password given with the file, to be changed at first sign-in.
/// When asked to, a line whose email is already a user of the tenant updates that user: the email is
/// the key and is never changed, a blank cell leaves its value alone, and the password is not touched.
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Users")]
public sealed partial class UsersImportController : ControllerBase
{
    private const int MaxRows = 500;
    private const int MaxContentLength = 1_000_000;
    private const string DepartmentOptionSetKey = "REMS.Department";

    private readonly IUserRepository _users;
    private readonly IPersonRepository _persons;
    private readonly IRoleRepository _roles;
    private readonly IPermissionGroupRepository _permissionGroups;
    private readonly IOptionSetRepository _optionSets;
    private readonly IUserDepartmentRepository _departments;
    private readonly IUserImportRepository _imports;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UsersImportController(
        IUserRepository users,
        IPersonRepository persons,
        IRoleRepository roles,
        IPermissionGroupRepository permissionGroups,
        IOptionSetRepository optionSets,
        IUserDepartmentRepository departments,
        IUserImportRepository imports,
        IPasswordHasher passwordHasher,
        IAuditTrailService audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _persons = persons;
        _roles = roles;
        _permissionGroups = permissionGroups;
        _optionSets = optionSets;
        _departments = departments;
        _imports = imports;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    /// <summary>The role and department names a file may use, as the import itself would accept them.</summary>
    [HttpGet("/api/admin/users/import/options")]
    [RequirePermission(Permissions.UsersWrite)]
    [ProducesResponseType<ApiResponse<UserImportOptions>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var roles = (await _roles.ListVisibleToTenantAsync(tenantId, cancellationToken))
            .Select(r => r.Name)
            .Where(n => !string.Equals(n, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var departments = (await ListDepartmentsAsync(tenantId, cancellationToken))
            .Select(d => d.Label)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(ApiResponseFactory.Success(new UserImportOptions(roles, departments), "Options retrieved."));
    }

    private async Task<List<OptionSetItem>> ListDepartmentsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var set = await _optionSets.GetEffectiveSetAsync(tenantId, DepartmentOptionSetKey, cancellationToken);
        return (set?.Items ?? Enumerable.Empty<OptionSetItem>()).Where(d => !d.Deleted && d.IsActive).ToList();
    }

    /// <summary>Validates the file and says what an import would do. Writes nothing.</summary>
    [HttpPost("/api/admin/users/import/check")]
    [RequirePermission(Permissions.UsersWrite)]
    [ProducesResponseType<ApiResponse<UserImportCheckResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Check([FromBody] UserImportRequest request, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var (lines, fileError) = await ReadAndValidateAsync(tenantId, request.Content, request.UpdateExisting, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "The file cannot be imported.", fileError));
        }

        static string CheckStatus(Line l) => l.Error is not null ? "Error"
            : !l.IsUpdate ? "Ready"
            : l.Changes.Count > 0 ? "Update" : "Unchanged";

        var rows = lines.Select(l => l.ToResult(CheckStatus(l), l.ExistingUserId)).ToList();
        var creates = lines.Count(l => l.Error is null && !l.IsUpdate);
        var updates = lines.Count(l => l.Error is null && l.IsUpdate && l.Changes.Count > 0);
        var unchanged = lines.Count(l => l.Error is null && l.IsUpdate && l.Changes.Count == 0);
        return Ok(ApiResponseFactory.Success(
            new UserImportCheckResponse(lines.Count, creates + updates, lines.Count(l => l.Error is not null), rows,
                creates, updates, unchanged), "File checked."));
    }

    /// <summary>Creates a user for every line that passes, and logs the whole file.</summary>
    [HttpPost("/api/admin/users/import")]
    [RequirePermission(Permissions.UsersWrite)]
    [ProducesResponseType<ApiResponse<UserImportDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import([FromBody] UserImportRequest request, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var (lines, fileError) = await ReadAndValidateAsync(tenantId, request.Content, request.UpdateExisting, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "The file cannot be imported.", fileError));
        }

        // The rule a user's own new password has to meet. Owed only when the file creates somebody.
        var password = request.TemporaryPassword ?? string.Empty;
        if (lines.Any(l => l.Error is null && !l.IsUpdate)
            && (password.Length < 8 || !password.Any(char.IsAsciiLetterUpper) || !password.Any(char.IsAsciiDigit)))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.",
                "The file has new users: the temporary password needs at least 8 characters, an uppercase letter and a digit."));
        }

        // Hashed once: it is the same password for everyone, and 500 hashes would take the best part of a minute.
        var (hash, salt) = _passwordHasher.Hash(password);
        var importId = Guid.NewGuid();
        var createdUserIds = new Dictionary<int, Guid>();
        var updatedRows = new HashSet<int>();

        foreach (var line in lines.Where(l => l.Error is null))
        {
            try
            {
                if (!line.IsUpdate)
                {
                    createdUserIds[line.RowNumber] = await CreateUserAsync(tenantId, importId, line, hash, salt, cancellationToken);
                }
                else if (line.Changes.Count > 0)
                {
                    await UpdateUserAsync(tenantId, importId, line, cancellationToken);
                    updatedRows.Add(line.RowNumber);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Each line is its own save, so the ones before it stand.
                _unitOfWork.DiscardChanges();
                line.Error = line.IsUpdate
                    ? "Could not be saved. The user may have been changed while the file was importing."
                    : "Could not be saved. The email may have been taken while the file was importing.";
            }
        }

        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "users.csv" : request.FileName.Trim();
        var import = new UserImport
        {
            Id = importId,
            TenantId = tenantId,
            FileName = Cut(fileName, 260)!,
            TotalCount = lines.Count,
            CreatedCount = createdUserIds.Count,
            UpdatedCount = updatedRows.Count,
            FailedCount = lines.Count(l => l.Error is not null),
            Rows = lines.Select(l => new UserImportRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserImportId = importId,
                RowNumber = l.RowNumber,
                MaconomyNumber = Cut(l.MaconomyNumber, 100),
                FirstName = Cut(l.FirstName, 200),
                LastName = Cut(l.LastName, 200),
                Email = Cut(l.Email, 320),
                Phone = Cut(l.Phone, 100),
                Department = Cut(l.Department, 100),
                Roles = Cut(l.Roles, 1000),
                AccountStatus = Cut(l.AccountStatus, 20),
                Status = l.Error is not null ? UserImportRowStatus.Failed
                    : !l.IsUpdate ? UserImportRowStatus.Created
                    : updatedRows.Contains(l.RowNumber) ? UserImportRowStatus.Updated : UserImportRowStatus.Unchanged,
                Error = Cut(l.Error, 1000),
                // The user the line became, or the one it matched.
                CreatedUserId = l.Error is not null ? null
                    : createdUserIds.TryGetValue(l.RowNumber, out var userId) ? userId : l.ExistingUserId,
            }).ToList(),
        };
        await _imports.AddAsync(import, cancellationToken);
        await _audit.AddAsync(nameof(UserImport), importId.ToString(), "Imported",
            details: $"file={import.FileName}; created={import.CreatedCount}; updated={import.UpdatedCount}; failed={import.FailedCount}",
            cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var names = await _users.GetFullNamesAsync(new[] { import.CreatedById ?? Guid.Empty }, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToDetail(import, names), "Import finished."));
    }

    /// <summary>The tenant's imports, newest first.</summary>
    [HttpGet("/api/admin/users/imports")]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType<ApiResponse<IEnumerable<UserImportSummary>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var (items, total) = await _imports.ListAsync(tenantId, Math.Max(page, 1), Math.Clamp(limit, 1, 100), cancellationToken);
        var names = await _users.GetFullNamesAsync(
            items.Where(i => i.CreatedById.HasValue).Select(i => i.CreatedById!.Value), cancellationToken);
        return Ok(ApiResponseFactory.Paginated(items.Select(i => ToSummary(i, names)), "Imports retrieved.", page, limit, total));
    }

    /// <summary>One import with every line of its file.</summary>
    [HttpGet("/api/admin/users/imports/{id:guid}")]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType<ApiResponse<UserImportDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var import = User.GetActiveTenantId() is { } tenantId ? await _imports.GetAsync(tenantId, id, cancellationToken) : null;
        if (import is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Import not found."));
        }

        var names = await _users.GetFullNamesAsync(new[] { import.CreatedById ?? Guid.Empty }, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToDetail(import, names), "Import retrieved."));
    }

    // ---- Reading and validating ----

    /// <summary>One data line of the file, with what it resolved to and the first reasons it cannot go in.</summary>
    private sealed class Line
    {
        public int RowNumber { get; init; }
        public string? MaconomyNumber { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string? Department { get; init; }
        public string? Roles { get; init; }
        public string? AccountStatus { get; init; }

        public List<Role> ResolvedRoles { get; } = new();
        public string? DepartmentCode { get; set; }
        public string? Error { get; set; }

        /// <summary>The Status cell said Inactive.</summary>
        public bool Inactive { get; set; }

        /// <summary>The user this line's email already belongs to, when the file may update them.</summary>
        public Guid? ExistingUserId { get; set; }
        public bool IsUpdate => ExistingUserId.HasValue;

        /// <summary>What an update would change, by name. Empty when the user already matches the line.</summary>
        public List<string> Changes { get; } = new();

        /// <summary>The roles that take a new place in the capped groups they compose.</summary>
        public List<Role> CapacityRoles { get; set; } = new();

        public UserImportRowResult ToResult(string status, Guid? userId)
            => new(RowNumber, MaconomyNumber, FirstName, LastName, Email, Phone, Department, Roles, status, Error, userId,
                AccountStatus,
                Error is not null || !IsUpdate ? null
                    : Changes.Count > 0 ? "Changes: " + string.Join(", ", Changes) + "." : "Nothing to change.");
    }

    private async Task<(List<Line> Lines, string? FileError)> ReadAndValidateAsync(
        Guid tenantId, string? content, bool updateExisting, CancellationToken cancellationToken)
    {
        var lines = new List<Line>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return (lines, "The file is empty.");
        }
        if (content.Length > MaxContentLength)
        {
            return (lines, "The file is larger than 1 MB.");
        }

        var records = CsvText.Read(content);
        if (records.Count < 2)
        {
            return (lines, "The file has no users in it: a heading line and at least one user are needed.");
        }

        // Headings in any order and any case, with or without spaces.
        var headings = records[0].Select(h => h.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant()).ToList();
        int Column(params string[] names) => headings.FindIndex(names.Contains);
        var maconomyAt = Column("maconomynumber");
        var firstNameAt = Column("firstname");
        var lastNameAt = Column("lastname");
        var emailAt = Column("email");
        var phoneAt = Column("phone");
        var departmentAt = Column("department");
        var roleAt = Column("role", "roles");
        var statusAt = Column("status");

        var missing = new[] { (firstNameAt, "FirstName"), (lastNameAt, "LastName"), (emailAt, "Email"), (roleAt, "Role") }
            .Where(c => c.Item1 < 0).Select(c => c.Item2).ToList();
        if (missing.Count > 0)
        {
            return (lines, $"Missing column: {string.Join(", ", missing)}. Use the template's heading line.");
        }
        if (records.Count - 1 > MaxRows)
        {
            return (lines, $"The file has {records.Count - 1} users; the most one file can hold is {MaxRows}.");
        }

        static string? Cell(string[] record, int at)
            => at >= 0 && at < record.Length && record[at].Length > 0 ? record[at] : null;

        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            lines.Add(new Line
            {
                RowNumber = i,
                MaconomyNumber = Cell(record, maconomyAt),
                FirstName = Cell(record, firstNameAt),
                LastName = Cell(record, lastNameAt),
                Email = Cell(record, emailAt),
                Phone = Cell(record, phoneAt),
                Department = Cell(record, departmentAt),
                Roles = Cell(record, roleAt),
                AccountStatus = Cell(record, statusAt),
            });
        }

        // ---- What the lines are checked against ----
        var roles = (await _roles.ListVisibleToTenantAsync(tenantId, cancellationToken))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var departments = await ListDepartmentsAsync(tenantId, cancellationToken);

        var emails = lines.Where(l => l.Email is not null).Select(l => l.Email!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var userEmails = (await _users.ListEmailsInUseAsync(emails, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var personEmails = (await _persons.ListPrimaryEmailsInUseAsync(tenantId, emails, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var numbers = lines.Where(l => l.MaconomyNumber is not null).Select(l => l.MaconomyNumber!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var numbersInUse = (await _users.ListMaconomyNumbersInUseAsync(tenantId, numbers, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callerId = User.GetUserId();

        foreach (var line in lines)
        {
            var errors = new List<string>();

            // The user the email already belongs to, when this file may update them.
            User? existing = null;
            if (line.Email is null) errors.Add("Email is required.");
            else if (line.Email.Length > 256 || !EmailPattern().IsMatch(line.Email)) errors.Add("Email is not a valid address.");
            else if (!seenEmails.Add(line.Email)) errors.Add("This email is on an earlier line of the file.");
            else if (userEmails.Contains(line.Email))
            {
                if (!updateExisting) errors.Add("A user with this email already exists.");
                else
                {
                    var found = await _users.GetByEmailAsync(line.Email, cancellationToken);
                    existing = found is null ? null : await _users.GetByIdAsync(found.Id, cancellationToken);
                    if (existing is null) errors.Add("A user with this email already exists.");
                    else if (existing.TenantRoles.Any(r => r.Role == UserRole.SuperAdmin)) errors.Add("A Super Admin cannot be updated by an import.");
                    else if (!existing.TenantRoles.Any(r => r.TenantId == tenantId)) errors.Add("A user with this email exists outside this tenant and cannot be updated here.");
                    else line.ExistingUserId = existing.Id;
                }
            }
            else if (personEmails.Contains(line.Email)) errors.Add("A person with this email already exists; create their user from People.");

            // A blank cell leaves an existing user's value alone, so only a new user owes a name and a role.
            var owesAll = !(updateExisting && line.Email is not null && userEmails.Contains(line.Email));
            if (line.FirstName is null) { if (owesAll) errors.Add("First name is required."); }
            else if (line.FirstName.Length > 100) errors.Add("First name is longer than 100 characters.");

            if (line.LastName is null) { if (owesAll) errors.Add("Last name is required."); }
            else if (line.LastName.Length > 100) errors.Add("Last name is longer than 100 characters.");

            if (line.MaconomyNumber is not null)
            {
                var ownNumber = line.IsUpdate && string.Equals(existing!.MaconomyNumber, line.MaconomyNumber, StringComparison.OrdinalIgnoreCase);
                if (line.MaconomyNumber.Length > 64) errors.Add("Maconomy number is longer than 64 characters.");
                else if (!seenNumbers.Add(line.MaconomyNumber)) errors.Add("This Maconomy number is on an earlier line of the file.");
                else if (!ownNumber && numbersInUse.Contains(line.MaconomyNumber)) errors.Add("Another user already has this Maconomy number.");
            }

            // Inactive switches an account off, or creates it switched off. Active never switches one back on.
            if (line.AccountStatus is not null)
            {
                if (string.Equals(line.AccountStatus, "Inactive", StringComparison.OrdinalIgnoreCase)) line.Inactive = true;
                else if (!string.Equals(line.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase)) errors.Add("Status must be Active or Inactive.");

                if (line.Inactive && line.IsUpdate && existing!.Id == callerId) errors.Add("You cannot deactivate your own account by an import.");
            }

            if (line.Phone is not null && (line.Phone.Length > 32 || !PhonePattern().IsMatch(line.Phone)))
            {
                errors.Add("Phone is not a valid number.");
            }

            if (line.Department is not null)
            {
                var department = departments.FirstOrDefault(d =>
                    string.Equals(d.Label, line.Department, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Value, line.Department, StringComparison.OrdinalIgnoreCase));
                if (department is null) errors.Add($"Unknown department '{line.Department}'.");
                else line.DepartmentCode = department.Value.Trim().ToLowerInvariant();
            }

            // Several roles in one cell, split on a comma or a semicolon.
            var roleNames = (line.Roles ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (roleNames.Count == 0 && owesAll)
            {
                errors.Add("At least one role is required.");
            }
            foreach (var name in roleNames)
            {
                if (string.Equals(name, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)) errors.Add("Super Admin cannot be given by an import.");
                else if (!roles.TryGetValue(name, out var role)) errors.Add($"Unknown role '{name}'.");
                else line.ResolvedRoles.Add(role);
            }

            if (errors.Count == 0 && existing is not null)
            {
                var placement = await _departments.GetForUserAsync(existing.Id, cancellationToken);
                var moving = line.DepartmentCode is not null
                    && !string.Equals(placement?.Department, line.DepartmentCode, StringComparison.OrdinalIgnoreCase);
                if (moving && placement is { IsHead: true })
                {
                    errors.Add("This user heads their department; change the department on the user's page.");
                }
                else
                {
                    DescribeChanges(line, existing, tenantId, moving);
                }
            }

            // A new place is taken by an account that is switched on, and only for roles it does not already hold.
            if (!line.Inactive && (existing is null || existing.IsActive))
            {
                var held = existing?.TenantRoles.Where(r => r.TenantId == tenantId).Select(r => r.RoleId).ToHashSet() ?? new HashSet<Guid>();
                line.CapacityRoles = line.ResolvedRoles.Where(r => !held.Contains(r.Id)).ToList();
            }

            if (errors.Count > 0)
            {
                line.Error = string.Join(" ", errors);
            }
        }

        await ApplyCapacityAsync(tenantId, lines, cancellationToken);
        return (lines, null);
    }

    /// <summary>What the line would change on the user it matched: a blank cell is not a change.</summary>
    private static void DescribeChanges(Line line, User existing, Guid tenantId, bool movingDepartment)
    {
        if (existing.Person is { } person)
        {
            if (line.FirstName is not null && !string.Equals(person.FirstName, line.FirstName, StringComparison.Ordinal)) line.Changes.Add("first name");
            if (line.LastName is not null && !string.Equals(person.LastName, line.LastName, StringComparison.Ordinal)) line.Changes.Add("last name");
            if (line.Phone is not null && !string.Equals(person.MobileNumber, line.Phone, StringComparison.Ordinal)) line.Changes.Add("phone");
        }
        if (line.MaconomyNumber is not null && !string.Equals(existing.MaconomyNumber, line.MaconomyNumber, StringComparison.Ordinal)) line.Changes.Add("Maconomy number");
        if (movingDepartment) line.Changes.Add("department");

        if (line.ResolvedRoles.Count > 0)
        {
            var held = existing.TenantRoles.Where(r => r.TenantId == tenantId).Select(r => r.RoleId).ToHashSet();
            if (!held.SetEquals(line.ResolvedRoles.Select(r => r.Id))) line.Changes.Add("roles");
        }
        if (line.Inactive && existing.IsActive) line.Changes.Add("deactivated");
    }

    /// <summary>
    /// Permission group limits, counted across the file: each passing line takes a place in every capped
    /// group its roles compose, and the line that would go over the limit fails.
    /// </summary>
    private async Task ApplyCapacityAsync(Guid tenantId, List<Line> lines, CancellationToken cancellationToken)
    {
        var groupsByRole = new Dictionary<Guid, List<PermissionGroup>>();
        foreach (var roleId in lines.Where(l => l.Error is null).SelectMany(l => l.CapacityRoles).Select(r => r.Id).Distinct())
        {
            groupsByRole[roleId] = (await _permissionGroups.GetByRoleAsync(roleId, cancellationToken))
                .Where(g => g.TenantId == tenantId && g.CapacityLimit.HasValue)
                .ToList();
        }

        var groupIds = groupsByRole.Values.SelectMany(g => g).Select(g => g.Id).Distinct().ToList();
        if (groupIds.Count == 0)
        {
            return;
        }

        var used = (await _permissionGroups.CountActiveMembersForGroupsAsync(groupIds, cancellationToken))
            .ToDictionary(p => p.Key, p => p.Value);

        foreach (var line in lines.Where(l => l.Error is null))
        {
            var groups = line.CapacityRoles.SelectMany(r => groupsByRole[r.Id]).DistinctBy(g => g.Id).ToList();
            var full = groups.FirstOrDefault(g => used.GetValueOrDefault(g.Id) + 1 > g.CapacityLimit!.Value);
            if (full is not null)
            {
                line.Error = $"Permission group '{full.Name}' is at its capacity limit ({full.CapacityLimit}).";
                continue;
            }

            foreach (var group in groups)
            {
                used[group.Id] = used.GetValueOrDefault(group.Id) + 1;
            }
        }
    }

    // ---- Writing ----

    /// <summary>One line becomes a person, their account, its roles and its department, in one save.</summary>
    private async Task<Guid> CreateUserAsync(
        Guid tenantId, Guid importId, Line line, string hash, string salt, CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var fullName = $"{line.FirstName} {line.LastName}";

        var person = new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            TenantId = tenantId,
            UserId = userId,
            FirstName = line.FirstName!,
            LastName = line.LastName!,
            DisplayName = fullName,
            PrimaryEmail = line.Email,
            MobileNumber = line.Phone,
            IsActive = true,
            LastProfileUpdatedOn = DateTime.UtcNow,
            SourceEntityType = EntityType.Person,
        };
        await _persons.AddAsync(person, cancellationToken);

        await _users.AddAsync(new User
        {
            Id = userId,
            Email = line.Email!,
            DisplayName = fullName,
            PersonId = person.Id,
            PasswordHash = hash,
            Salt = salt,
            IsActive = !line.Inactive,
            MustChangePassword = true,
            TokenVersion = 1,
            MaconomyNumber = line.MaconomyNumber,
            CreatedDate = DateTime.UtcNow,
        }, cancellationToken);

        foreach (var role in line.ResolvedRoles)
        {
            await _users.AddAssignmentAsync(new UserTenantRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                Role = RoleAssignment.MapLegacyRole(role, null),
                RoleId = role.Id,
            }, cancellationToken);
        }

        // A placement only: who heads a department is set on the user's own page.
        if (line.DepartmentCode is not null)
        {
            await _departments.AddAsync(new UserDepartment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Department = line.DepartmentCode,
                IsHead = false,
            }, cancellationToken);
        }

        await _audit.AddAsync(nameof(User), userId.ToString(), "Created",
            details: $"import={importId}; roles={string.Join(",", line.ResolvedRoles.Select(r => r.Name))}; tenant={tenantId}",
            cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return userId;
    }

    /// <summary>
    /// One line onto the user its email matched, in one save: the cells that are filled, the roles as the
    /// file gives them, and never the email or the password. Read again here, because a failed line before
    /// this one clears everything that was loaded.
    /// </summary>
    private async Task UpdateUserAsync(Guid tenantId, Guid importId, Line line, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(line.ExistingUserId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("The user is gone.");

        if (user.Person is { } person)
        {
            if (line.FirstName is not null) person.FirstName = line.FirstName;
            if (line.LastName is not null) person.LastName = line.LastName;
            if (line.Phone is not null) person.MobileNumber = line.Phone;
            if (line.FirstName is not null || line.LastName is not null)
            {
                person.DisplayName = person.FullName;
                user.DisplayName = person.FullName;
            }
            person.LastProfileUpdatedOn = DateTime.UtcNow;
            _persons.Update(person);
        }

        if (line.MaconomyNumber is not null) user.MaconomyNumber = line.MaconomyNumber;

        // Switched off only: an import never switches an account back on.
        if (line.Inactive && user.IsActive)
        {
            user.IsActive = false;
            user.TokenVersion++;
        }
        _users.Update(user);

        // The file's roles are the user's roles in this tenant; a blank Role cell leaves them alone.
        if (line.ResolvedRoles.Count > 0)
        {
            var held = await _users.GetAssignmentsAsync(user.Id, tenantId, cancellationToken);
            var wanted = line.ResolvedRoles.Select(r => r.Id).ToHashSet();
            foreach (var assignment in held.Where(a => !wanted.Contains(a.RoleId)))
            {
                _users.RemoveAssignment(assignment);
            }
            var heldIds = held.Select(a => a.RoleId).ToHashSet();
            foreach (var role in line.ResolvedRoles.Where(r => !heldIds.Contains(r.Id)))
            {
                await _users.AddAssignmentAsync(new UserTenantRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TenantId = tenantId,
                    Role = RoleAssignment.MapLegacyRole(role, null),
                    RoleId = role.Id,
                }, cancellationToken);
            }
        }

        // A placement only. A head is never moved from here: the check refuses that line.
        if (line.DepartmentCode is not null)
        {
            var placement = await _departments.GetForUserAsync(user.Id, cancellationToken);
            if (placement is null)
            {
                await _departments.AddAsync(new UserDepartment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = user.Id,
                    Department = line.DepartmentCode,
                    IsHead = false,
                }, cancellationToken);
            }
            else if (!string.Equals(placement.Department, line.DepartmentCode, StringComparison.OrdinalIgnoreCase))
            {
                if (placement.IsHead) throw new InvalidOperationException("The user became a department head.");
                placement.Department = line.DepartmentCode;
                _departments.Update(placement);
            }
        }

        await _audit.AddAsync(nameof(User), user.Id.ToString(), "Updated",
            details: $"import={importId}; changes={string.Join(",", line.Changes)}; tenant={tenantId}",
            cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ---- Mapping ----

    private static UserImportSummary ToSummary(UserImport import, IReadOnlyDictionary<Guid, string> names)
        => new(import.Id, import.FileName, import.TotalCount, import.CreatedCount, import.FailedCount,
            import.CreatedById is { } by && names.TryGetValue(by, out var name) ? name : null, import.CreatedOnUtc,
            import.UpdatedCount);

    private static UserImportDetail ToDetail(UserImport import, IReadOnlyDictionary<Guid, string> names)
        => new(ToSummary(import, names), import.Rows.OrderBy(r => r.RowNumber).Select(r => new UserImportRowResult(
            r.RowNumber, r.MaconomyNumber, r.FirstName, r.LastName, r.Email, r.Phone, r.Department, r.Roles,
            r.Status.ToString(), r.Error, r.CreatedUserId, r.AccountStatus)).ToList());

    private static string? Cut(string? value, int length)
        => value is { Length: var n } && n > length ? value[..length] : value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // Digits with the usual spacing and punctuation, an optional leading plus.
    [GeneratedRegex(@"^\+?[0-9][0-9 ().\-]{4,}$")]
    private static partial Regex PhonePattern();
}
