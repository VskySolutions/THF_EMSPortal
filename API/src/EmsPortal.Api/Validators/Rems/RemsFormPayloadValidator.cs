using System.Net.Mail;
using EmsPortal.Api.Models.Rems;
using FluentValidation.Results;

namespace EmsPortal.Api.Validators.Rems;

/// <summary>
/// Validates a <see cref="RemsFormPayloadV1"/> against the FULL entity-type rules
/// (AC-REMS-011.1/2/3, AC-REMS-024.8).
/// </summary>
public sealed class RemsFormPayloadValidator
{
    public const string Individual = "individual";
    public const string Government = "government";

    /// <summary>How many places a client may be invoiced at.</summary>
    public const int MaxBillingAddresses = 10;

    /// <summary>How many other people one individual client may declare on their return.</summary>
    public const int MaxAdditionalIndividuals = 10;

    // The business FAMILY: the kinds of business THF onboards are asked for exactly the same things.
    public const string Business = "business";
    public const string NotForProfit = "not_for_profit";
    public const string Insurance = "insurance";
    public const string Commercial = "commercial";

    /// <summary>A trust or a decedent's estate.</summary>
    public const string TrustEstate = "trust_estate";

    private static readonly HashSet<string> BusinessGroups =
        new(StringComparer.Ordinal) { Business, NotForProfit, Insurance, Commercial, TrustEstate };

    /// <summary>True for any industry group that asks the business questions.</summary>
    public static bool IsBusinessGroup(string? entityType)
        => entityType is not null && BusinessGroups.Contains(entityType);

