using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// Projects a request's workspace graph (WO-114) into its read models. The request is one row now, so the
/// engagement view is read off the request and the client's rows; the shapes handed out do not change.
/// </summary>
internal static class RemsWorkspaceMapper
{
    public static RemsUserRef? UserRef(Guid? id, IReadOnlyDictionary<Guid, string> names)
        => id is { } uid ? new RemsUserRef(uid, names.TryGetValue(uid, out var n) ? n : string.Empty) : null;

    /// <summary>
    /// The request's EMS-form state as the UI reads it: the form's own status, and whether the client
    /// has submitted / is still being waited on.
    /// </summary>
    public static (string EmsFormState, string? ClientSubmissionState) FormState(RemsFormStateInfo? form)
    {
        if (form is null)
        {
            return ("NotStarted", null);
        }
        // Draft and Saved both mean the same thing to a reader: nothing has gone to the client yet, so both
        // report as Not started and this column changes only once the form is actually Sent.
        var ems = form.FormStatus switch
        {
            null or RemsFormStatus.Draft or RemsFormStatus.Saved => "NotStarted",
            var status => status.ToString(),
        };
        var submission = form.HasSubmission || form.FormSubmittedOnUtc is not null
            ? "Submitted"
            : form.FormSentOnUtc is not null ? "AwaitingCustomer" : null;
        return (ems, submission);
    }

    public static RemsFormStateInfo FormStateOf(RemsRequest request)
        => new(
            request.Id, request.Client?.EntityType?.Value, RemsDerived.FormStatusOf(request),
            request.InviteSentOnUtc, request.FormSubmittedOnUtc, request.FormSubmittedOnUtc is not null, request.InviteCode);

    public static RemsAddressView? Address(Address? address)
        => address is null
            ? null
            : new RemsAddressView(
                address.Id, address.AddressLine1, address.AddressLine2, address.CityName, address.StateName,
                address.StateCode, address.PostalCode, address.CountryCode, address.CountryName,
                // Who the post is addressed to, carried on the address itself. Null on a physical or
                // mailing row; filled on a billing one, where the intake form asks for both halves.
                address.Suffix, address.FirstName, address.LastName, address.Email, address.PhoneNumber);

    /// <summary>
    /// A GCS engagement's rate card, in the personnel-level list's own order. With
    /// <paramref name="withRates"/> false the levels are named and the money withheld — the approver's
    /// packet for a role that may not see the fee.
    /// </summary>
    public static IReadOnlyList<RemsPersonnelRateView> PersonnelRates(RemsRequest request, bool withRates = true)
        => request.PersonnelRates
            .Where(r => !r.Deleted)
            .OrderBy(r => r.PersonnelLevel?.SortOrder ?? int.MaxValue)
            .ThenBy(r => r.PersonnelLevel?.Label)
            .Select(r => new RemsPersonnelRateView(
                r.PersonnelLevel?.Value ?? string.Empty,
                r.PersonnelLevel?.Label ?? string.Empty,
                withRates ? r.BillRatePerHour : null))
            .ToList();

    public static RemsFile? FileOfKind(RemsRequest request, RemsFileKind kind)
        => request.Files.Where(f => !f.Deleted && f.Kind == kind).OrderByDescending(f => f.CreatedOnUtc).FirstOrDefault();

    /// <summary>The engagement setup as the UI reads it. Its id is the request's id.</summary>
    public static RemsEngagementView Engagement(RemsRequest request, IReadOnlyDictionary<Guid, string> names)
    {
        var client = request.Client;
        var marketing = request.MarketingMethods.Where(m => !m.Deleted).Select(m => m.MarketingMethodId).ToList();
        var commission = request.CommissionSplits
            .Where(s => !s.Deleted)
            .Select(s => new RemsCommissionSplitView(s.Id, UserRef(s.EmployeeId, names)!, s.CommissionPercentage))
            .ToList();
        var acceptance = FileOfKind(request, RemsFileKind.ClientAcceptanceForm);
        var purchaseOrder = FileOfKind(request, RemsFileKind.PurchaseOrder);
        var department = request.Department?.Value;

        var auditView = RemsEngagementCodes.RequiresClientAcceptanceForm(department) || acceptance is not null || client?.FiscalYearEnd is not null
            ? new RemsAuditDetailView(request.Id, acceptance?.MediaId, acceptance?.Media?.OriginalFileName, client?.FiscalYearEnd)
            : null;
        var govView = RemsEngagementCodes.IsGcs(department) || request.ContractId is not null || request.ContractNumber is not null
            || request.PurchaseOrderNumber is not null || request.PersonnelRates.Any(r => !r.Deleted)
            ? new RemsGovernmentDetailView(
                request.Id, request.ContractNumber, request.IsFloridaOnePercentFeeApplicable,
                request.Contract?.ContractStartDate, request.Contract?.ContractEndDate, request.Contract?.OriginalTerm, request.Contract?.RenewalTerms,
                request.Contract?.PurchaseOrderStartDate, request.Contract?.PurchaseOrderEndDate,
                request.PurchaseOrderNumber, request.PurchaseOrderAmount,
                purchaseOrder?.MediaId, purchaseOrder?.Media?.OriginalFileName,
                PersonnelRates(request))
            : null;
        var taxView = RemsEngagementCodes.IsTax(department) || request.TaxForms.Any(f => !f.Deleted) || request.TaxDueDates.Any(d => !d.Deleted)
            ? TaxDetail(request)
            : null;

        return new RemsEngagementView(
            request.Id,
            department,
            request.ServiceLine?.Value,
            request.JobTemplate?.Value,
            client?.Industry?.Value,
            UserRef(request.DepartmentDirectorId, names),
            UserRef(request.EngagementExecutiveId, names),
            UserRef(request.BillingManagerId, names),
            request.FirstYearFeeEstimate,
            request.EngagementFee,
            request.RealizationPercentage,
            request.BillingPeriod?.Value,
            request.BillingProcessDescription,
            RemsDerived.EngagementStatusOf(request).ToString(),
            marketing,
            commission,
            auditView,
            govView,
            taxView);
    }

