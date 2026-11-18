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

        var (lines, fileError) = await ReadAndValidateAsync(tenantId, request.Content, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "The file cannot be imported.", fileError));
        }

        var rows = lines.Select(l => l.ToResult(l.Error is null ? "Ready" : "Error", null)).ToList();
        var ready = lines.Count(l => l.Error is null);
        return Ok(ApiResponseFactory.Success(
            new UserImportCheckResponse(lines.Count, ready, lines.Count - ready, rows), "File checked."));
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

        // The rule a user's own new password has to meet.
        var password = request.TemporaryPassword ?? string.Empty;
        if (password.Length < 8 || !password.Any(char.IsAsciiLetterUpper) || !password.Any(char.IsAsciiDigit))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.",
                "The temporary password needs at least 8 characters, an uppercase letter and a digit."));
        }

        var (lines, fileError) = await ReadAndValidateAsync(tenantId, request.Content, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "The file cannot be imported.", fileError));
        }

        // Hashed once: it is the same password for everyone, and 500 hashes would take the best part of a minute.
        var (hash, salt) = _passwordHasher.Hash(password);
        var importId = Guid.NewGuid();
        var createdUserIds = new Dictionary<int, Guid>();

        foreach (var line in lines.Where(l => l.Error is null))
        {
            try
            {
                createdUserIds[line.RowNumber] = await CreateUserAsync(tenantId, importId, line, hash, salt, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Each line is its own save, so the ones before it stand.
                _unitOfWork.DiscardChanges();
                line.Error = "Could not be saved. The email may have been taken while the file was importing.";
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
            FailedCount = lines.Count - createdUserIds.Count,
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
                Status = l.Error is null ? UserImportRowStatus.Created : UserImportRowStatus.Failed,
                Error = Cut(l.Error, 1000),
                CreatedUserId = createdUserIds.TryGetValue(l.RowNumber, out var userId) ? userId : null,
            }).ToList(),
        };
        await _imports.AddAsync(import, cancellationToken);
        await _audit.AddAsync(nameof(UserImport), importId.ToString(), "Imported",
            details: $"file={import.FileName}; created={import.CreatedCount}; failed={import.FailedCount}",
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

        public List<Role> ResolvedRoles { get; } = new();
        public string? DepartmentCode { get; set; }
        public string? Error { get; set; }

        public UserImportRowResult ToResult(string status, Guid? userId)
            => new(RowNumber, MaconomyNumber, FirstName, LastName, Email, Phone, Department, Roles, status, Error, userId);
    }

    private async Task<(List<Line> Lines, string? FileError)> ReadAndValidateAsync(
        Guid tenantId, string? content, CancellationToken cancellationToken)
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

        foreach (var line in lines)
        {
            var errors = new List<string>();

            if (line.FirstName is null) errors.Add("First name is required.");
            else if (line.FirstName.Length > 100) errors.Add("First name is longer than 100 characters.");

            if (line.LastName is null) errors.Add("Last name is required.");
            else if (line.LastName.Length > 100) errors.Add("Last name is longer than 100 characters.");

            if (line.Email is null) errors.Add("Email is required.");
            else if (line.Email.Length > 256 || !EmailPattern().IsMatch(line.Email)) errors.Add("Email is not a valid address.");
            else if (!seenEmails.Add(line.Email)) errors.Add("This email is on an earlier line of the file.");
            else if (userEmails.Contains(line.Email)) errors.Add("A user with this email already exists.");
            else if (personEmails.Contains(line.Email)) errors.Add("A person with this email already exists; create their user from People.");

            if (line.MaconomyNumber is not null)
            {
                if (line.MaconomyNumber.Length > 64) errors.Add("Maconomy number is longer than 64 characters.");
                else if (!seenNumbers.Add(line.MaconomyNumber)) errors.Add("This Maconomy number is on an earlier line of the file.");
                else if (numbersInUse.Contains(line.MaconomyNumber)) errors.Add("Another user already has this Maconomy number.");
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
            if (roleNames.Count == 0)
            {
                errors.Add("At least one role is required.");
            }
            foreach (var name in roleNames)
            {
                if (string.Equals(name, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)) errors.Add("Super Admin cannot be given by an import.");
                else if (!roles.TryGetValue(name, out var role)) errors.Add($"Unknown role '{name}'.");
                else line.ResolvedRoles.Add(role);
            }

            if (errors.Count > 0)
            {
                line.Error = string.Join(" ", errors);
            }
        }

        await ApplyCapacityAsync(tenantId, lines, cancellationToken);
        return (lines, null);
    }

    /// <summary>
    /// Permission group limits, counted across the file: each passing line takes a place in every capped
    /// group its roles compose, and the line that would go over the limit fails.
    /// </summary>
    private async Task ApplyCapacityAsync(Guid tenantId, List<Line> lines, CancellationToken cancellationToken)
    {
        var groupsByRole = new Dictionary<Guid, List<PermissionGroup>>();
        foreach (var roleId in lines.Where(l => l.Error is null).SelectMany(l => l.ResolvedRoles).Select(r => r.Id).Distinct())
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
            var groups = line.ResolvedRoles.SelectMany(r => groupsByRole[r.Id]).DistinctBy(g => g.Id).ToList();
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
            IsActive = true,
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

    // ---- Mapping ----

    private static UserImportSummary ToSummary(UserImport import, IReadOnlyDictionary<Guid, string> names)
        => new(import.Id, import.FileName, import.TotalCount, import.CreatedCount, import.FailedCount,
            import.CreatedById is { } by && names.TryGetValue(by, out var name) ? name : null, import.CreatedOnUtc);

    private static UserImportDetail ToDetail(UserImport import, IReadOnlyDictionary<Guid, string> names)
        => new(ToSummary(import, names), import.Rows.OrderBy(r => r.RowNumber).Select(r => new UserImportRowResult(
            r.RowNumber, r.MaconomyNumber, r.FirstName, r.LastName, r.Email, r.Phone, r.Department, r.Roles,
            r.Status.ToString(), r.Error, r.CreatedUserId)).ToList());

    private static string? Cut(string? value, int length)
        => value is { Length: var n } && n > length ? value[..length] : value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // Digits with the usual spacing and punctuation, an optional leading plus.
    [GeneratedRegex(@"^\+?[0-9][0-9 ().\-]{4,}$")]
    private static partial Regex PhonePattern();
}
