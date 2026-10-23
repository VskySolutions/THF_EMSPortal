using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Rems;

/// <summary>
/// The one place the intake form's V1 payload meets the client master. Reading builds the payload the
/// public form and the submitted-form view show from the Client_* rows; writing upserts those rows from a
/// payload, whether the client is saving a draft, submitting, or staff are correcting the submission.
/// There is no stored copy of the payload: the rows are the record.
/// </summary>
public sealed class RemsClientIntake
{
    private readonly IClientRepository _clients;
    private readonly IPersonRepository _persons;
    private readonly IAddressRepository _addresses;
    private readonly IOptionCodeResolver _codes;
    private readonly IClientNumberGenerator _clientNumbers;

    public RemsClientIntake(
        IClientRepository clients,
        IPersonRepository persons,
        IAddressRepository addresses,
        IOptionCodeResolver codes,
        IClientNumberGenerator clientNumbers)
    {
        _clients = clients;
        _persons = persons;
        _addresses = addresses;
        _codes = codes;
        _clientNumbers = clientNumbers;
    }

    // ------------------------------------------------------------------------------------------------
    // Read: rows -> payload
    // ------------------------------------------------------------------------------------------------

    /// <summary>The form as the client would see it now, built from the client's rows.</summary>
    public static RemsFormPayloadV1 ToPayload(RemsRequest request, string entityType, string lockedEmail)
    {
        var client = request.Client ?? throw new InvalidOperationException("The request has no client loaded.");
        var person = client.Person;
        var payload = new RemsFormPayloadV1
        {
            Version = 1,
            ClientName = client.IsOrganisation ? client.LegalName : person?.DisplayName,
            ClientFirstName = client.IsOrganisation ? null : person?.FirstName,
            ClientLastName = client.IsOrganisation ? null : person?.LastName,
            ClientSuffix = client.IsOrganisation ? null : person?.Suffix,
            Email = lockedEmail.Length > 0 ? lockedEmail : client.ClientEmail,
            MobileNumber = client.PrimaryPhone,
            ReferralSource = client.ReferralSource?.Value,
            ReferralSourceDetail = client.ReferralSourceDetail,
            Ein = client.EIN,
        };

        var physical = AddressesOfKind(client, RemsAddressTypes.Physical).FirstOrDefault();
        var mailing = AddressesOfKind(client, RemsAddressTypes.Mailing).FirstOrDefault();
        payload.PhysicalAddress = physical is null ? null : AddressPayload(physical.Address!);
        payload.MailingAddress = mailing is null ? null : AddressPayload(mailing.Address!);
        payload.MailingSameAsPhysical = physical is not null && mailing is not null && SameAddress(physical.Address!, mailing.Address!);
        payload.BillingAddresses = AddressesOfKind(client, RemsAddressTypes.Billing).Select(a => AddressPayload(a.Address!)).ToList();

        var roles = new RemsRolesPayload();
        var billing = new List<RemsRolePayload>();
        foreach (var contact in client.Contacts.Where(c => !c.Deleted).OrderBy(c => c.CreatedOnUtc))
        {
            var role = RolePayload(contact.Person);
            switch (contact.ContactRole?.Value)
            {
                case "self": roles.Self = role; break;
                case "spouse": roles.Spouse = role; break;
                case "primary_client_contact": roles.PrimaryContact = role; break;
                case "financial_contact": roles.FinancialContact = role; break;
                case "other_contact": roles.OtherContact = role; break;
                case "finance_director": roles.FinanceDirector = role; break;
                case "trust_estate_contact": roles.TrustEstateContact = role; break;
                case "banker": roles.Banker = role; break;
                case "lawyer": roles.Lawyer = role; break;
                case RemsContactRoles.BillingContactCode: billing.Add(role); break;
            }
        }
        payload.Roles = roles;
        payload.AdditionalBillingContacts = billing;
        var firstBilling = billing.FirstOrDefault();
        payload.BillingContactName = firstBilling?.DisplayName;
        payload.BillingEmail = firstBilling?.Email;

        payload.AdditionalIndividuals = client.Relationships
            .Where(r => !r.Deleted && r.RelatedClient is { IsOrganisation: false })
            .OrderBy(r => r.CreatedOnUtc)
            .Select(r =>
            {
                var related = r.RelatedClient!;
                var billed = RemsWorkspaceMapper.BillingContact(related)?.Person;
                return new RemsAdditionalIndividualPayload
                {
                    SourceKey = r.Id.ToString("N"),
                    Type = r.RelationType?.Value,
                    FilingType = r.FilingType?.Value,
                    FirstName = related.Person?.FirstName,
                    LastName = related.Person?.LastName,
                    Suffix = related.Person?.Suffix,
                    Email = related.ClientEmail ?? related.Person?.PrimaryEmail,
                    Phone = related.ClientPhone ?? related.Person?.MobileNumber,
                    IsMinor = r.IsMinor,
                    BillingPreference = r.BillingPreference?.Value,
                    BillingFirstName = billed?.FirstName,
                    BillingLastName = billed?.LastName,
                };
            })
            .ToList();

        payload.RelatedEntities = client.Relationships
            .Where(r => !r.Deleted && r.RelatedClient is { IsOrganisation: true })
            .OrderBy(r => r.CreatedOnUtc)
            .Select(r => new RemsRelatedEntityPayload
            {
                SourceKey = r.Id.ToString("N"),
                FullName = r.RelatedClient!.LegalName,
                EmailAddress = r.RelatedClient.ClientEmail,
                PhoneNumber = r.RelatedClient.ClientPhone,
            })
            .ToList();

        var contract = request.Contract ?? client.Contracts.Where(c => !c.Deleted).OrderByDescending(c => c.IsCurrent).ThenByDescending(c => c.CreatedOnUtc).FirstOrDefault();
        if (contract is not null)
        {
            payload.ContractStartDate = contract.ContractStartDate;
            payload.ContractEndDate = contract.ContractEndDate;
            payload.OriginalTerm = contract.OriginalTerm;
            payload.RenewalTerms = contract.RenewalTerms;
            payload.PoStartDate = contract.PurchaseOrderStartDate;
            payload.PoEndDate = contract.PurchaseOrderEndDate;
        }
        return payload;
    }