    /// <summary>
    /// The tax detail as the UI has always read it: the two editable dates are the overridden rows, and the
    /// schedule JSON is rebuilt from the rows so the screen keeps parsing what it parsed before.
    /// </summary>
    public static RemsTaxDetailView TaxDetail(RemsRequest request)
    {
        var fye = request.Client?.FiscalYearEnd;
        var original = request.TaxDueDates.FirstOrDefault(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.Original);
        var extension = request.TaxDueDates.FirstOrDefault(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.FirstExtension);
        string? schedule = null;
        if (fye is { } fiscalYearEnd)
        {
            schedule = RemsTaxDueDates.EffectiveJson(
                fiscalYearEnd,
                original?.DueOn,
                extension?.DueOn);
        }
        return new RemsTaxDetailView(
            request.Id, fye,
            original is { IsOverridden: true } ? original.DueOn : null,
            extension is { IsOverridden: true } ? extension.DueOn : null,
            schedule,
            request.TaxForms.Where(f => !f.Deleted).Select(f => f.TaxFormId).ToList());
    }

    /// <summary>The client as the setup screen edits it. Billing name and email come from the first billing contact.</summary>
    public static RemsClientView ClientView(Client client)
    {
        var billing = BillingContact(client);
        return new RemsClientView(
            client.Id, client.ClientName, client.ClientEmail ?? string.Empty, client.ClientPhone,
            client.ReferralSource?.Value,
            billing?.Person?.DisplayName, billing?.Person?.PrimaryEmail);
    }

    public static ClientContact? BillingContact(Client client)
        => client.Contacts
            .Where(c => !c.Deleted && string.Equals(c.ContactRole?.Value, RemsContactRoles.BillingContactCode, StringComparison.Ordinal))
            .OrderBy(c => c.CreatedOnUtc)
            .FirstOrDefault();

    public static RemsEntityAddressView EntityAddress(ClientAddress address)
        => new(address.Id, RemsAddressTypes.NameOf(address.AddressType?.Value), Address(address.Address)!);

    public static RemsEntityContactView EntityContact(ClientContact contact, string? entityTypeCode)
    {
        var role = RemsContactRoles.NameOf(contact.ContactRole?.Value);
        return new RemsEntityContactView(
            contact.Id, role, RemsContactRoles.IsRequired(entityTypeCode, role),
            contact.Person?.DisplayName, contact.Person?.PrimaryEmail, contact.Person?.MobileNumber, contact.Person?.Suffix);
    }

    /// <summary>
    /// The client as one entity: the old model's main entity was always the client itself, so the entity
    /// list the UI reads has one row whose id is the client's id.
    /// </summary>
    public static IReadOnlyList<RemsEntityView> Entities(Client client)
    {
        var entityType = client.EntityType?.Value;
        var addresses = client.Addresses.Where(a => !a.Deleted).OrderBy(a => a.AddressType?.SortOrder).ThenBy(a => a.CreatedOnUtc).Select(EntityAddress).ToList();
        var contacts = client.Contacts.Where(c => !c.Deleted).OrderBy(c => c.ContactRole?.SortOrder).ThenBy(c => c.CreatedOnUtc)
            .Select(c => EntityContact(c, entityType)).ToList();
        return new[] { new RemsEntityView(client.Id, client.ClientName, client.EIN, true, addresses, contacts) };
    }

    /// <summary>The related businesses declared on the client, with the request raised for each where one exists.</summary>
    public static IReadOnlyList<RemsAdditionalEntityView> AdditionalEntities(Client client, IReadOnlyDictionary<Guid, (Guid RemsId, string RemsNumber)> raisedFor)
        => client.Relationships
            .Where(r => !r.Deleted && r.RelatedClient is { IsOrganisation: true })
            .OrderBy(r => r.CreatedOnUtc)
            .Select(r =>
            {
                raisedFor.TryGetValue(r.RelatedClientId, out var raised);
                return new RemsAdditionalEntityView(
                    r.Id, r.RelatedClient!.ClientName, r.RelatedClient.ClientEmail, r.RelatedClient.ClientPhone,
                    raised.RemsId == Guid.Empty ? null : raised.RemsId,
                    raised.RemsId == Guid.Empty ? null : raised.RemsNumber);
            })
            .ToList();

    /// <summary>The workspace for a request: its client, that client as the one entity, and the setup.</summary>
    public static RemsEngagementWorkspace Workspace(
        RemsRequest request,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyList<RemsAdditionalEntityView> additionalEntities,
        IReadOnlyList<RemsDepartmentDirectorView> departmentDirectors)
    {
        var client = request.Client;
        return new RemsEngagementWorkspace(
            request.Id, request.RemsNumber, request.Status!.Value,
            client is null ? null : ClientView(client),
            client is null ? Array.Empty<RemsEntityView>() : Entities(client),
            Engagement(request, names),
            client?.EntityType?.Value, additionalEntities, departmentDirectors);
    }
}
