using System.Globalization;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of an approved engagement to Maconomy's job card. Everything that names a job card field,
/// or decides what goes in it, is in this file and nowhere else: change what the portal sends here.
/// Every field name is as the instance's card specification spells it and was accepted on opening a card
/// (12 Nov 2026); no job has yet been created through it. The container is the one thing that differs
/// per instance, so the sync service overrides it from configuration.
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
    // The job type popup, as its "…name" field — the form the customer card's specification popups took.
    public const string JobTypeField = "specification2name";
    public const string PurchaseOrderStartField = "date4";
    public const string PurchaseOrderEndField = "date5";
    public const string MarketingMethodsField = "text20";
    // The client's fiscal year end, as its month (a popup of January…December) and its year (a whole number).
    public const string FiscalMonthField = "popup2";
    public const string FiscalYearField = "theyear";
    public const string FiscalMonthList = "popup_jobpopuptype2";

    // The people on the engagement, each as the Maconomy Number on their user record (user, 13 Nov 2026).
    public const string AccountManagerField = "accountmanagernumber";
    public const string BillingManagerField = "employeenumber6";
    public static readonly string[] CommissionEmployeeFields = { "employeenumber8", "employeenumber9", "employeenumber10" };
    // Amount 1–3 on the card; the commission share goes as the percentage itself. Maconomy holds an amount
    // as a whole number of hundredths (live, 13 Nov 2026: 35.5 was cut to 35), so 40% goes out as 4000.
    public static readonly string[] CommissionShareFields =
        { "jobheaderextensionamount1var", "jobheaderextensionamount2var", "jobheaderextensionamount3var" };

    // Maconomy's length for a text field; a longer value is refused, record and all.
    private const int TextMax = 255;
    private const string MarketingSeparator = ", ";

    // REMS.Department: the one department whose fiscal year is the client's own, asked on its card.
    private const string AssuranceDepartment = "assurance";

    // Whether the template job goes on the init step, so Maconomy derives the template's defaults into
    // the record it hands back, or on the create with everything else. The first live create decides.
    private const bool TemplateOnInit = false;

    /// <summary>The card fields the write names. The number is read back, never sent: Maconomy's series assigns it.</summary>
    public static readonly string[] Fields =
    {
        NumberField, NameField, CustomerField, TemplateField, ProjectManagerField, LocationField, PurchaseOrderField, JobTypeField,
        PurchaseOrderStartField, PurchaseOrderEndField, MarketingMethodsField, FiscalMonthField, FiscalYearField,
        AccountManagerField, BillingManagerField,
        CommissionEmployeeFields[0], CommissionEmployeeFields[1], CommissionEmployeeFields[2],
        CommissionShareFields[0], CommissionShareFields[1], CommissionShareFields[2],
    };

    /// <summary>The fields Maconomy holds as numbers, which a write must send as numbers.</summary>
    public static readonly string[] NumberFields =
        { FiscalYearField, CommissionShareFields[0], CommissionShareFields[1], CommissionShareFields[2] };

    /// <summary>
    /// What the portal posts as a new job: a header against a template job, nothing below it. Total: it
    /// maps whatever is there, blanks included, so the confirmation can show a request that is not yet
    /// eligible; eligibility is <see cref="MaconomyJobSyncRules"/>' business, checked before the writer is called.
    /// Everything past the job name is optional: left out when blank, so the template job's stands.
    /// <paramref name="lookups"/> is what Maconomy itself says: without its months the fiscal month is
    /// left out, without its templates a Service Line is not matched by name.
    /// </summary>
    public static MaconomyCardWrite Map(RemsRequest request, MaconomyJobSyncContext context, MaconomyJobLookups? lookups = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CustomerField] = request.Client?.MaconomyCustomerNumber?.Trim(),
            [NameField] = JobNameOf(request),
        };
        // A person without a Maconomy Number is left out (user, 13 Nov 2026).
        Put(values, ProjectManagerField, request.EngagementExecutive?.MaconomyNumber);
        Put(values, AccountManagerField, request.CSE?.MaconomyNumber);
        Put(values, BillingManagerField, request.BillingManager?.MaconomyNumber);
        var recipients = CommissionRecipientsOf(request);
        for (var i = 0; i < recipients.Count; i++)
        {
            // The share stays beside its recipient: without the person's number neither is sent.
            if (!string.IsNullOrWhiteSpace(recipients[i].Employee?.MaconomyNumber))
            {
                values[CommissionEmployeeFields[i]] = recipients[i].Employee!.MaconomyNumber!.Trim();
                values[CommissionShareFields[i]] = AmountOf(recipients[i].CommissionPercentage);
            }
        }
        Put(values, LocationField, ResolveLocation(request, context));
        Put(values, PurchaseOrderField, request.PurchaseOrderNumber);
        Put(values, JobTypeField, ResolveJobType(request, context));
        Put(values, PurchaseOrderStartField, DateOf(request.Contract?.PurchaseOrderStartDate));
        Put(values, PurchaseOrderEndField, DateOf(request.Contract?.PurchaseOrderEndDate));
        Put(values, MarketingMethodsField, MarketingMethodsOf(request));
        if (FiscalYearEndOf(request) is { } fiscalYearEnd)
        {
            Put(values, FiscalMonthField, lookups?.Months?.ValueOf(MonthNameOf(fiscalYearEnd)));
            values[FiscalYearField] = fiscalYearEnd.Year.ToString("0000", CultureInfo.InvariantCulture);
        }
        var template = ResolveTemplate(request, context, lookups?.Templates);
        Dictionary<string, string?>? init = null;
        if (TemplateOnInit)
        {
            init = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [TemplateField] = template };
        }
        else
        {
            values[TemplateField] = template;
        }
        return new MaconomyCardWrite(DefaultContainer, Fields, values, init, KeyField: NumberField, NumberFields: NumberFields);
    }

    /// <summary>
    /// The template job: the one picked for this request, else the number set on the Service Line under
    /// Option Sets, else the one Maconomy template whose name is the Service Line's label (user, 13 Nov
    /// 2026). Null when none of the three gives one: the sync stops and asks for a pick.
    /// </summary>
    public static string? ResolveTemplate(RemsRequest request, MaconomyJobSyncContext context, MaconomyPickList? templates = null)
    {
        if (!string.IsNullOrWhiteSpace(request.MaconomyTemplateJobNumber))
        {
            return request.MaconomyTemplateJobNumber.Trim();
        }
        if (request.ServiceLineId is { } line && context.TemplateMap.TryGetValue(line, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }
        var label = request.ServiceLine?.Label?.Trim();
        if (string.IsNullOrEmpty(label) || templates is null)
        {
            return null;
        }
        var named = templates.Items.Where(t => string.Equals(t.Title.Trim(), label, StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count == 1 ? named[0].Value : null;
    }

    /// <summary>The first three commission recipients, in the order they were added.</summary>
    public static IReadOnlyList<RemsCommissionSplit> CommissionRecipientsOf(RemsRequest request)
        => request.CommissionSplits.Where(s => !s.Deleted).OrderBy(s => s.CreatedOnUtc).ThenBy(s => s.Id).Take(CommissionEmployeeFields.Length).ToList();

    /// <summary>The people on the engagement who have no Maconomy Number, as "Role Name": whose user record a refusal points at.</summary>
    public static IReadOnlyList<string> MissingNumbersOf(RemsRequest request)
    {
        var missing = new List<string>();
        void Look(string role, User? user)
        {
            if (user is not null && string.IsNullOrWhiteSpace(user.MaconomyNumber))
            {
                missing.Add($"{role} {user.DisplayName}".Trim());
            }
        }
        Look("Engagement Executive", request.EngagementExecutive);
        Look("CSE", request.CSE);
        Look("Billing Manager", request.BillingManager);
        foreach (var split in CommissionRecipientsOf(request))
        {
            Look("Commission recipient", split.Employee);
        }
        return missing;
    }

    /// <summary>The Maconomy location for the request: the map's row for its Department, or null while the department carries no number.</summary>
    public static string? ResolveLocation(RemsRequest request, MaconomyJobSyncContext context)
        => request.DepartmentId is { } department && context.LocationMap.TryGetValue(department, out var number) && !string.IsNullOrWhiteSpace(number)
            ? number.Trim()
            : null;

    /// <summary>What specification 2 is given for the request's Job Type — its Maconomy number where set, its label otherwise — or null while it names none.</summary>
    public static string? ResolveJobType(RemsRequest request, MaconomyJobSyncContext context)
        => request.JobTypeId is { } type && context.JobTypeValueMap.TryGetValue(type, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    /// <summary>
    /// The fiscal year end the job carries: the Tax card's Fiscal Year End, else, on an Assurance
    /// engagement, its Fiscal Year End of Client. Null when neither is filled, and then nothing is sent.
    /// </summary>
    public static DateOnly? FiscalYearEndOf(RemsRequest request)
        => request.FiscalYearEnd
            ?? (string.Equals(request.Department?.Value, AssuranceDepartment, StringComparison.OrdinalIgnoreCase)
                ? request.Client?.FiscalYearEnd
                : null);

    /// <summary>The month as Maconomy's Popup 2 titles it: January … December.</summary>
    public static string MonthNameOf(DateOnly date) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(date.Month);

    /// <summary>
    /// The chosen marketing methods' labels in the list's own order, joined by a comma and a space (user,
    /// 12 Nov 2026). Whole labels only: what would run past Maconomy's 255 characters is left off.
    /// </summary>
    public static string? MarketingMethodsOf(RemsRequest request)
    {
        var joined = string.Empty;
        foreach (var label in request.MarketingMethods
            .Where(m => !m.Deleted && !string.IsNullOrWhiteSpace(m.MarketingMethod?.Label))
            .OrderBy(m => m.MarketingMethod!.SortOrder)
            .ThenBy(m => m.MarketingMethod!.Label, StringComparer.OrdinalIgnoreCase)
            .Select(m => m.MarketingMethod!.Label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var next = joined.Length == 0 ? label : joined + MarketingSeparator + label;
            if (next.Length > TextMax)
            {
                break;
            }
            joined = next;
        }
        return joined.Length == 0 ? null : joined;
    }

    // An amount as Maconomy takes it: hundredths, whole.
    public static string AmountOf(decimal value)
        => decimal.ToInt64(decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    // Maconomy's date fields read and write as yyyy-MM-dd.
    private static string? DateOf(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }

    /// <summary>
    /// "{client name}-{Job Type label}-{Job Description}", the format the user set on 13 Nov 2026; a blank
    /// part is skipped. Needs the job type loaded.
    /// </summary>
    public static string JobNameOf(RemsRequest request)
    {
        var client = request.Client is { } c ? ClientNameOf(c) : string.Empty;
        var parts = new[] { client, request.JobType?.Label?.Trim(), request.JobDescription?.Trim() };
        return string.Join("-", parts.Where(p => !string.IsNullOrEmpty(p)));
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

    // ---- The mapping in words ----

    private const string Setup = "Engagement Setup";
    private const string SetupAcceptance = "Engagement Setup · Client Acceptance Form (Audit, Assurance)";
    private const string SetupGovernment = "Engagement Setup · Government Audit — Contract";
    private const string SetupGcs = "Engagement Setup · GCS — Purchase Order";
    private const string SetupTax = "Engagement Setup · Tax — Fiscal Year";
    private const string Marketing = "Marketing";
    private const string Commission = "Commission";
    private const string FormContract = "Client's form · Contract Details";
    private const string NotOnAForm = "Not on a form";

    private const string LeftOutWhenBlank = "Blank is left out.";
    private const string DocumentRule = "Uploaded to the job once it is created. Where Maconomy filed it is kept on the file.";
    private const string FiscalYearRule = "The month by name (December) to Popup 2, the year (2026) to Year. Left out when the date is blank.";
    private const string PersonRule = "The Maconomy Number on their user record (Administration → Users). Left out when they have none.";

    /// <summary>
    /// The mapping in words, for the Job tab: every field of the Engagement Setup, Marketing and Commission
    /// tabs in the order they are asked. Kept beside <see cref="Map"/> so a change to what is sent is a
    /// change to what the page says.
    /// </summary>
    public static MaconomyCardMapping Describe(string container) => new(container, new[]
    {
        new MaconomyFieldMapping(Setup, "Department", LocationField, "Location",
            "The department's Maconomy number, set under Option Sets → REMS Department. A department without one is left out, so the template job's location stands."),
        new MaconomyFieldMapping(Setup, "Department Director"),
        new MaconomyFieldMapping(Setup, "Service Line", TemplateField, "Template Job No.",
            "The line's Maconomy number where one is set under Option Sets → REMS Service Line, else the Maconomy template job named as the line's label. With no match the sync stops, and Sync job now asks which template to use."),
        new MaconomyFieldMapping(Setup, "Job Type", JobTypeField, "Specification 2",
            "The value's label, as Maconomy's job type list names it, or its Maconomy number where one is set under Option Sets → REMS Job Type. The label is also part of the job name."),
        new MaconomyFieldMapping(Setup, "Job Description", NameField, "Job Name", "The last part of the job name. Left off when blank."),
        new MaconomyFieldMapping(Setup, "Engagement Executive", ProjectManagerField, "Project Manager No.", PersonRule),
        new MaconomyFieldMapping(Setup, "Billing Manager", BillingManagerField, "Employee 6", PersonRule),
        new MaconomyFieldMapping(Setup, "First-Year Fee Estimate", MaconomyJobBudgetDataMapping.PriceField, "Budget · FEE line · Billing Price",
            "After the job is created: the job's budget line of type Outlay on task FEE gets quantity 1 and the estimate as its billing price. The line comes from the template job; where the template gave none the line is added. Left alone when the estimate is blank."),
        new MaconomyFieldMapping(Setup, "Engagement Fee"),
        new MaconomyFieldMapping(Setup, "% Realization"),
        new MaconomyFieldMapping(Setup, "Billing Frequency"),
        new MaconomyFieldMapping(Setup, "Description of Billing Process"),

        new MaconomyFieldMapping(SetupAcceptance, "Signed CAF (PDF)", null, "Job's Documents", DocumentRule, IsDocument: true),
        new MaconomyFieldMapping(SetupAcceptance, "Fiscal Year End of Client", FiscalMonthField, "Popup 2 and Year", "Assurance. " + FiscalYearRule),

        new MaconomyFieldMapping(SetupGovernment, "Contract Number"),
        new MaconomyFieldMapping(SetupGovernment, "Florida 1% state fee applies"),

        new MaconomyFieldMapping(SetupGcs, "Purchase Order No.", PurchaseOrderField, "Text 3", LeftOutWhenBlank),
        new MaconomyFieldMapping(SetupGcs, "Purchase Order Amount"),
        new MaconomyFieldMapping(SetupGcs, "PO Beginning Date", PurchaseOrderStartField, "Date 4", LeftOutWhenBlank),
        new MaconomyFieldMapping(SetupGcs, "PO Ending Date", PurchaseOrderEndField, "Date 5", LeftOutWhenBlank),
        new MaconomyFieldMapping(SetupGcs, "Purchase Order (file)", null, "Job's Documents", DocumentRule, IsDocument: true),

        new MaconomyFieldMapping(SetupTax, "Fiscal Year End", FiscalMonthField, "Popup 2 and Year", FiscalYearRule),
        new MaconomyFieldMapping(SetupTax, "Original Due Date"),
        new MaconomyFieldMapping(SetupTax, "First Extension Due Date"),

        new MaconomyFieldMapping(Marketing, "Marketing Methods", MarketingMethodsField, "Text 20",
            "The chosen methods' labels in the list's order, joined by a comma and a space, kept within Maconomy's 255 characters."),

        new MaconomyFieldMapping(Commission, "Recipient", CommissionEmployeeFields[0], "Employee 8, 9, 10",
            "The first, second and third recipient, in the order they were added. " + PersonRule),
        new MaconomyFieldMapping(Commission, "Percentage", CommissionShareFields[0], "Amount 1, 2, 3",
            "The percentage as typed (40 for 40%), beside its recipient; sent in hundredths (4000), which is how Maconomy holds an amount. Left out with a recipient who has no Maconomy Number."),

        new MaconomyFieldMapping(FormContract, "Purchase Order Start Date", PurchaseOrderStartField, "Date 4",
            "A Government client's own answer: the same date the GCS card shows as PO Beginning Date."),
        new MaconomyFieldMapping(FormContract, "Purchase Order End Date", PurchaseOrderEndField, "Date 5",
            "The same date the GCS card shows as PO Ending Date."),

        new MaconomyFieldMapping(NotOnAForm, "Job name", NameField, "Job Name",
            "Client name-Job Type-Job Description, the description left off when blank: a person as \"Last First\", an organisation by its legal name, a Maconomy-picked client as Maconomy names it."),
        new MaconomyFieldMapping(NotOnAForm, "Customer", CustomerField, "Customer No.",
            "The client's Maconomy client number, from the customer sync or the Maconomy client picked on the request. Without one the request is not eligible."),
        new MaconomyFieldMapping(NotOnAForm, "CSE (Client Information)", AccountManagerField, "Account Manager No.", PersonRule),
        new MaconomyFieldMapping(NotOnAForm, "Job number", NumberField, "Job No.",
            "Never sent: Maconomy's number series assigns it, and it is written back onto the request.", ReadBack: true),
    });
}