    /// <summary>Whether the client's rows hold anything the form could have written: the draft exists once they do.</summary>
    public static bool HasFormContent(Client client)
        => client.Addresses.Any(a => !a.Deleted) || client.Contacts.Any(c => !c.Deleted)
            || client.Relationships.Any(r => !r.Deleted) || client.Contracts.Any(c => !c.Deleted)
            || client.ReferralSourceId is not null || client.EIN is not null;

    // ------------------------------------------------------------------------------------------------
    // Write: payload -> rows
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a payload onto the request's client. The client's email is never taken from the form; it is
    /// locked to the request. Everything else is replaced by what the form says, row by row.
    /// </summary>
    public async Task ApplyAsync(RemsRequest request, RemsFormPayloadV1 payload, string entityType, CancellationToken cancellationToken)
    {
        var client = request.Client ?? throw new InvalidOperationException("The request has no client loaded.");
        var tenantId = request.TenantId;
        var isBusiness = RemsFormPayloadValidator.IsBusinessGroup(entityType);
        var isIndividual = string.Equals(entityType, RemsFormPayloadValidator.Individual, StringComparison.Ordinal);
        var isGovernment = string.Equals(entityType, RemsFormPayloadValidator.Government, StringComparison.Ordinal);

        // The client row and, for an individual, the person it points at.
        client.PrimaryPhone = Clean(payload.MobileNumber);
        client.ReferralSourceId = await _codes.IdOfAsync(EntityType.Rems, RemsOptionSetKeys.ReferralSource, Clean(payload.ReferralSource), cancellationToken);
        client.ReferralSourceDetail = Clean(payload.ReferralSourceDetail);
        client.EIN = isBusiness ? Clean(payload.Ein) : null;
        if (client.IsOrganisation)
        {
            client.LegalName = Clean(payload.EffectiveClientName) ?? client.LegalName;
        }
        else if (client.Person is { } person)
        {
            if (Clean(payload.ClientFirstName) is { } first) person.FirstName = first;
            if (Clean(payload.ClientLastName) is { } last) person.LastName = last;
            person.Suffix = Clean(payload.ClientSuffix);
            person.DisplayName = string.Join(" ", new[] { person.FirstName, person.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
            person.LastProfileUpdatedOn = DateTime.UtcNow;
            _persons.Update(person);
        }
        _clients.Update(client);

        await UpsertAddressesAsync(client, tenantId, RemsAddressTypes.Physical,
            payload.PhysicalAddress is { HasAny: true } ? new[] { payload.PhysicalAddress } : Array.Empty<RemsAddressPayload>(), cancellationToken);
        await UpsertAddressesAsync(client, tenantId, RemsAddressTypes.Mailing,
            payload.EffectiveMailingAddress is { HasAny: true } mailing ? new[] { mailing } : Array.Empty<RemsAddressPayload>(), cancellationToken);
        await UpsertAddressesAsync(client, tenantId, RemsAddressTypes.Billing,
            payload.EffectiveBillingAddresses.Where(a => a is { HasAnyContent: true }).ToList(), cancellationToken);

        await UpsertContactsAsync(request, client, entityType, payload, cancellationToken);

        if (isIndividual)
        {
            await UpsertIndividualsAsync(request, client, payload.AdditionalIndividuals.Where(i => i is { HasAny: true }).ToList(), cancellationToken);
        }
        else
        {
            await UpsertRelatedEntitiesAsync(request, client, payload.RelatedEntities.Where(e => Clean(e.FullName) is not null).ToList(), cancellationToken);
        }

        if (isGovernment)
        {
            await UpsertContractAsync(request, client, payload, cancellationToken);
        }
    }

    private async Task UpsertAddressesAsync(Client client, Guid tenantId, string kind, IReadOnlyList<RemsAddressPayload> wanted, CancellationToken cancellationToken)
    {
        var typeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.AddressType, kind, cancellationToken);
        var existing = AddressesOfKind(client, kind).ToList();
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < existing.Count)
            {
                Fill(existing[i].Address!, wanted[i]);
                _addresses.Update(existing[i].Address!);
                continue;
            }
            var address = new Address { Id = Guid.NewGuid(), AddressType = RemsAddressTypes.PlatformTypeOf(kind) };
            Fill(address, wanted[i]);
            await _addresses.AddAsync(address, cancellationToken);
            var link = new ClientAddress
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ClientId = client.Id, AddressId = address.Id, AddressTypeId = typeId, IsCurrent = true,
                Address = address,
            };
            client.Addresses.Add(link);
            await _clients.AddAddressAsync(link, cancellationToken);
        }
        foreach (var surplus in existing.Skip(wanted.Count))
        {
            client.Addresses.Remove(surplus);
            _clients.RemoveAddress(surplus);
        }
    }

    private async Task UpsertContactsAsync(RemsRequest request, Client client, string entityType, RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var roles = payload.EffectiveRoles;
        var wanted = new List<(string Code, RemsRolePayload Role)>();
        foreach (var name in RemsContactRoles.RolesFor(entityType))
        {
            var role = name switch
            {
                nameof(RemsContactRole.Self) => roles.Self,
                nameof(RemsContactRole.Spouse) => roles.Spouse,
                nameof(RemsContactRole.PrimaryClientContact) => roles.PrimaryContact,
                nameof(RemsContactRole.FinancialContact) => roles.FinancialContact,
                nameof(RemsContactRole.TrustEstateContact) => roles.TrustEstateContact,
                nameof(RemsContactRole.OtherContact) => roles.OtherContact,
                nameof(RemsContactRole.FinanceDirector) => roles.FinanceDirector,
                nameof(RemsContactRole.BillingContact) => roles.BillingContact,
                nameof(RemsContactRole.Banker) => roles.Banker,
                nameof(RemsContactRole.Lawyer) => roles.Lawyer,
                _ => null,
            };
            if (role is { HasAny: true })
            {
                wanted.Add((RemsContactRoles.CodeOf(name)!, role));
            }
        }
        foreach (var extra in payload.AdditionalBillingContacts.Where(r => r is { HasAny: true }))
        {
            wanted.Add((RemsContactRoles.BillingContactCode, extra));
        }
        // The retired billing name/email pair is one more billing contact when nothing else names them.
        if (!wanted.Any(w => w.Code == RemsContactRoles.BillingContactCode)
            && (Clean(payload.BillingContactName) is not null || Clean(payload.BillingEmail) is not null))
        {
            wanted.Add((RemsContactRoles.BillingContactCode, new RemsRolePayload { Name = Clean(payload.BillingContactName), Email = Clean(payload.BillingEmail) }));
        }

        var existing = client.Contacts.Where(c => !c.Deleted).OrderBy(c => c.CreatedOnUtc).ToList();
        var kept = new HashSet<Guid>();
        foreach (var group in wanted.GroupBy(w => w.Code))
        {
            var typeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.ContactRole, group.Key, cancellationToken);
            var rows = existing.Where(c => string.Equals(c.ContactRole?.Value, group.Key, StringComparison.Ordinal)).ToList();
            var i = 0;
            foreach (var (_, role) in group)
            {
                if (i < rows.Count && rows[i].Person is { } person)
                {
                    FillPerson(person, role);
                    _persons.Update(person);
                    kept.Add(rows[i].Id);
                }
                else
                {
                    var created = NewPerson(request, role);
                    await _persons.AddAsync(created, cancellationToken);
                    var contact = new ClientContact
                    {
                        Id = Guid.NewGuid(), TenantId = request.TenantId, ClientId = client.Id, PersonId = created.Id, ContactRoleId = typeId,
                        IsPrimary = group.Key == "primary_client_contact", Person = created,
                    };
                    client.Contacts.Add(contact);
                    await _clients.AddContactAsync(contact, cancellationToken);
                    kept.Add(contact.Id);
                }
                i++;
            }
        }
        foreach (var gone in existing.Where(c => !kept.Contains(c.Id)))
        {
            client.Contacts.Remove(gone);
            _clients.RemoveContact(gone);
        }
    }

    private async Task UpsertIndividualsAsync(RemsRequest request, Client client, IReadOnlyList<RemsAdditionalIndividualPayload> wanted, CancellationToken cancellationToken)
    {
        var individualTypeId = await _codes.IdOfAsync(EntityType.Rems, RemsOptionSetKeys.EntityType, RemsFormPayloadValidator.Individual, cancellationToken);
        var billingRoleId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.ContactRole, RemsContactRoles.BillingContactCode, cancellationToken);
        var existing = client.Relationships.Where(r => !r.Deleted && r.RelatedClient is { IsOrganisation: false }).ToList();
        var kept = new HashSet<Guid>();
        foreach (var individual in wanted)
        {
            var match = MatchIndividual(existing, individual, kept);
            var relationTypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.RelationType, Clean(individual.Type) ?? RemsRelationTypes.Other, cancellationToken);
            var filingTypeId = await _codes.IdOfAsync(EntityType.Rems, RemsOptionSetKeys.FilingType, individual.EffectiveFilingType, cancellationToken);
            var billingPreferenceId = await _codes.IdOfAsync(EntityType.Rems, RemsOptionSetKeys.BillingPreference, individual.EffectiveBillingPreference, cancellationToken);
            Client related;
            if (match is null)
            {
                var person = new Person
                {
                    Id = Guid.NewGuid(),
                    PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    TenantId = request.TenantId,
                    SourceEntityType = EntityType.Rems,
                    SourceEntityId = request.Id,
                    IsActive = true,
                };
                FillIndividual(person, individual);
                await _persons.AddAsync(person, cancellationToken);
                related = new Client
                {
                    Id = person.Id,
                    TenantId = request.TenantId,
                    ClientNumber = await _clientNumbers.GenerateAsync(cancellationToken),
                    Status = ClientStatus.Prospect,
                    PersonId = person.Id,
                    EntityTypeId = individualTypeId,
                    PrimaryEmail = Clean(individual.Email),
                    PrimaryPhone = Clean(individual.Phone),
                    Person = person,
                };
                await _clients.AddAsync(related, cancellationToken);
                match = new ClientRelationship
                {
                    Id = Guid.NewGuid(), TenantId = request.TenantId, ClientId = client.Id, RelatedClientId = related.Id,
                    RelationTypeId = relationTypeId, RelatedClient = related,
                };
                client.Relationships.Add(match);
                await _clients.AddRelationshipAsync(match, cancellationToken);
            }
            else
            {
                related = match.RelatedClient!;
                if (related.Person is { } person)
                {
                    FillIndividual(person, individual);
                    _persons.Update(person);
                }
                related.PrimaryEmail = Clean(individual.Email);
                related.PrimaryPhone = Clean(individual.Phone);
                _clients.Update(related);
                match.RelationTypeId = relationTypeId;
                _clients.UpdateRelationship(match);
            }
            match.FilingTypeId = filingTypeId;
            match.BillingPreferenceId = billingPreferenceId;
            match.IsMinor = individual.EffectiveIsMinor ?? false;
            kept.Add(match.Id);

            // Who is billed for this person, when it is someone else: a billing contact on their client.
            var billed = RemsWorkspaceMapper.BillingContact(related);
            var billingFirst = individual.AsksBillingName ? Clean(individual.BillingFirstName) : null;
            var billingLast = individual.AsksBillingName ? Clean(individual.BillingLastName) : null;
            if (billingFirst is not null || billingLast is not null)
            {
                if (billed?.Person is { } billedPerson)
                {
                    billedPerson.FirstName = billingFirst ?? string.Empty;
                    billedPerson.LastName = billingLast ?? string.Empty;
                    billedPerson.DisplayName = string.Join(" ", new[] { billingFirst, billingLast }.Where(p => p is not null));
                    _persons.Update(billedPerson);
                }
                else
                {
                    var newBilled = NewPerson(request, new RemsRolePayload { FirstName = billingFirst, LastName = billingLast });
                    await _persons.AddAsync(newBilled, cancellationToken);
                    var contact = new ClientContact
                    {
                        Id = Guid.NewGuid(), TenantId = request.TenantId, ClientId = related.Id, PersonId = newBilled.Id, ContactRoleId = billingRoleId,
                        Person = newBilled,
                    };
                    related.Contacts.Add(contact);
                    await _clients.AddContactAsync(contact, cancellationToken);
                }
            }
            else if (billed is not null)
            {
                related.Contacts.Remove(billed);
                _clients.RemoveContact(billed);
            }
        }
        foreach (var gone in existing.Where(r => !kept.Contains(r.Id)))
        {
            client.Relationships.Remove(gone);
            _clients.RemoveRelationship(gone);
        }
    }

    private async Task UpsertRelatedEntitiesAsync(RemsRequest request, Client client, IReadOnlyList<RemsRelatedEntityPayload> wanted, CancellationToken cancellationToken)
    {
        var relationTypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.RelationType, RemsRelationTypes.RelatedEntity, cancellationToken);
        var existing = client.Relationships.Where(r => !r.Deleted && r.RelatedClient is { IsOrganisation: true }).ToList();
        var kept = new HashSet<Guid>();
        foreach (var entity in wanted)
        {
            var name = Clean(entity.FullName)!;
            var match = existing.FirstOrDefault(r => !kept.Contains(r.Id) && MatchesKey(r, entity.SourceKey))
                ?? existing.FirstOrDefault(r => !kept.Contains(r.Id) && string.Equals(r.RelatedClient!.LegalName, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var related = new Client
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    ClientNumber = await _clientNumbers.GenerateAsync(cancellationToken),
                    Status = ClientStatus.Prospect,
                    LegalName = name,
                    PrimaryEmail = Clean(entity.EmailAddress),
                    PrimaryPhone = Clean(entity.PhoneNumber),
                };
                await _clients.AddAsync(related, cancellationToken);
                match = new ClientRelationship
                {
                    Id = Guid.NewGuid(), TenantId = request.TenantId, ClientId = client.Id, RelatedClientId = related.Id,
                    RelationTypeId = relationTypeId, RelatedClient = related,
                };
                client.Relationships.Add(match);
                await _clients.AddRelationshipAsync(match, cancellationToken);
            }
            else
            {
                var related = match.RelatedClient!;
                related.LegalName = name;
                related.PrimaryEmail = Clean(entity.EmailAddress);
                related.PrimaryPhone = Clean(entity.PhoneNumber);
                _clients.Update(related);
            }
            kept.Add(match.Id);
        }
        foreach (var gone in existing.Where(r => !kept.Contains(r.Id)))
        {
            client.Relationships.Remove(gone);
            _clients.RemoveRelationship(gone);
        }
    }

    private async Task UpsertContractAsync(RemsRequest request, Client client, RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var any = payload.ContractStartDate is not null || payload.ContractEndDate is not null
            || Clean(payload.OriginalTerm) is not null || Clean(payload.RenewalTerms) is not null
            || payload.PoStartDate is not null || payload.PoEndDate is not null;
        var contract = request.Contract ?? client.Contracts.FirstOrDefault(c => !c.Deleted && c.IsCurrent);
        if (!any)
        {
            return;
        }
        if (contract is null)
        {
            contract = new ClientContract { Id = Guid.NewGuid(), TenantId = request.TenantId, ClientId = client.Id, IsCurrent = true };
            foreach (var other in client.Contracts.Where(c => !c.Deleted && c.IsCurrent))
            {
                other.IsCurrent = false;
                _clients.UpdateContract(other);
            }
            client.Contracts.Add(contract);
            await _clients.AddContractAsync(contract, cancellationToken);
            request.ContractId = contract.Id;
            request.Contract = contract;
        }
        contract.ContractStartDate = payload.ContractStartDate;
        contract.ContractEndDate = payload.ContractEndDate;
        contract.OriginalTerm = Clean(payload.OriginalTerm);
        contract.RenewalTerms = Clean(payload.RenewalTerms);
        contract.PurchaseOrderStartDate = payload.PoStartDate;
        contract.PurchaseOrderEndDate = payload.PoEndDate;
        _clients.UpdateContract(contract);
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private static IEnumerable<ClientAddress> AddressesOfKind(Client client, string kind)
        => client.Addresses
            .Where(a => !a.Deleted && a.Address is not null && string.Equals(a.AddressType?.Value, kind, StringComparison.Ordinal))
            .OrderBy(a => a.CreatedOnUtc);

    private static ClientRelationship? MatchIndividual(List<ClientRelationship> existing, RemsAdditionalIndividualPayload individual, HashSet<Guid> kept)
    {
        var byKey = existing.FirstOrDefault(r => !kept.Contains(r.Id) && MatchesKey(r, individual.SourceKey));
        if (byKey is not null)
        {
            return byKey;
        }
        var first = Clean(individual.FirstName);
        var last = Clean(individual.LastName);
        return existing.FirstOrDefault(r => !kept.Contains(r.Id)
            && r.RelatedClient?.Person is { } p
            && string.Equals(p.FirstName, first ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.LastName, last ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesKey(ClientRelationship relationship, string? sourceKey)
        => !string.IsNullOrWhiteSpace(sourceKey)
            && (string.Equals(relationship.Id.ToString("N"), sourceKey.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(relationship.Id.ToString(), sourceKey.Trim(), StringComparison.OrdinalIgnoreCase));

    private static RemsAddressPayload AddressPayload(Address address) => new()
    {
        Street = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        City = address.CityName,
        State = address.StateName,
        StateCode = address.StateCode,
        Zip = address.PostalCode,
        CountryCode = address.CountryCode,
        CountryName = address.CountryName,
        Suffix = address.Suffix,
        FirstName = address.FirstName,
        LastName = address.LastName,
        Email = address.Email,
        Phone = address.PhoneNumber,
    };

    private static bool SameAddress(Address a, Address b)
        => string.Equals(a.AddressLine1, b.AddressLine1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.AddressLine2, b.AddressLine2, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.CityName, b.CityName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.StateCode, b.StateCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.PostalCode, b.PostalCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.CountryCode, b.CountryCode, StringComparison.OrdinalIgnoreCase);

    private static void Fill(Address address, RemsAddressPayload payload)
    {
        address.AddressLine1 = Clean(payload.Street);
        address.AddressLine2 = Clean(payload.AddressLine2);
        address.CityName = Clean(payload.City);
        address.StateCode = Clean(payload.StateCode);
        address.StateName = Clean(payload.State);
        address.CountryCode = Clean(payload.CountryCode);
        address.CountryName = Clean(payload.CountryName);
        address.PostalCode = Clean(payload.Zip);
        address.Suffix = Clean(payload.Suffix);
        address.FirstName = Clean(payload.FirstName);
        address.LastName = Clean(payload.LastName);
        address.Email = Clean(payload.Email);
        address.PhoneNumber = Clean(payload.Phone);
    }

    private static RemsRolePayload RolePayload(Person? person) => new()
    {
        Suffix = person?.Suffix,
        FirstName = person?.FirstName,
        LastName = person?.LastName,
        Name = person?.DisplayName,
        Email = person?.PrimaryEmail,
        Phone = person?.MobileNumber,
    };

    private static Person NewPerson(RemsRequest request, RemsRolePayload role)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            TenantId = request.TenantId,
            SourceEntityType = EntityType.Rems,
            SourceEntityId = request.Id,
            IsActive = true,
        };
        FillPerson(person, role);
        return person;
    }

    private static void FillPerson(Person person, RemsRolePayload role)
    {
        person.Suffix = Clean(role.Suffix);
        person.FirstName = role.EffectiveFirstName;
        person.LastName = role.EffectiveLastName;
        person.DisplayName = Clean(role.NameWithSuffix) ?? role.EffectiveFirstName;
        person.PrimaryEmail = Clean(role.Email);
        person.MobileNumber = Clean(role.Phone);
        person.LastProfileUpdatedOn = DateTime.UtcNow;
    }

    private static void FillIndividual(Person person, RemsAdditionalIndividualPayload individual)
    {
        person.FirstName = Clean(individual.FirstName) ?? string.Empty;
        person.LastName = Clean(individual.LastName) ?? string.Empty;
        person.Suffix = Clean(individual.Suffix);
        person.DisplayName = Clean(individual.DisplayName) ?? string.Join(" ", new[] { person.FirstName, person.LastName }.Where(p => p.Length > 0));
        person.PrimaryEmail = Clean(individual.Email);
        person.MobileNumber = Clean(individual.Phone);
        person.LastProfileUpdatedOn = DateTime.UtcNow;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
