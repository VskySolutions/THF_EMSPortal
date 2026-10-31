# One mapping function per Maconomy record — plan

**Status: BUILT on 31 Oct 2026.** The user asked, from a developer's point of view, for a
`MaconomyCustomerDataMapping` and a `MaconomyJobDataMapping` function in which the actual mapping lives,
so that a later change to what the portal sends Maconomy is made in that one place. This is a refactor
of the customer sync (`REMS_MACONOMY_CUSTOMER_SYNC_PLAN.md`) and the job sync
(`REMS_MACONOMY_JOB_SYNC_PLAN.md`); it changed no field, value or call. Proof: a console harness
(scratch, not in the repo) captured the container, fields, values and init values the old writers
produced for six cases — three clients (individual with suffix, organisation, person without a last
name) and three approved requests (mapped template, default template, Maconomy-picked client) — and the
new mapping classes produced byte-identical JSON. Section 2 is now a description of the code; one
difference from the plan: `Map` returns the mapping's default container and the sync service overrides it
with the configured one (`write with { Container = … }`), so the preview needs no configuration.

---

## 1. Where the mapping lives today

Each record's mapping is spread over four places, and the two records do it differently.

**Customer** (the client posted as a customer):

| What | Where |
| --- | --- |
| Which portal value goes out (name, email, phone, entity type, industry) | `MaconomyCustomerSyncService.PayloadFor` (private) |
| The name format "Last, First Suffix" / legal name | `MaconomyCustomerSyncService.CustomerNameOf` |
| Entity type → number, industry → number | `MaconomyCustomerService.EntityTypeNumberOf` / `IndustryNumberOf` |
| The field each value is written to (`name1`, `electronicmailaddress`, …) | `MaconomyCustomerCardOptions` (configuration), read in `MaconomyCustomerWriter.CreateAsync` |
| The same field names on the READ side (search, details) | string literals inside `MaconomyCustomerService` |

**Job** (the engagement posted as a job):

| What | Where |
| --- | --- |
| Which portal value goes out (customer, template, project manager, name) | `MaconomyJobSyncRules.PayloadFor` |
| The job name "{client}.{service line}", the client name, the template resolution | `MaconomyJobSyncRules.JobNameOf` / `ClientNameOf` / `ResolveTemplate` |
| The field each value is written to, and whether the template goes on `init` | `MaconomyJobCardOptions` (configuration), read in `MaconomyJobWriter.CreateAsync` |

A developer changing "send the billing email instead of the primary email" or "put the REMS number in
`description1`" today touches a payload record, a service, a writer and a configuration class.

---

## 2. The shape after

**One class per record, one function that is the mapping.** Everything that names a Maconomy field or
decides what goes in it sits in that file, and nothing else in the solution names a Maconomy field.

```
Application/Integrations/Maconomy/Mapping/
    MaconomyCustomerDataMapping.cs
    MaconomyJobDataMapping.cs
```

```csharp
public static class MaconomyCustomerDataMapping
{
    // The customer card's fields, as the instance accepted them (22 Oct 2026). Rename here, nowhere else.
    public const string NumberField = "customernumber";
    public const string NameField = "name1";
    public const string EmailField = "electronicmailaddress";
    public const string PhoneField = "telephone";
    public const string EntityTypeField = "specification6name";
    public const string IndustryField = "specification1name";

    /// THE mapping: the client as Maconomy's customer card. Change what is sent here and only here.
    public static MaconomyCardWrite Map(Client client, string email, MaconomyNumberMap numbers)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NameField] = CustomerNameOf(client),
            [EmailField] = email,
            [PhoneField] = client.PrimaryPhone ?? client.Person?.MobileNumber,
            [EntityTypeField] = numbers.EntityTypeNumberOf(client.EntityType?.Value),   // the option items' numbers
            [IndustryField] = numbers.IndustryNumberOf(client.Industry?.Value),         // (MaconomyNumberMap, since the same day)
        };
        return new MaconomyCardWrite(container, Fields, values, InitValues: null, KeyField: NumberField);
    }

    public static string CustomerNameOf(Client client) { … }            // moves here from the sync service
    public static MaconomyClientDetails FromCreated(IReadOnlyDictionary<string, string?> record) { … }
}
```

```csharp
public static class MaconomyJobDataMapping
{
    public const string NumberField = "jobnumber";
    public const string NameField = "jobname";
    public const string CustomerField = "customernumber";
    public const string TemplateField = "templatejobnumber";
    public const string ProjectManagerField = "projectmanagernumber";
    // Whether the template job goes on the init step (Maconomy derives the template's defaults) or on create.
    private const bool TemplateOnInit = false;

    /// THE mapping: the engagement as Maconomy's job card. Total: maps whatever is there, nulls included;
    /// eligibility is the rules' business, checked before the writer is called.
    public static MaconomyCardWrite Map(RemsRequest request, MaconomyJobSyncContext context, string container) { … }

    public static string JobNameOf(RemsRequest request) { … }           // "{client}.{service line}"
    public static string ClientNameOf(Client client) { … }              // "LastName FirstName" / legal name
    public static string? ResolveTemplate(RemsRequest request, MaconomyJobSyncContext context) { … }
    public static MaconomyCreatedJob FromCreated(IReadOnlyDictionary<string, string?> record) { … }
}
```

