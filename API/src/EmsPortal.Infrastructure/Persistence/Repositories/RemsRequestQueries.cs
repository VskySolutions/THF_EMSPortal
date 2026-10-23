using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>Query pieces the request, approval and client repositories share.</summary>
internal static class RemsRequestQueries
{
    /// <summary>Everything the setup, submission and approval screens read, in one graph.</summary>
    public static IQueryable<RemsRequest> WorkspaceGraph(this IQueryable<RemsRequest> query)
        => query
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.Department)
            .Include(r => r.ServiceLine)
            .Include(r => r.JobTemplate)
            .Include(r => r.BillingPeriod)
            .Include(r => r.Contract)
            .Include(r => r.Files.Where(f => !f.Deleted)).ThenInclude(f => f.Media)
            .Include(r => r.PersonnelRates.Where(x => !x.Deleted)).ThenInclude(x => x.PersonnelLevel)
            .Include(r => r.MarketingMethods.Where(x => !x.Deleted)).ThenInclude(x => x.MarketingMethod)
            .Include(r => r.CommissionSplits.Where(x => !x.Deleted))
            .Include(r => r.TaxForms.Where(x => !x.Deleted)).ThenInclude(x => x.TaxForm)
            .Include(r => r.TaxDueDates.Where(x => !x.Deleted))
            .Include(r => r.Approvers.Where(x => !x.Deleted))
            .ClientGraph();

    /// <summary>The client's full graph on a request: person, option items, addresses, contacts, relationships, contracts.</summary>
    public static IQueryable<RemsRequest> ClientGraph(this IQueryable<RemsRequest> query)
        => query
            .Include(r => r.Client).ThenInclude(c => c!.Person)
            .Include(r => r.Client).ThenInclude(c => c!.EntityType)
            .Include(r => r.Client).ThenInclude(c => c!.Industry)
            .Include(r => r.Client).ThenInclude(c => c!.ReferralSource)
            .Include(r => r.Client).ThenInclude(c => c!.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.Address)
            .Include(r => r.Client).ThenInclude(c => c!.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.AddressType)
            .Include(r => r.Client).ThenInclude(c => c!.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.Person)
            .Include(r => r.Client).ThenInclude(c => c!.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.ContactRole)
            .Include(r => r.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedClient).ThenInclude(x => x!.Person)
            .Include(r => r.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelationType)
            .Include(r => r.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.FilingType)
            .Include(r => r.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.BillingPreference)
            .Include(r => r.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedStatus)
            .Include(r => r.Client).ThenInclude(c => c!.Contracts.Where(x => !x.Deleted));

    /// <summary>The client's full graph when the client itself is the root.</summary>
    public static IQueryable<Client> FullGraph(this IQueryable<Client> query)
        => query
            .Include(c => c.Person)
            .Include(c => c.EntityType)
            .Include(c => c.Industry)
            .Include(c => c.ReferralSource)
            .Include(c => c.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.Address)
            .Include(c => c.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.AddressType)
            .Include(c => c.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.Person)
            .Include(c => c.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.ContactRole)
            .Include(c => c.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedClient).ThenInclude(x => x!.Person)
            .Include(c => c.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelationType)
            .Include(c => c.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.FilingType)
            .Include(c => c.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.BillingPreference)
            .Include(c => c.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedStatus)
            .Include(c => c.Contracts.Where(x => !x.Deleted));

    /// <summary>Requests whose client name, or a Maconomy customer matched by name, contains the term.</summary>
    public static IQueryable<RemsRequest> WhereClientNameMatches(this IQueryable<RemsRequest> query, string term, IReadOnlyList<string> maconomyNumbers)
        => query.Where(r =>
            (r.Client!.Person != null
                && (r.Client.Person.ClientDisplayName.Contains(term)
                    || r.Client.Person.FirstName.Contains(term)
                    || r.Client.Person.LastName.Contains(term)))
            || (r.Client!.LegalName != null && r.Client.LegalName.Contains(term))
            || (r.Client!.MaconomyCustomerNumber != null && maconomyNumbers.Contains(r.Client.MaconomyCustomerNumber)));

    /// <summary>Requests whose client email or phone, or a Maconomy customer matched by contact, contains the term.</summary>
    public static IQueryable<RemsRequest> WhereClientContactMatches(this IQueryable<RemsRequest> query, string term, IReadOnlyList<string> maconomyNumbers)
        => query.Where(r =>
            (r.Client!.PrimaryEmail != null && r.Client.PrimaryEmail.Contains(term))
            || (r.Client!.PrimaryPhone != null && r.Client.PrimaryPhone.Contains(term))
            || (r.Client!.MaconomyCustomerNumber != null && maconomyNumbers.Contains(r.Client.MaconomyCustomerNumber)));

    public static IQueryable<RemsRequest> WhereStatus(this IQueryable<RemsRequest> query, string code)
    {
        var wanted = code.Trim();
        if (wanted.Equals(RemsRequestStatuses.WaitingForPickup, StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(r => r.AdminAssignedToId == null
                && (r.Status!.Value == RemsRequestStatuses.AdminReview
                    || r.Status!.Value == RemsRequestStatuses.AwaitingAdminConfirmation));
        }
        return RemsRequestStatuses.IsWithAdmin(wanted)
            ? query.Where(r => r.Status!.Value == wanted && r.AdminAssignedToId != null)
            : query.Where(r => r.Status!.Value == wanted);
    }

    /// <summary>The form state the old model stored, as a filter on the invite columns.</summary>
    public static IQueryable<RemsRequest> WhereFormState(this IQueryable<RemsRequest> query, string state)
    {
        var wanted = state.Trim();
        if (wanted.Equals("NotStarted", StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(r => r.InviteSentOnUtc == null && r.FormSubmittedOnUtc == null);
        }
        if (!Enum.TryParse<RemsFormStatus>(wanted, true, out var formStatus))
        {
            return query.Where(r => false);
        }
        return formStatus switch
        {
            RemsFormStatus.Submitted => query.Where(r => r.FormSubmittedOnUtc != null),
            RemsFormStatus.Sent => query.Where(r => r.InviteSentOnUtc != null && r.FormSubmittedOnUtc == null),
            RemsFormStatus.Saved or RemsFormStatus.Draft => query.Where(r => r.InviteCode != null && r.InviteSentOnUtc == null),
            _ => query.Where(r => false),
        };
    }

    public static IQueryable<RemsRequest> WhereClientSubmissionState(this IQueryable<RemsRequest> query, string state)
    {
        var wanted = state.Trim();
        if (wanted.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(r => r.FormSubmittedOnUtc != null);
        }
        if (wanted.Equals("AwaitingCustomer", StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(r => r.InviteSentOnUtc != null && r.FormSubmittedOnUtc == null);
        }
        return query.Where(r => false);
    }

    public static string NameOf(Client client)
        => client.Person?.ClientDisplayName is { Length: > 0 } personName ? personName : client.LegalName ?? string.Empty;
}
