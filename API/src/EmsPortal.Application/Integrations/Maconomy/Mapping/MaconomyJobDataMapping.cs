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

    // Whether the template job goes on the init step, so Maconomy derives the template's defaults into
    // the record it hands back, or on the create with everything else. The probe decides.
    private const bool TemplateOnInit = false;

    /// <summary>The card fields the write names. The number is read back, never sent: Maconomy's series assigns it.</summary>
    public static readonly string[] Fields = { NumberField, NameField, CustomerField, TemplateField, ProjectManagerField };

    /// <summary>
    /// What the portal posts as a new job: a header against a template job, nothing below it. Total: it
    /// maps whatever is there, blanks included, so the confirmation can show a request that is not yet
    /// eligible; eligibility is <see cref="MaconomyJobSyncRules"/>' business, checked before the writer is called.
    /// </summary>
    public static MaconomyCardWrite Map(RemsRequest request, MaconomyJobSyncContext context)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CustomerField] = request.Client?.MaconomyCustomerNumber?.Trim(),
            [ProjectManagerField] = context.ProjectManagerNumber?.Trim(),
            [NameField] = JobNameOf(request),
        };
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

    /// <summary>The template job for the request: the map's row for its Job Template, else the tenant's default.</summary>
    public static string? ResolveTemplate(RemsRequest request, MaconomyJobSyncContext context)
    {
        if (request.JobTemplateId is { } template && context.TemplateMap.TryGetValue(template, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }
        return string.IsNullOrWhiteSpace(context.DefaultTemplateNumber) ? null : context.DefaultTemplateNumber.Trim();
    }

    /// <summary>"{client name}.{Service Line label}", the format the user set on 31 Oct 2026. Needs the service line loaded.</summary>
    public static string JobNameOf(RemsRequest request)
    {
        var client = request.Client is { } c ? ClientNameOf(c) : string.Empty;
        var line = request.ServiceLine?.Label?.Trim();
        return string.IsNullOrEmpty(line) ? client : $"{client}.{line}";
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
