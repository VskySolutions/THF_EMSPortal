using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of an approved engagement to Maconomy's job card. Everything that names a job card field,
/// or decides what goes in it, is in this file and nowhere else: change what the portal sends here.
/// The field names are lower-cased JobHeader columns from documentation, not yet confirmed against the
/// instance (31 Oct 2026); the probe in the plan doc fixes them, in this one place. The container is
/// the one thing that differs per instance, so the sync service overrides it from configuration.
/// </summary>
public static class MaconomyJobDataMapping
{
    public const string DefaultContainer = "jobs";

    public const string NumberField = "jobnumber";
    public const string NameField = "jobname";
    public const string CustomerField = "customernumber";
    public const string TemplateField = "templatejobnumber";
    public const string ProjectManagerField = "projectmanagernumber";
    public const string LocationField = "locationname";
    public const string PurchaseOrderField = "text3";

    // Whether the template job goes on the init step, so Maconomy derives the template's defaults into
    // the record it hands back, or on the create with everything else. The probe decides.
    private const bool TemplateOnInit = false;

    /// <summary>The card fields the write names. The number is read back, never sent: Maconomy's series assigns it.</summary>
    public static readonly string[] Fields =
    {
        NumberField, NameField, CustomerField, TemplateField, ProjectManagerField, LocationField, PurchaseOrderField,
    };

    /// <summary>
    /// The mapping in words, for the Job tab. Kept beside <see cref="Map"/> so a change to what is sent
    /// is a change to what the page says.
    /// </summary>
    public static MaconomyCardMapping Describe(string container) => new(container, new[]
    {
        new MaconomyFieldMapping(
            NumberField, "Job number", "Maconomy's number series",
            "Never sent. Written back onto the request once the job is created.", ReadBack: true),
        new MaconomyFieldMapping(
            NameField, "Job name", "Client name.Service Line[.Fiscal year.Tax form]",
            "A person as \"Last First\", an organisation by its legal name, a Maconomy-picked client as Maconomy names it; then a dot and the Service Line label; an engagement with a tax form adds the fiscal year (yyyy) and the form, so one client's returns are different jobs.", ReadBack: false),
        new MaconomyFieldMapping(
            CustomerField, "Customer number", "The client's Maconomy customer number",
            "From the customer sync, or the Maconomy customer picked on the request. Without one the request is not eligible.", ReadBack: false),
        new MaconomyFieldMapping(
            TemplateField, "Template job", "The Job Type's Maconomy number, else the default template job",
            "Set per value under Option Sets → REMS Job Type; the default is server configuration (Maconomy:JobCard).", ReadBack: false),
        new MaconomyFieldMapping(
            ProjectManagerField, "Project manager", "The configured employee number",
            "Server configuration (Maconomy:JobCard); every job names the same one.", ReadBack: false),
        new MaconomyFieldMapping(
            LocationField, "Location", "The Department's Maconomy number",
            "Set per value under Option Sets → REMS Department. A department without one is left out, so the template job's location stands.", ReadBack: false),
        new MaconomyFieldMapping(
            PurchaseOrderField, "Text 3", "The engagement's Purchase Order number",
            "As entered on the Engagement Setup. Blank is left out.", ReadBack: false),
    });

    /// <summary>
    /// What the portal posts as a new job: a header against a template job, nothing below it. Total: it
    /// maps whatever is there, blanks included, so the confirmation can show a request that is not yet
    /// eligible; eligibility is <see cref="MaconomyJobSyncRules"/>' business, checked before the writer is called.
    /// The location and the purchase order are optional: left out when blank, so the template job's stand.
    /// </summary>
    public static MaconomyCardWrite Map(RemsRequest request, MaconomyJobSyncContext context)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CustomerField] = request.Client?.MaconomyCustomerNumber?.Trim(),
            [ProjectManagerField] = context.ProjectManagerNumber?.Trim(),
            [NameField] = JobNameOf(request),
        };
        Put(values, LocationField, ResolveLocation(request, context));
        Put(values, PurchaseOrderField, request.PurchaseOrderNumber);
        var template = ResolveTemplate(request, context);
        Dictionary<string, string?>? init = null;
        if (TemplateOnInit)
        {
            init = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [TemplateField] = template };
        }
        else
        {
            values[TemplateField] = template;
        }
        return new MaconomyCardWrite(DefaultContainer, Fields, values, init, KeyField: NumberField);
    }

    /// <summary>The template job for the request: the map's row for its Job Type, else the tenant's default.</summary>
    public static string? ResolveTemplate(RemsRequest request, MaconomyJobSyncContext context)
    {
        if (request.JobTypeId is { } template && context.TemplateMap.TryGetValue(template, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }
        return string.IsNullOrWhiteSpace(context.DefaultTemplateNumber) ? null : context.DefaultTemplateNumber.Trim();
    }

    /// <summary>The Maconomy location for the request: the map's row for its Department, or null while the department carries no number.</summary>
    public static string? ResolveLocation(RemsRequest request, MaconomyJobSyncContext context)
        => request.DepartmentId is { } department && context.LocationMap.TryGetValue(department, out var number) && !string.IsNullOrWhiteSpace(number)
            ? number.Trim()
            : null;

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }

    /// <summary>
    /// "{client name}.{Service Line label}", the format the user set on 31 Oct 2026. An engagement with a tax
    /// form adds ".{fiscal year yyyy}.{tax form label}" (5 Nov 2026): one form per REMS, so the year and the
    /// form are what tell a client's returns apart. Needs the service line and the tax forms loaded.
    /// </summary>
    public static string JobNameOf(RemsRequest request)
    {
        var client = request.Client is { } c ? ClientNameOf(c) : string.Empty;
        var parts = new List<string?> { request.ServiceLine?.Label?.Trim() };
        var form = request.TaxForms.FirstOrDefault(f => !f.Deleted)?.TaxForm?.Label?.Trim();
        if (!string.IsNullOrEmpty(form))
        {
            parts.Add(request.FiscalYearEnd?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(form);
        }
        return string.Join(".", new[] { client }.Concat(parts.Where(p => !string.IsNullOrEmpty(p))));
    }

    /// <summary>A person as "LastName FirstName", an organisation as its name; a Maconomy-picked client as Maconomy names it.</summary>
    public static string ClientNameOf(Client client)
    {
        if (client.Person is { } person && !string.IsNullOrWhiteSpace(person.LastName))
        {
            return string.Join(" ", new[] { person.LastName.Trim(), person.FirstName?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
        }
        if (!string.IsNullOrWhiteSpace(client.LegalName))
        {
            return client.LegalName.Trim();
        }
        return client.ClientName.Trim();
    }

    /// <summary>The job as Maconomy created it, the assigned number included.</summary>
    public static MaconomyCreatedJob FromCreated(IReadOnlyDictionary<string, string?> record, MaconomyCardWrite sent)
    {
        var sentName = sent.Values.TryGetValue(NameField, out var n) ? n ?? string.Empty : string.Empty;
        var number = Value(record, NumberField);
        if (number.Length == 0)
        {
            throw new MaconomyException(
                MaconomyFailure.Rejected,
                $"Maconomy created the job but sent no {NumberField} back. Look for a new job named \"{sentName}\" in Maconomy before syncing again.");
        }
        var name = Value(record, NameField);
        return new MaconomyCreatedJob(number, name.Length > 0 ? name : sentName);
    }

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
}