    public ValidationResult Validate(RemsFormPayloadV1? payload, string entityType)
    {
        var failures = new List<ValidationFailure>();

        if (payload is null)
        {
            failures.Add(new ValidationFailure("payload", "No form data was supplied."));
            return new ValidationResult(failures);
        }

        // ---- Common ----
        // An individual is a person and is asked for a first and a last name; a business or government body is
        // asked for the one name it has.
        if (entityType == Individual)
        {
            RequireField(failures, "clientFirstName", payload.ClientFirstName, "First name is required.");
            RequireField(failures, "clientLastName", payload.ClientLastName, "Last name is required.");
            // …and what IS typed has to read as a name. An individual client becomes a Person record under
            // exactly these two boxes. See PersonNames, which the browser mirrors.
            RequireName(failures, "clientFirstName", payload.ClientFirstName, "First name");
            RequireName(failures, "clientLastName", payload.ClientLastName, "Last name");
        }
        else if (string.IsNullOrWhiteSpace(payload.ClientName))
        {
            failures.Add(new ValidationFailure("clientName", "Client name is required."));
        }

        // The physical address always.
        RequireAddress(failures, "physicalAddress", payload.PhysicalAddress);
        if (!payload.MailingSameAsPhysical)
        {
            RequireAddress(failures, "mailingAddress", payload.MailingAddress);
        }

        // Billing: who each invoice is for, and where it goes.
        if (payload.BillingAddresses.Count > MaxBillingAddresses)
        {
            failures.Add(new ValidationFailure(
                "billingAddresses", $"Give at most {MaxBillingAddresses} billing blocks."));
        }

        if (payload.EffectiveBillingAddresses.Count == 0)
        {
            failures.Add(new ValidationFailure(
                "billingAddresses",
                "Billing information is required — give a name, an email address and an address for the invoice."));
        }

        for (var i = 0; i < payload.BillingAddresses.Count; i++)
        {
            ValidateBillingAddress(failures, $"billingAddresses[{i}]", payload.BillingAddresses[i]);
        }

        // Optional like the rest of the spouse block, but checked when given — a mistyped address is
        // worse than a blank one, since nobody finds out until someone tries to use it.
        if (!string.IsNullOrWhiteSpace(payload.SpouseEmail) && !IsEmail(payload.SpouseEmail))
        {
            failures.Add(new ValidationFailure("spouseEmail", "Spouse email is not a valid email address."));
        }

        // ---- Entity-type role rules ----
        // Normalized, so a payload written before the business roles were renamed is validated on the answers
        // it actually carries rather than failing for three contacts it gave under the old keys.
        var roles = payload.EffectiveRoles;
        // if/else rather than a switch: the business branch matches a FAMILY of codes, not one literal.
        if (entityType == Individual)
        {
            // No contact roles at all.
            if (payload.AdditionalIndividuals.Count > MaxAdditionalIndividuals)
            {
                failures.Add(new ValidationFailure(
                    "additionalIndividuals", $"Give at most {MaxAdditionalIndividuals} people here."));
            }

            for (var i = 0; i < payload.AdditionalIndividuals.Count; i++)
            {
                var individual = payload.AdditionalIndividuals[i];
                // A block somebody opened and left empty is a change of mind, not an answer — the browser
                // drops those on the way out, and one that arrives anyway is not worth nine complaints.
                if (individual is not { HasAny: true })
                {
                    continue;
                }

                ValidateAdditionalIndividual(failures, $"additionalIndividuals[{i}]", individual);
            }
        }
        else if (IsBusinessGroup(entityType))
        {
            if (string.IsNullOrWhiteSpace(payload.Ein))
            {
                failures.Add(new ValidationFailure("ein", "EIN is required for a business."));
            }

            RequireRole(failures, "roles.primaryContact", roles.PrimaryContact);
            RequireRole(failures, "roles.financialContact", roles.FinancialContact);
            OptionalRole(failures, "roles.otherContact", roles.OtherContact);
            // Trust and Estate only, and optional — but still shape-checked. Unconditional like the
            // retired roles: an answer already given survives a change of entity type.
            OptionalRole(failures, "roles.trustEstateContact", roles.TrustEstateContact);
        }
        else if (entityType == Government)
        {
            RequireRole(failures, "roles.financeDirector", roles.FinanceDirector);
            OptionalRole(failures, "roles.otherContact", roles.OtherContact);
        }
        else
        {
            failures.Add(new ValidationFailure("entityType", $"Unsupported entity type '{entityType}'."));
        }

        // ---- Billing contacts ----
        // Neither `roles.billingContact` nor `additionalBillingContacts` is validated any more.

        // ---- Additional entities ----
        // Each row is another of the client's businesses for the firm to set up separately.
        if (entityType != Individual)
        {
            for (var i = 0; i < payload.RelatedEntities.Count; i++)
            {
                var related = payload.RelatedEntities[i];
                RequireField(
                    failures, $"relatedEntities[{i}].fullName", related.FullName,
                    "A client / entity name is required for each additional entity.");

                if (string.IsNullOrWhiteSpace(related.EmailAddress))
                {
                    failures.Add(new ValidationFailure(
                        $"relatedEntities[{i}].emailAddress", "An email address is required for each additional entity."));
                }
                else if (!IsEmail(related.EmailAddress))
                {
                    failures.Add(new ValidationFailure(
                        $"relatedEntities[{i}].emailAddress", "Email is not a valid email address."));
                }
            }
        }

        return new ValidationResult(failures);
    }

    /// <summary>
    /// A required address must carry the whole standard block except line 2: country, state, city,
    /// address line 1 and zip code.
    /// </summary>
    private static void RequireAddress(List<ValidationFailure> failures, string prefix, RemsAddressPayload? address)
    {
        if (address is null)
        {
            failures.Add(new ValidationFailure(
                prefix, "A complete address is required (country, state, city, address line 1, zip code)."));
            return;
        }

        // Country is keyed on the ISO code, not the display name — that is what the client's cascade binds
        // and what the state / city lists below it are resolved from.
        RequireField(failures, $"{prefix}.countryCode", address.CountryCode, "Country is required.");
        RequireField(failures, $"{prefix}.state", address.State, "State / Province is required.");
        RequireField(failures, $"{prefix}.city", address.City, "City is required.");
        RequireField(failures, $"{prefix}.street", address.Street, "Address Line 1 is required.");
        RequireField(failures, $"{prefix}.zip", address.Zip, "Zip Code is required.");
    }

