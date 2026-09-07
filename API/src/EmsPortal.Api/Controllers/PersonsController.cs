using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Persons;
using EmsPortal.Api.Models.Profile;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Person (CRM master record) management.</summary>
[ApiController]
[Route("/api/admin/persons")]
[Produces("application/json")]
[Tags("Persons")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonRepository _persons;
    private readonly IAddressRepository _addresses;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _audit;

    public PersonsController(
        IPersonRepository persons,
        IAddressRepository addresses,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IAuditTrailService audit)
    {
        _persons = persons;
        _addresses = addresses;
        _users = users;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    [HttpPost]
    [RequirePermission(Permissions.PersonsWrite)]
    [ProducesResponseType<ApiResponse<PersonDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePersonRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "First and last name are required."));
        }

        var fullName = string.Join(" ", new[] { request.FirstName, request.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? fullName : request.DisplayName!;

        var person = new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = await GeneratePersonCodeAsync(cancellationToken),
            // A non-Super-Admin can only create within their own tenant; a client-supplied TenantId is honoured
            // only for Super Admins.
            TenantId = (User.IsSuperAdmin() ? request.TenantId : null) ?? User.GetActiveTenantId(),
            Suffix = request.Suffix,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            DisplayName = displayName,
            PreferredName = request.PreferredName,
            Gender = request.Gender,
            DateOfBirth = request.DateOfBirth,
            MaritalStatus = request.MaritalStatus,
            Nationality = request.Nationality,
            PrimaryEmail = request.PrimaryEmail,
            SecondaryEmail = request.SecondaryEmail,
            MobileNumber = request.MobileNumber,
            CountryCode = request.CountryCode,
            AlternateMobileNumber = request.AlternateMobileNumber,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactRelationship = request.EmergencyContactRelationship,
            EmergencyContactNumber = request.EmergencyContactNumber,
            EmployeeCode = request.EmployeeCode,
            Notes = request.Notes,
            IsActive = true,
            LastProfileUpdatedOn = DateTime.UtcNow,
            // Entered directly on the Person screen. No SourceEntityId: there is no other record behind
            // this one to point back at.
            SourceEntityType = EntityType.Person,
        };

        if (request.Address is { } addressInput)
        {
            await UpsertAddressAsync(person, addressInput, cancellationToken);
        }

        person.ProfileCompletionPercentage = ComputeCompletion(person);
        await _persons.AddAsync(person, cancellationToken);
        await _audit.AddAsync(nameof(Person), person.Id.ToString(), "Created", details: person.PersonCode, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var created = await LoadAsync(person.Id, cancellationToken) ?? person;
        return StatusCode(StatusCodes.Status201Created,
            ApiResponseFactory.Success(new PersonDetail(PersonProfileMapper.Map(created, await RecordAudit.ForAsync(_users, created, cancellationToken)), created.UserId is not null), "Person created."));
    }

    [HttpGet]
    [RequirePermission(Permissions.PersonsRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] bool? isUser = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        // Super Admins may scope to any tenant; everyone else is pinned to their active tenant by the
        // ambient query filter (a client-supplied tenantId is ignored for non-Super-Admins).
        Guid? scopeTenant = User.IsSuperAdmin() && tenantId is { } tid ? tid : null;
        var (items, total) = await _persons.ListAsync(
            search, scopeTenant, isUser, isActive, new SortRequest(sortBy, descending), page, limit,
            cancellationToken: cancellationToken);
        var names = await ResolveActorNamesAsync(items.SelectMany(p => new[] { p.CreatedById, p.UpdatedById }), cancellationToken);
        var summaries = items.Select(p => new PersonSummary(
            p.Id, p.PersonCode, p.FullName, p.PrimaryEmail, p.MobileNumber,
            p.TenantId, p.Tenant?.Name,
            p.UserId is not null, p.IsActive,
            p.SourceEntityType?.ToString(), p.SourceEntityId,
            NameOf(names, p.CreatedById), NameOf(names, p.UpdatedById), p.CreatedOnUtc, p.UpdatedOnUtc));

        return Ok(ApiResponseFactory.Paginated(summaries, "Persons retrieved.", page, limit, total));
    }

    /// <summary>
    /// Selectable persons for the user-create dropdown (already-promoted persons carry
    /// <c>IsUser=true</c>).
    /// </summary>
    [HttpGet("selectable")]
    [RequirePermission(Permissions.PersonsRead)]
    public async Task<IActionResult> Selectable([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var scope = tenantId is { } requested && User.HasPermission(Permissions.TenantsWrite)
            ? requested
            : (Guid?)null;
        var items = await _persons.ListSelectableAsync(scope, cancellationToken);
        var options = items.Select(x => new PersonSelectItem(
            x.Person.Id, x.Person.FullName, x.Person.PrimaryEmail, x.Person.MobileNumber, x.Person.CountryCode, x.Person.TenantId, x.IsUser));
        return Ok(ApiResponseFactory.Success(options, "Persons retrieved."));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.PersonsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var person = await LoadAsync(id, cancellationToken);
        return person is null
            ? NotFound(ApiResponseFactory.NotFound("Person not found."))
            : Ok(ApiResponseFactory.Success(new PersonDetail(PersonProfileMapper.Map(person, await RecordAudit.ForAsync(_users, person, cancellationToken)), person.UserId is not null), "Person retrieved."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.PersonsWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonRequest request, CancellationToken cancellationToken)
    {
        var person = await LoadAsync(id, cancellationToken);
        if (person is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Person not found."));
        }

        // Personal
        Apply(request.Suffix, v => person.Suffix = v);
        Apply(request.FirstName, v => person.FirstName = v);
        Apply(request.MiddleName, v => person.MiddleName = v);
        Apply(request.LastName, v => person.LastName = v);
        Apply(request.PreferredName, v => person.PreferredName = v);
        Apply(request.Gender, v => person.Gender = v);
        Apply(request.MaritalStatus, v => person.MaritalStatus = v);
        Apply(request.Nationality, v => person.Nationality = v);
        if (request.DateOfBirth.HasValue)
        {
            person.DateOfBirth = request.DateOfBirth;
        }
        if (request.DisplayName is { } displayName)
        {
            person.DisplayName = displayName;
        }

        // Contact
        Apply(request.PrimaryEmail, v => person.PrimaryEmail = v);
        Apply(request.SecondaryEmail, v => person.SecondaryEmail = v);
        Apply(request.MobileNumber, v => person.MobileNumber = v);
        Apply(request.CountryCode, v => person.CountryCode = v);
        Apply(request.AlternateMobileNumber, v => person.AlternateMobileNumber = v);
        Apply(request.EmergencyContactName, v => person.EmergencyContactName = v);
        Apply(request.EmergencyContactRelationship, v => person.EmergencyContactRelationship = v);
        Apply(request.EmergencyContactNumber, v => person.EmergencyContactNumber = v);

        // Professional
        Apply(request.EmployeeCode, v => person.EmployeeCode = v);
        Apply(request.Notes, v => person.Notes = v);
        if (request.IsActive.HasValue)
        {
            person.IsActive = request.IsActive.Value;
        }
        // Only a Super Admin may move a person to a different tenant.
        if (request.TenantId.HasValue && User.IsSuperAdmin())
        {
            person.TenantId = request.TenantId;
        }

        if (request.Address is { } addressInput)
        {
            await UpsertAddressAsync(person, addressInput, cancellationToken);
        }

        // Keep the linked user's display name in sync with the person's.
        if (person.UserId is { } linkedUserId)
        {
            var user = await _users.GetByIdAsync(linkedUserId, cancellationToken);
            if (user is not null)
            {
                user.DisplayName = person.FullName;
                _users.Update(user);
            }
        }

        person.LastProfileUpdatedOn = DateTime.UtcNow;
        person.ProfileCompletionPercentage = ComputeCompletion(person);
        _persons.Update(person);

        await _audit.AddAsync(nameof(Person), person.Id.ToString(), "Updated", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(person.Id, cancellationToken) ?? person;
        return Ok(ApiResponseFactory.Success(new PersonDetail(PersonProfileMapper.Map(refreshed, await RecordAudit.ForAsync(_users, refreshed, cancellationToken)), refreshed.UserId is not null), "Person updated."));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PersonsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // Deletion is restricted to Super Admins regardless of any granted permission.
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Only a Super Admin can delete persons."));
        }

        var person = await LoadAsync(id, cancellationToken);
        if (person is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Person not found."));
        }

        // A person backing a login account cannot be deleted; remove the user first.
        if (person.UserId is not null)
        {
            return Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot delete a person linked to a user.",
                "Delete or unlink the associated user account first."));
        }

        _persons.Remove(person);
        await _audit.AddAsync(nameof(Person), person.Id.ToString(), "Deleted", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { id }, "Person deleted."));
    }

    /// <summary>Loads a single person: Super Admins read across tenants; everyone else is tenant-scoped.</summary>
    private Task<Person?> LoadAsync(Guid id, CancellationToken cancellationToken)
        => User.IsSuperAdmin()
            ? _persons.GetByIdUnscopedAsync(id, cancellationToken)
            : _persons.GetByIdAsync(id, cancellationToken);

    private async Task UpsertAddressAsync(Person person, AddressInput input, CancellationToken cancellationToken)
    {
        var address = person.AddressId is { } addressId
            ? await _addresses.GetByIdAsync(addressId, cancellationToken)
            : null;

        var isNew = address is null;
        address ??= new Address { Id = Guid.NewGuid() };

        address.AddressType = Enum.TryParse<AddressType>(input.AddressType, ignoreCase: true, out var type) ? type : AddressType.Home;
        address.AddressLine1 = input.AddressLine1;
        address.AddressLine2 = input.AddressLine2;
        address.Landmark = input.Landmark;
        address.BuildingName = input.BuildingName;
        address.FloorNumber = input.FloorNumber;
        address.UnitNumber = input.UnitNumber;
        address.CountryCode = input.CountryCode;
        address.CountryName = input.CountryName;
        address.StateCode = input.StateCode;
        address.StateName = input.StateName;
        address.CityName = input.CityName;
        address.PostalCode = input.PostalCode;

        if (isNew)
        {
            await _addresses.AddAsync(address, cancellationToken);
            person.AddressId = address.Id;
            person.Address = address;
        }
        else
        {
            _addresses.Update(address);
        }
    }

    private static void Apply(string? value, Action<string> set)
    {
        if (value is not null)
        {
            set(value);
        }
    }

    /// <summary>Rough completion score over a representative set of core profile fields.</summary>
    private static int ComputeCompletion(Person p)
    {
        var fields = new[]
        {
            p.FirstName, p.LastName, p.DisplayName, p.PreferredName, p.Gender,
            p.PrimaryEmail, p.MobileNumber, p.Nationality
        };
        var filled = fields.Count(f => !string.IsNullOrWhiteSpace(f));
        var total = fields.Length + 2; // + date of birth + address
        if (p.DateOfBirth.HasValue) filled++;
        if (p.AddressId.HasValue) filled++;
        return (int)Math.Round(filled * 100.0 / total);
    }

    /// <summary>Generates a unique business person code (e.g. PER-AB12CD34EF).</summary>
    private async Task<string> GeneratePersonCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = "PER-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            if (!await _persons.PersonCodeExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }

        return "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveActorNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => await _users.GetFullNamesAsync(ids.Where(id => id.HasValue).Select(id => id!.Value), cancellationToken);

    private static string? NameOf(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;
}