`MaconomyCardWrite` is one new record in `MaconomyContracts.cs`: the container, the fields the instance
is opened with, the values posted on create, the values posted on init (null for a bare init), and the key
field read back from the reply. It knows nothing about customers or jobs.

**The writer becomes generic.** One `IMaconomyCardWriter.CreateAsync(connection, MaconomyCardWrite)`
replaces the create halves of `MaconomyCustomerWriter` and `MaconomyJobWriter`: open the instance with
`write.Fields`, init with `write.InitValues`, create with `write.Values`, hand the record back. The
customer's duplicate check (`FindByEmailAsync`) stays on `IMaconomyCustomerWriter`, reading by the
mapping's constants. `IMaconomyJobWriter` goes.

**The sync services call the mapping and the writer, nothing else:**

```csharp
var write = MaconomyCustomerDataMapping.Map(client, email, _options.CustomerCard.Container);
var created = MaconomyCustomerDataMapping.FromCreated((await _writer.CreateAsync(connection, write, ct)).Data);
```

`MaconomyJobSyncRules` keeps the eligibility rules only and calls the mapping for `ResolveTemplate`;
`PayloadFor` and the `MaconomyNewCustomer` / `MaconomyNewJob` records go, since the write is the payload.

**The confirmation shows what is sent.** `RemsMaconomyJobSyncViews.PreviewFor` reads the mapped write
by the mapping's constants, so the dialog can never drift from the record.

**Configuration shrinks to what really differs per instance.** `Maconomy:CustomerCard` and
`Maconomy:JobCard` keep `Container` only (`dpl:customercard` on d104p, `customercard` on thmasd01;
`jobs`). The field-name keys and `TemplateOnInit` leave configuration: they are code, in the mapping. The
API and Workers `appsettings.json` set only `Container` today, so nothing on disk changes.

**The read side uses the same names.** `MaconomyCustomerService` (search, details, the preloaded
directory) swaps its string literals for `MaconomyCustomerDataMapping` constants, so a field renamed for
the write is renamed for the read in the same edit.

---

## 3. What changes, file by file

| File | Change |
| --- | --- |
| `Application/Integrations/Maconomy/Mapping/MaconomyCustomerDataMapping.cs` | new |
| `Application/Integrations/Maconomy/Mapping/MaconomyJobDataMapping.cs` | new |
| `Application/Abstractions/Integrations/Maconomy/MaconomyContracts.cs` | add `MaconomyCardWrite`; drop `MaconomyNewCustomer`, `MaconomyNewJob` |
| `Application/Abstractions/Integrations/Maconomy/IMaconomyCardWriter.cs` | new, generic create |
| `Application/Integrations/Maconomy/MaconomyCardWriter.cs` | new: the generic create (moved from the two writers) |
| `Application/Abstractions/…/IMaconomyCustomerWriter.cs`, `MaconomyCustomerWriter.cs` | keep `FindByEmailAsync` only, on the mapping's constants |
| `Application/Abstractions/…/IMaconomyJobWriter.cs`, `MaconomyJobWriter.cs` | deleted |
| `MaconomyCustomerSyncService.cs` | `PayloadFor`, `CustomerNameOf` out; `Map` + `FromCreated` in |
| `MaconomyJobSyncService.cs`, `MaconomyJobSyncRules.cs` | `PayloadFor`, `JobNameOf`, `ClientNameOf`, `ResolveTemplate` move to the mapping |
| `MaconomyCustomerService.cs` | field literals → mapping constants |
| `Shared/Configuration/MaconomyOptions.cs` | the two card option classes keep `Container` only |
| `Api/Models/Rems/RemsMaconomyJobSyncModels.cs` | preview reads the mapped write |
| `Application/DependencyInjection.cs` | register the card writer, drop the job writer |
| the two plan docs | point at the mapping files |

No migration, no endpoint, no UI, no configuration key on disk. Nothing the user sees moves.

---

## 4. Not in this change

- `private` functions inside the sync services instead of static mapping classes: the same shape is
  possible (`private static MaconomyCardWrite MaconomyCustomerDataMapping(…)` inside each service, with
  the job preview going through a service method), but a developer then has to know which service to
  open; a file named for the mapping is found by name. The recommendation is the classes above.
- Reading the write out of configuration or a database table (a Tenant Admin editing the mapping): not
  asked for, and the mapping holds code (name formats, number translations), not just field pairs.
- The probe of the `jobs` container is unchanged by this: whatever it finds is typed into
  `MaconomyJobDataMapping` once.

---

## 5. Checks before it is called done

1. A scratch build of the API.
2. For one client and one approved request, the `values` dictionary the new mapping produces equals the
   one the old code produced, field for field (checked in a console harness against the dev database, no
   call to Maconomy).
3. The customer search still returns the same rows for a known number (the read side on the constants).

About two hours.