    /// <summary>A billing block the client has started.</summary>
    private static void ValidateBillingAddress(
        List<ValidationFailure> failures, string prefix, RemsAddressPayload? address)
    {
        if (address is null || !address.HasAnyContent)
        {
            return;
        }

        RequireAddress(failures, prefix, address);

        RequireField(failures, $"{prefix}.firstName", address.FirstName, "First name is required.");
        RequireField(failures, $"{prefix}.lastName", address.LastName, "Last name is required.");
        RequireName(failures, $"{prefix}.firstName", address.FirstName, "First name");
        RequireName(failures, $"{prefix}.lastName", address.LastName, "Last name");

        if (string.IsNullOrWhiteSpace(address.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email Address is required."));
        }
        else if (!IsEmail(address.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email is not a valid email address."));
        }
    }

    /// <summary>One of the other people on an individual's return.</summary>
    private static void ValidateAdditionalIndividual(
        List<ValidationFailure> failures, string prefix, RemsAdditionalIndividualPayload individual)
    {
        RequireField(failures, $"{prefix}.type", individual.Type, "A type is required (spouse, child or other).");
        RequireField(
            failures, $"{prefix}.filingType", individual.FilingType,
            "A filing type is required (joint or individual).");
        RequireField(failures, $"{prefix}.firstName", individual.FirstName, "First name is required.");
        RequireField(failures, $"{prefix}.lastName", individual.LastName, "Last name is required.");
        RequireName(failures, $"{prefix}.firstName", individual.FirstName, "First name");
        RequireName(failures, $"{prefix}.lastName", individual.LastName, "Last name");
        RequireField(
            failures, $"{prefix}.billingPreference", individual.BillingPreference,
            "A billing preference is required.");

        if (string.IsNullOrWhiteSpace(individual.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email Address is required."));
        }
        else if (!IsEmail(individual.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email is not a valid email address."));
        }

        // Billing NAMES are no longer required, because the form no longer asks for them: the invoice for this
        // person's return is addressed to this person.
        RequireName(failures, $"{prefix}.billingFirstName", individual.BillingFirstName, "Billing first name");
        RequireName(failures, $"{prefix}.billingLastName", individual.BillingLastName, "Billing last name");
    }

    /// <summary>A required role must carry a first name, a last name and a valid email; the phone is optional.</summary>
    private static void RequireRole(List<ValidationFailure> failures, string prefix, RemsRolePayload? role)
    {
        if (role is null || !role.HasAny)
        {
            failures.Add(new ValidationFailure(prefix, "This contact is required (name and email)."));
            return;
        }

        ValidateRoleFields(failures, prefix, role);
    }

    /// <summary>
    /// An optional role is unvalidated when omitted, but must be complete once the client starts
    /// filling it in.
    /// </summary>
    private static void OptionalRole(List<ValidationFailure> failures, string prefix, RemsRolePayload? role)
    {
        if (role is not null && role.HasAny)
        {
            ValidateRoleFields(failures, prefix, role);
        }
    }

    /// <summary>A contact is a first name, a last name and a valid email.</summary>
    private static void ValidateRoleFields(List<ValidationFailure> failures, string prefix, RemsRolePayload role)
    {
        var preSplit = string.IsNullOrWhiteSpace(role.FirstName)
            && string.IsNullOrWhiteSpace(role.LastName)
            && !string.IsNullOrWhiteSpace(role.Name);
        if (!preSplit)
        {
            RequireField(failures, $"{prefix}.firstName", role.FirstName, "First name is required.");
            RequireField(failures, $"{prefix}.lastName", role.LastName, "Last name is required.");
            RequireName(failures, $"{prefix}.firstName", role.FirstName, "First name");
            RequireName(failures, $"{prefix}.lastName", role.LastName, "Last name");
        }

        if (string.IsNullOrWhiteSpace(role.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email is required."));
        }
        else if (!IsEmail(role.Email))
        {
            failures.Add(new ValidationFailure($"{prefix}.email", "Email is not a valid email address."));
        }
    }

    private static void RequireField(List<ValidationFailure> failures, string property, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new ValidationFailure(property, message));
        }
    }

    /// <summary>The value must read as a person's name where one was given.</summary>
    private static void RequireName(List<ValidationFailure> failures, string property, string? value, string label)
    {
        if (PersonNames.Issue(value, label) is { } issue)
        {
            failures.Add(new ValidationFailure(property, issue));
        }
    }

    private static bool IsEmail(string value) => MailAddress.TryCreate(value.Trim(), out _);
}
