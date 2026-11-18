# Posting a REMS engagement to Maconomy as a job — plan

**Status: BUILT on 31 Oct 2026. Approval trigger BUILT 3 Nov 2026 (section 11). Field names confirmed against the
instance's specification on 12 Nov 2026, and a job has been created from the portal on thmasd01 (section 14).**
The user settled the shape (section 0)
and the code in section 4 exists on branch `feat/rems-data-model-v2` (migration
`20261031090743_AddRemsMaconomyJobSync`, applied by the API on its next start). The job card's field names
are still documentation guesses, held as constants in `MaconomyJobDataMapping` (since the mapping
refactor of the same day, `MACONOMY_DATA_MAPPING_PLAN.md`; only the container is configuration,
`Maconomy:JobCard:Container`); section 5 is the read-only probe that confirms them before the first live
create, exactly as the customer card's names were fixed. Section 7 lists the points decided by me rather
than the user.

Builds on the customer sync (`REMS_MACONOMY_CUSTOMER_SYNC_PLAN.md`): the per-tenant connection,
`MaconomySessionManager` (token by age, pause after a failure), `MaconomyGateway.CreateAsync` (instance →
init → create, each carrying `Maconomy-Concurrency-Control`), the sync columns on `Client`, and the
request-page chip. The job sync is the same shape one level down and simpler: where the customer sync
makes the CLIENT a Maconomy customer on approval, the job sync makes the ENGAGEMENT a Maconomy job under
that customer, **by hand, from the request page**.

---

## 0. What was decided on 31 Oct 2026

1. **Three fields are mandatory** to create a job in Maconomy: the template job, the customer, and the
   project manager. Nothing else is sent in this phase beyond a job name.
2. **The project manager is static**: Maconomy employee **82** for every job, as a tenant setting on the
   Job tab (editable, default 82). No user → employee mapping yet.
3. **Sync is manual, per request** — SUPERSEDED 3 Nov 2026 by section 11: the job is queued on approval.
   As first decided: no approval trigger, no Hangfire job: an eligible request is posted
   when somebody presses Sync job now, and a failed attempt stays failed until somebody presses it again.
   The button lives in the Actions column of the list pages (the Approvals list and COP Review) and on the
   Job tab's table — NOT on the request page, which shows the job chip only (user, 31 Oct 2026).
   A Start/Stop switch on the Job tab, like the customer sync's: while stopped, the action is hidden
   everywhere, not disabled.
4. **The job number comes back onto REMS**, with who pressed sync and when: `MaconomyJobNumber`,
   `MaconomyJobSyncedById`, `MaconomyJobSyncedOnUtc` (the user's "SyncById / SyncOnUTC", named like the
   client's `MaconomySyncedOnUtc` and the column standard: every FK ends in Id and names its role).
5. **Tenant Settings → Maconomy becomes a tabbed page**: Authentication → Customer → Job. The Customer and
   Job tabs open only once authentication is configured.
6. **The Job tab** is the Tenant Admin's section for jobs: the settings the sync needs, the template map,
   and the list of approved requests with their job state and a Sync now on each.

---

## 1. How it works

**Eligibility.** A request can be posted as a job when all of these hold (assumption 7.1 for the first):

- its status is **Approved** — an engagement is a job once the firm has signed it off;
- its client holds a **Maconomy customer number** (picked from Maconomy, or posted by the customer sync —
  the "customer first" rule falls out naturally: a Brand New Client request's customer sync runs first,
  then the job sync);
- a **template job number resolves**: the request's Job Template is mapped on the Job tab, or the tab's
  default template job number is set (section 3);
- the **project manager number** is set on the Job tab (82 by default);
- the request does not already hold a job number, and no attempt is under way.

When one does not hold, the icon is disabled and its tooltip says which ("Sync the customer first",
"Pick a Job Template on the request, or set a default template job under Tenant Settings → Maconomy →
Job", …). The same reasons come back as a 409 from the API, so a stale page cannot post.

**The press.** Sync job now opens a confirmation naming what will be sent — client and customer number,
template job number, project manager number, job name — because the press creates a record in another
system. Confirm → one inline attempt:

1. Mark the row **Syncing** (set BEFORE the create, so a crash between the create and the record leaves a
   trace), save.
2. Open a `jobs` container instance naming the card fields; `init`; `create` with the values in section 2.
   The job number is sent EMPTY: Maconomy's series assigns it. Whether the template number goes on `init`
   (so Maconomy derives the template's defaults into the record) or on `create` is what the probe decides;
   the gateway takes both.
3. Read the job number back from the create reply. Record **Synced** with the number, the actor and the
   time; write the activity event; return the row. A reply without a number is **Failed**, with the job
   name to look for in Maconomy before pressing again.
4. Any refusal — Maconomy's own `errorMessage`, an unreachable host, a missing mapping — is **Failed**
   with the reason on the row and in the activity log. No retries.

**Double-create guard.** Syncing before the create; a row left Syncing longer than
`Maconomy:JobSyncStaleMinutes` (10) reads as Failed ("the portal lost track of this attempt") and may be
pressed again. Without the REMS number on the job (7.3) there is no lookup by which a lost job could be
found, so that press could make a second job; sending the REMS number closes the gap.

**Who may press.** The same rule as the customer's Sync now: an approver on the request, or anyone with
`rems.engagements.manage`; the Job tab's per-row Sync now needs `integrations.maconomy.manage` and is
always the caller's own tenant (the 22 Oct rule for the sync cards).

**What users see.** The request page header gets a job chip beside the customer chip — "Job 10234" with
the synced-by and synced-on in the tooltip, or "No Maconomy job" / "Posting job…" / "Job sync failed"
with the reason; the engagement section repeats it read-only with a sentence. The lists (Approvals, COP
Review, My Requests) get a "Maconomy job" column beside the customer one; the Approvals list and COP
Review carry the Sync job now action in their Actions column, shown only while the job sync is on and the
row is eligible. Every attempt writes `RemsMaconomyJobSynced` or `RemsMaconomyJobSyncFailed` to the
activity log.

---

## 2. What is sent

Every field name is a constant in `MaconomyJobDataMapping`, to be confirmed by the probe; the names below
are the JobHeader columns lower-cased, as the customer card's turned out to be. That class is the one
place that says what is sent.

| Job card field (default name) | From | Rule |
| --- | --- | --- |
| `templatejobnumber` | the request's Service Line → its Maconomy number (Option Sets → REMS Service Line; section 13); else the configured default template | required; refused when neither resolves |
| `customernumber` | `Client.MaconomyCustomerNumber` | required; refused when blank |
| `projectmanagernumber` | the Job tab's project manager number | required; 82 by default |
| `jobname` | "{client name}-{Job Type label}-{Job Description}", the description left off when blank (user, 13 Nov 2026; was "{client name}.{Service Line label}") — a person as "LastName FirstName", an organisation as its name | refused when the request names no Job Type |
| `jobnumber` | — | sent empty; read back from the reply |
| `locationname` | the request's Department → its Maconomy number (Option Sets → REMS Department; section 12) | optional; left out when the department carries none, so the template job's location stands |
| `text3` | `RemsRequest.PurchaseOrderNumber` (section 12) | optional; left out when blank |
| `specification2name` | the request's Job Type → its label, or its Maconomy number where one is set (section 13) | optional; left out when the request names no Job Type |
| `date4`, `date5` | the purchase order's start and end dates on the request's contract (section 14) | optional; left out when blank |
| `text20` | the chosen marketing methods' labels, joined by ", " (section 14) | optional; left out when none is chosen |
| `popup1` | the engagement's fiscal year, yyyy, as Popup 1's literal (section 14) | optional; left out when the request has none, or the list does not hold the year |

Not sent in this phase: company, job group (the template job fixes them — the probe's dry run shows what
it derives), the job's own dates, fees, billing, personnel rates, tasks, the REMS number (7.3). Each is a later phase
(section 8), none blocks the first job. The location was on this list until 7 Nov 2026 (section 12).

---

## 3. The Maconomy page in tabs

`MaconomySettingsPage.vue` keeps its route (`/settings/maconomy`, `integrations.maconomy.manage`) and its
header, and becomes three tabs in the house style (`q-tabs dense no-caps align="left"`, as on Tenant
detail), with the open tab in the URL (`?tab=job`) so a link lands on the right one.

**Authentication** — the connection card as it is today: base URL, instance code, user name, password,
container id, default limit, "Customer lookup enabled", the token line and last-login error, and the
Delete / Forget token / Test connection / Save actions. A Super Admin's scope control moves this card to
another tenant, as now.

**Customer** — the customer lookup card and the customer sync card, moved as they are (lookup follows the
scope; the sync card is always the caller's own tenant).

**Job** — new, always the caller's own tenant:

- *Job settings* card: **Project manager (employee number)**, default 82; **Default template job number**,
  optional — used when a request's Job Template has no row in the map, or the request names no template
  (3 of the 4 approved requests in dev name none, so this is what makes a first run possible without
  editing requests). Save button. Field names and the container are platform config, not shown.
- *Template map* card: one row per value of the tenant's `REMS.JobTemplate` option set (17 at THF), each
  with a Maconomy template job number. Typed in phase 1; a picker fed from Maconomy's template jobs once
  the probe confirms the template flag (section 5, step 2).
- *Jobs* card: counts (jobs created, failed, waiting) and a table of the tenant's approved requests —
  request, client, customer number, template, job number, status chip, synced by / on, reason — with a
  Sync now button per row that is enabled under the same rules as the request-page icon. Refresh button.

**Gating.** The Customer and Job tabs are disabled, with the tooltip "Save and enable the connection on
the Authentication tab first", until a connection is saved and enabled (assumption 7.4). Deleting the
connection returns the page to Authentication. A banner on both tabs says "No token held yet — Test the
connection" while none has been issued, without locking them.

---

## 4. What gets built

**Data** (one migration, `AddRemsMaconomyJobSync`):

- `REMS`: `MaconomyJobNumber` nvarchar(64) null; `MaconomyJobSyncStatus` nvarchar(20) not null, default
  `NotSynced` (enum `RemsMaconomyJobSyncStatus`: NotSynced, Syncing, Synced, Failed — stored as text like
  the client's); `MaconomyJobSyncedById` uniqueidentifier null → `Users`; `MaconomyJobSyncedOnUtc`
  datetime2 null; `MaconomyJobSyncError` nvarchar(1000) null. Filtered index on
  (`TenantId`, `MaconomyJobNumber`) where not null.
- `MaconomyConnections`: `JobSyncEnabled` bit default 0, `JobSyncChangedOnUtc`, `JobSyncChangedById`
  (the Start/Stop, as the customer sync's), `JobProjectManagerNumber` nvarchar(64) null,
  `JobDefaultTemplateNumber` nvarchar(64) null. (On the connection row because they are "static for all";
  the Job tab shows 82 when the project manager is still null, and the first Save stores it.)
- `MaconomyJobTemplates` (new, per tenant): `Id`, `TenantId`, `JobTemplateId` → `OptionSetItems`,
  `TemplateJobNumber` nvarchar(64), audit columns; unique (`TenantId`, `JobTemplateId`) where
  `Deleted = 0`. A table rather than `OptionSetItem.MetadataJson`: no JSON in the REMS model, and a grid
  is what a Tenant Admin fills.

**API**

- `IMaconomyGateway.CreateAsync`: `MaconomyCreateRequest` gains optional `InitValues`, posted on the
  `init` step when present (today init always sends `{}`); nothing else in the gateway changes.
- Application: `IMaconomyJobWriter` (`CreateAsync` → job number; `ReadByNumberAsync` for a later Link)
  and `MaconomyJobSyncService` (`WhyNotEligible`, `SyncNowAsync`, `IsStale`, overview and list for the
  Job tab, settings and map read/write), mirroring `MaconomyCustomerSyncService` minus the queue and the
  approval hook.
- Endpoints:
  - `POST /api/rems/requests/{id}/maconomy-job-sync` — the request-page icon (approver on the request or
    `rems.engagements.manage`); 200 with the row whatever happened, 409 with the reason when not eligible.
  - `GET /api/integrations/maconomy/job-sync` — settings, counts, template map;
    `PUT …/job-sync/settings` — project manager number, default template, the map rows in one payload;
    `GET …/job-sync/requests` — the Jobs table; `POST …/job-sync/{remsId}/run` — Sync now from the tab.
    All `integrations.maconomy.manage`, always the caller's own tenant.
  - `GET /api/integrations/maconomy/template-jobs` — after the probe: Maconomy's template jobs for the
    map's picker.
- Configuration: `Maconomy:JobCard:Container` (`jobs`), `Maconomy:JobSyncStaleMinutes` (10). The field
  names and the init-or-create switch are code in `MaconomyJobDataMapping`.
- `RemsRequestDetail` gets `maconomyJobSync` beside `maconomySync` (the same view shape: status, job
  number, synced by name, synced on, failure reason, `canSyncNow`, `blockedReason`); activity types
  `RemsMaconomyJobSynced` / `RemsMaconomyJobSyncFailed`; error codes reuse the customer sync's
  (`MaconomySyncNotEligible`, `MaconomySyncRunning`).

**Web**

- `MaconomySettingsPage.vue` → the three tabs (section 3); the connection and customer blocks move
  unchanged into their tabs.
- `RemsRequestForm.vue`: the job chip and the sync icon in the header actions; the read-only line in the
  engagement section. `MaconomySyncChip.vue` takes a `kind` (customer | job) and `maconomySync.js` gains
  the job looks, so the two chips read alike.
- `services/api.js`: the calls above.
- Optional, cheap: a "Maconomy job" column on COP Review and the Approvals list beside the customer
  column. Not in the first cut unless asked.

**Not touched:** the approval route, the customer sync, the REMS statuses, Hangfire.

---

## 5. Before the first live create — the probe

Read-only, against the tenant's connection (thmasd01, the test instance), from the console harness
described in the live-check memory note. Needs the user's go-ahead: it is outward-facing. Nothing is
written; the instance opened in step 3 is deleted again.

1. `GET /containers/thmasd01/jobs` — the container specification: the card fields and their types, which
   are keys, which are popups (`…name`), and the action links. Fixes the constants in `MaconomyJobDataMapping`. If the write
   container turns out to be a customised one (as `dpl:customercard` was on d104p), that goes in config too.
2. `POST /containers/thmasd01/jobs/filter` with the template flag the spec names (`template = true`
   expected): THF's template jobs — number, name, company, location, job group — to fill the map and to
   see what a template fixes.
3. Dry run: open a `jobs` instance, `init` with `{"data": {"templatejobnumber": "<one from step 2>"}}`,
   read what Maconomy derives, DELETE the instance without `create`. If init refuses data, repeat with `{}`:
   the template number then goes on `create` (`TemplateOnInit` in `MaconomyJobDataMapping`).
4. The first real create, pressed by the user from a request page on dev, with the confirmation showing
   every value beforehand. Alternatively THF's Maconomy team can supply the job-create curl as they did for
   the customer (22 Oct), which replaces steps 1 and 3.

---

## 6. Build order

1. Probe (section 5) — can run in parallel with 2–4, since every name is config.
2. Data: entity columns, enum, `MaconomyJobTemplate`, migration, repository methods (job state by request,
   approved requests for the Jobs table, map rows).
3. Application: gateway `InitValues`, `MaconomyJobWriter`, `MaconomyJobSyncService`.
4. API: the two controllers' endpoints, the detail view field, validators.
5. Web: the tabbed settings page, the Job tab, the request-page chip and icon, `api.js`.
6. First live create on thmasd01 by the user; then the template picker (`template-jobs`) if the probe
   confirmed the flag.

Roughly two days of build before the first live create; every piece has a twin in the customer sync to
copy.

---

## 7. Assumptions to confirm or change

1. **Approved only.** The icon is enabled only on an Approved request. (Alternative: any request whose
   client is in Maconomy, so a job exists before approval.)
2. ~~Job name~~ DECIDED 31 Oct 2026: "{client name}.{Service Line label}", the person as
   "LastName FirstName", the organisation as its name. A request without a Service Line is refused.
   CHANGED 13 Nov 2026: "{client name}-{Job Type label}-{Job Description}", the description left off when
   blank; the tax form and fiscal year are no longer part of it (tax forms were dropped the same day).
3. **The REMS number is not sent** in this phase, matching the customer decision of 22 Oct. Recommended
   for a later cut: a text field on the job (`description1` or whatever the spec names) carrying the REMS
   number, which gives a duplicate check on re-sync and a way to find the portal's job from Maconomy's side.
4. **Tab gating** = a saved, enabled connection. (Alternative: a token must have been issued at least
   once, i.e. Test connection passed, which would also lock the tabs again after a password change.)
5. ~~No Start/Stop switch~~ DECIDED 31 Oct 2026: Start/Stop on the Job tab; while stopped the Sync job
   now action is hidden, not disabled.
6. **Project manager is a plain number field** (default 82), not a picker from Maconomy's employees.
7. ~~Where Sync now lives~~ DECIDED 31 Oct 2026: only in the Actions column of the list pages (Approvals
   list, COP Review) and on the Job tab's table. The request page shows the chip only.

---

## 8. Later phases

- **Link by hand**: type an existing job number onto a request; the portal reads it back to check it
  belongs to the client's customer. Needs `ReadByNumberAsync`, which the writer gets in phase 1.
- ~~**Approval trigger**~~ BUILT 3 Nov 2026, section 11.
- **Project manager from the request** (Engagement Executive → a Maconomy employee number on the user).
- **Tasks**: one `jobtasks` row per ticked `REMS.TaxForm`, named by THF's task type codes (1040, 1041, …),
  where the template does not already carry them.
- **Money**: fees as a job budget, GCS rates as a job price list, billing period as an invoicing plan —
  each its own container and its own decision.

---

## 9. What Maconomy's Jobs module wants — the research (29 Oct 2026, unchanged)

- Jobs live in the `jobs` container; a card field is the JobHeader column lower-cased, so the expected
  names are `jobnumber`, `jobname`, `customernumber`, `templatejobnumber`, `projectmanagernumber`,
  `companynumber`, `locationname`, `startingdate`, `expectedendingdate`, with popups as `…name`. None is
  confirmed until the probe.
- By hand (Maconomy Essentials, the cloud edition THF runs): New Job → select a template job → customer
  (mandatory) and company → job name and project manager (defaults to the signed-in user, i.e. the API's
  service account if not sent) → location and job group → Create. Maconomy assigns the job number; new
  jobs start as Quote; every job belongs to a job group.
- The template job carries what the portal does not know: tasks, price list, invoicing principle, invoice
  layout, approval settings, dimensions, job group, company. Deltek's own CRM → Maconomy job creation
  sends only a header against a template — the model copied here.
- `REMS.JobTemplate` (17 values) is the portal's name for Maconomy template jobs; the map on the Job tab
  is the table THF supplies.
- The project manager is an EMPLOYEE number; portal users carry none (`Person.EmployeeCode` is free text,
  empty for all staff), hence the static 82.
- THF's "CCH Workflow – Maconomy integration fields" sheet (attached to REMS-10 in dev) lists task types
  per tax form — the basis for the tasks phase.
- Still true from the customer work: one write is instance → init → create; a popup is written through
  its `…name` field; a refusal comes back as `errorMessage`; `dpl:` containers exist on d104p only,
  thmasd01 knows the plain ones; the filter language has no `in`; a container instance is deleted after use.

Sources: Deltek Maconomy RESTful API guide; Deltek Information Center "Create a Job" and the Jobs window
reference; Maconomy Essentials "Create a Job" click guide; Deltek CRM → Maconomy job creation field list;
Deltek Web Client "Build a New Workspace" (Jobs source, JobsCard, JobTasks); Deltek Unionpoint connector
container list; THF's CCH Workflow sheet.

---

## 10. Settings relocation and Maconomy numbers on option items — BUILT 31 Oct 2026

The user's instructions, in order: the project manager number goes into appsettings, so the Job tab needs
no Job settings card; the template map moves into Option Sets as a new `MaconomyNumber` column on option
items, which the user fills by hand, so the Template map table and card go away; keep an optional default
template job number in appsettings; a second migration on top of the morning's; and the entity type and
industry numbers the customer sync hard-codes move onto the same column.

### Where each value lives after

| Value | Today | After |
| --- | --- | --- |
| Project manager employee number | `MaconomyConnections.JobProjectManagerNumber`, typed on the Job tab | `Maconomy:JobCard:ProjectManagerNumber` in `appsettings.json` (API and Workers), set to `82`; code default null, so a missing key refuses the sync naming the key |
| Default template job number | `MaconomyConnections.JobDefaultTemplateNumber`, typed on the Job tab | `Maconomy:JobCard:DefaultTemplateJobNumber` in `appsettings.json`, optional; blank means a request without a numbered template is refused |
| Template job number per REMS Job Template | `MaconomyJobTemplates` table, the Template map card | `OptionSetItems.MaconomyNumber` on each `REMS.JobTemplate` item, set under Tenant Settings → Option Sets |
| Entity type → specification 6 number (1–6) | the `EntityTypeOf` table in `MaconomyCustomerService` | `OptionSetItems.MaconomyNumber` on each `REMS.EntityType` item |
| Industry → specification 1 number (17–38) | the `Industries` table in `MaconomyCustomerService` | `OptionSetItems.MaconomyNumber` on each `REMS.Industry` item |
| Start/Stop, counts, approved-requests table | Job tab | Job tab, unchanged |

### The number map

One per-tenant object, `MaconomyNumberMap`, built from the tenant's effective `REMS.EntityType`,
`REMS.Industry` and `REMS.JobTemplate` lists: for each item its id, code (`Value`), label and
`MaconomyNumber`. Provided by `IMaconomyNumberMaps.GetAsync(tenantId)` in the Application layer, read
through `IOptionSetRepository`, held in `IMemoryCache` for a minute and dropped the moment an item of one
of those lists is saved or deleted through `OptionSetService`. Every caller that translates a code to a
number or a number to a code asks the map; nothing holds a table any more.

- **Write side.** `MaconomyCustomerDataMapping.Map(client, email, numbers)` asks
  `numbers.EntityTypeNumberOf(code)` and `numbers.IndustryNumberOf(code)`; a code without a number leaves
  the field out, so Maconomy keeps its default (as today for an unmapped code). `MaconomyJobSyncContext`
  keeps its `TemplateMap`, now `numbers.JobTemplates`, so the per-row eligibility check and
  `MaconomyJobDataMapping.ResolveTemplate` do not change.
- **Read side.** `MaconomyCustomerService` resolves the map once per search or read and hands it to
  `ToOption` / `ToDetails`, which keep their matching rules: entity type by number; industry by number
  prefix ("17", "17-Manufacturing") then by label, the label now being the item's. The customer picker's
  auto-fill of entity type and industry, and the directory's details, come out the same. The directory
  (a singleton refreshed every `CustomerCacheSeconds`) converts records as it loads, so a number changed in
  Option Sets reaches a cached client on the next refresh and a live search at once.
- **Unknown values** read as they do today: the raw specification text, no REMS code.

### Data

One migration, `MaconomyNumberOnOptionItems`, on top of `20261031090743_AddRemsMaconomyJobSync`:

1. Add `OptionSetItems.MaconomyNumber` nvarchar(64) null.
2. Seed today's numbers onto every existing `REMS.EntityType` and `REMS.Industry` item — the standard
   list and every tenant's copy — matched by set key and item value, so the first start after the
   migration behaves exactly as before. (`REMS.JobTemplate` items get no number: THF supplies those.)
3. Drop the `MaconomyJobTemplates` table and the `JobProjectManagerNumber` / `JobDefaultTemplateNumber`
   columns on `MaconomyConnections`.

`DefaultOptionSets` item definitions carry the same numbers, so a tenant created later is seeded with
them. Then the two tables in `MaconomyCustomerService` are deleted: the option items are the only source.

### API

- `OptionSetItem.MaconomyNumber`; the item create/update requests, the item response, the validator
  (64 characters) and `OptionSetService` carry it like `Description`; saving an item of one of the three
  lists drops that tenant's cached map.
- `IMaconomyNumberMaps` + `MaconomyNumberMap` (new); `MaconomyOptions.JobCard` gains
  `ProjectManagerNumber` and `DefaultTemplateJobNumber`.
- Removed: `IMaconomyJobTemplateRepository`, its repository, entity, configuration, DbSet, query filter
  and tenant case; `SaveSettingsAsync`, `ListTemplatesAsync`, `MaconomyJobSyncSettingsInput`,
  `MaconomyJobTemplateInput`; `PUT /api/integrations/maconomy/job-sync/settings` and its request class;
  the static number tables and `EntityTypeNumberOf` / `IndustryNumberOf` / `EntityTypeOf` / `IndustryOf`.
- The job overview keeps `projectManagerNumber` and `defaultTemplateNumber` (the configured values,
  read-only) and `templates` (label + number per `REMS.JobTemplate` item), for the Job tab's line.

### Web

- Option Sets: the item dialog gets a "Maconomy number" field with an info line saying what the sync
  uses it for (the specification 6 number on an entity type, the specification 1 number on an industry,
  the template job number on a job template); the detail page's item list gets the column.
- Maconomy settings → Job tab: the Job settings and Template map cards go. The Job sync card gains two
  read-only lines above its counts: "Project manager: employee 82 (server configuration)" and
  "12 of 17 job templates carry a Maconomy number — set them under Option Sets", linking to the list.
- `api.js`: `saveJobSyncSettings` goes; the option-item calls already send the whole item.

### What the checks showed

The scratch harness produced byte-identical customer and job writes for the six cases, with the numbers
now read from a map built the way the seeded option items are. The read side matched for six of seven
records; the seventh is the label-only form of the four insurance trades: the old table carried Maconomy's
labels ("Insurance - Health"), the option items carry the portal's ("Health"), so a specification 1 value
that is the label alone no longer resolves for those four. The number form ("29", "29-…"), which THF's
numbering was built for, resolves as before. Migration `20261031115010_MaconomyNumberOnOptionItems`.

### Checks before it is called done

1. Scratch build.
2. The six-case harness again: the customer writes (numbers 1, 5, 6, 17 via the map) and the job writes
   are byte-identical to `old.json`; plus a read-side case: `ToDetails` on a sample customer record gives
   the same entity type code, industry code and label as before.
3. On dev after the migration: every `REMS.EntityType` and `REMS.Industry` item shows its number in Option
   Sets; a customer search still returns "(Commercial)" style suffixes and the picker still auto-fills.

About three hours.

---

## 11. Approval trigger — BUILT 3 Nov 2026

The user's instruction: "When a request gets approved by all, like the customer, we also need to send the
request as a job into Maconomy automatically." Section 8's first later phase, built the way the customer
sync is queued; the user confirmed the order (customer first if new, then the job) the same day.

- **When.** A request is queued to be posted as a job the moment it is fully approved AND its client is a
  Maconomy customer. For a client already in Maconomy (picked at intake, or linked earlier) that is the
  final approval itself (`RemsApprovalController.Approve`, next to the customer sync's record-and-enqueue).
  For a Brand New Client request it is the moment the customer sync lands the customer, Synced or Linked:
  `MaconomyCustomerSyncService.SucceedAsync` records and enqueues the job for the subject request, whether
  the customer sync ran from the queue or from a Sync now press.
- **How.** `IMaconomyJobSyncService.RecordPendingAsync` marks the request `Pending` (a new value of
  `RemsMaconomyJobSyncStatus`; the column is a string, so no migration) inside the same unit of work;
  `Enqueue` hands it to Hangfire after the commit (`MaconomyJobSyncJob`, `IMaconomyJobSyncQueue`, one
  attempt, no Hangfire retry). `RunAsync` loads the request, applies `MaconomyJobSyncRules.WhyNotPostable`
  and posts through the same `AttemptAsync` as a press, with no actor.
- **Outcomes.** A post that fails, or a request that lacks something (no Service Line, no template job, no
  project manager, Maconomy not connected), is recorded `Failed` with the reason, for Sync job now. A
  request queued while the sync was on but run after it was switched off goes back to `NotSynced`, quietly.
  A `Pending` older than `Maconomy:JobSyncStaleMinutes` reads as a lost attempt, like a stale `Syncing`,
  and Sync job now is offered again.
- **The switch.** The Job tab's Start/Stop now governs the trigger too: off means nothing is queued. The
  customer sync's switch is unchanged; while it is off a Brand New Client request's job waits (blocked:
  "The client is not in Maconomy yet") until an admin syncs the customer by hand, which then queues the job.
- **Sync job now stays**, on the same lists, for a request that failed or was approved while the sync was
  off. `MaconomyJobSyncedById` is null for a job that ran by itself.
- Not run against Maconomy, like the rest of the job sync.

---

## 12. Location and purchase order on the job card — BUILT 7 Nov 2026

The user's instruction: "Add New Job mapping, 1. Department -> location (maconomy) [Assurance-1, Tax-2,
Admin-3, GCS-4, CAS-5, Consulting-6]. 2. Purchase Order -> text3."

- **`locationname`** is the request's Department's Maconomy number, read the way the template job's is:
  `OptionSetItem.MaconomyNumber` on each `REMS.Department` item, through `MaconomyNumberMap.LocationMap`
  (department item id → number) on `MaconomyJobSyncContext`. `MaconomyJobDataMapping.ResolveLocation`
  resolves it; a department without a number leaves the field out, so the template job's location stands.
  The location is not an eligibility condition, so `MaconomyJobSyncRules` does not change.
- **The numbers.** Seeded in `DefaultOptionSets` for a new tenant and backfilled by migration
  `DepartmentMaconomyLocationNumbers` onto the standard list and every tenant copy, matched by set key and
  value, only where blank: Assurance 1, Tax 2, Admin 3, GCS 4, CAS 5. Audit, retired, gets none. **Consulting
  (6) is not a department**: it is a Service Line, offered under Tax, Assurance, GCS and CAS, so nothing
  carries 6. A department added under Option Sets with Maconomy number 6 would map to it without a code
  change.
- **`text3`** is `RemsRequest.PurchaseOrderNumber`, as entered on the Engagement Setup (the GCS block);
  blank is left out.
- **What follows.** The job's field list names both; the preview (`RemsMaconomyJobSyncPreview`:
  `department`, `locationNumber`, `purchaseOrderNumber`) and the confirmation dialog show them; the Job tab's
  table gains a Department column and its Job defaults card a "n of m departments carry a Maconomy location
  number" line (offered departments only); the mapping card reads them from `Describe`. `JobSyncGraph` loads
  the department.
- Field names as unconfirmed as the rest of the card (section 5): `locationname` is the popup's `…name` form,
  `text3` the free-text column.
- Not run against Maconomy, like the rest of the job sync.

## 13. Job Type in place of Job Template — BUILT 11 Nov 2026

The Job Template is gone from the Engagement Setup: a 46-value **Job Type** (`REMS.JobType`, independent of the department and service line) names the template job instead, through the same `MaconomyNumber` on its option item, the same `TemplateMap` and the same `ResolveTemplate`; the eligibility messages, the Job tab line and the mapping card say Job Type. The probe in section 5 step 2 is answered: the flag is `template = true` (not `templatejob`), THF has 38 template jobs numbered "ZZ" + the job's name, and 44 job types were seeded with theirs (the thirteen tax types all on ZZTax Compliance; Estate Planning and Internal IT empty by decision). Plan, decisions and what was built: `REMS_JOB_TYPE_PLAN.md`.

**Corrected later on 11 Nov 2026.** The template job follows the **Service Line**, not the Job Type: the
Service Line's Maconomy number is `templatejobnumber` (THF's ZZ template jobs, seeded on thirteen lines
from THF's department table; Assurance Services and Non-Chargeable Internal left for the tenant), and the
**Job Type goes to `specification2name`** by its label, or by its Maconomy number where a tenant sets one
to say it differently. The job type numbers seeded that morning were cleared.

## 14. Purchase order dates, marketing methods, fiscal year, and documents — BUILT 12 Nov 2026

The user's instruction of 12 Nov 2026 ("New Mapping Confirmed For Maconomy Integration"): PO Start Date →
Date 4, PO End Date → Date 5, Marketing Methods → Text 20, Fiscal Year End → Popup 1, the PO and CAF files
→ the job's documents, the Client Information attachments → the customer's documents, with what Maconomy
gives back kept in the portal. The user's answers the same day: Popup 1's values are years (yyyy); the
marketing string uses a comma and a space.

### The card fields (in `MaconomyJobDataMapping`)

| Portal field | Card field | Rule |
| --- | --- | --- |
| PO Start Date | `date4` | `ClientContract.PurchaseOrderStartDate` of the request's contract, as `yyyy-MM-dd`: the client form's Contract Details for a Government client, or the GCS card's PO Beginning Date (the same column) |
| PO End Date | `date5` | the same, for the end date |
| Marketing Methods | `text20` | the chosen methods' labels in the list's order, joined by ", "; whole labels only, within Maconomy's 255 characters |
| Fiscal Year End | `popup1` | the year (yyyy) of the Tax card's Fiscal Year End, else, on an Assurance engagement, of its Fiscal Year End of Client. Sent as Popup 1's literal for that year; left out when the list does not hold it |

All four are optional and left out when blank. `JobSyncGraph` loads the contract, the marketing methods
and the files; the confirmation before Sync job now shows the new values.

**Popup 1 on thmasd01 holds no years yet** (12 Nov 2026: its only value is "Copy Actuals"). A popup takes
one of its own literals, so until THF adds the years to that list the fiscal year is left off the job; the
confirmation says so ("2026 — not on Maconomy's Popup 1 list, so it is left out"). Nothing needs changing
in the portal once the years are there.

### Documents

In Maconomy a document is a line of a **document archive**, and a customer or a job points at its archive
by number ("Customer 900004", "Job 10102"). Everything that names a Maconomy container, field or action
for this is in `MaconomyDocumentMapping`; `MaconomyDocumentWriter` carries it out; `MaconomyDocumentSyncService`
decides which file goes when.

- **Which file goes where.** Attachments (Client Information tab) → the client's customer. The signed
  client acceptance form and the purchase order (Engagement Setup) → the engagement's job.
- **When.** Only for an approved request. Attachments once the client has a customer number — created by
  the customer sync, linked by email, or picked from Maconomy; job files once the job is created. A
  Hangfire job (`MaconomyDocumentSyncJob`, one attempt, no retry) is queued by the customer sync when it
  lands, by the job sync when it lands, and by the final approval when the client is already a customer.
  One run per request at a time.
- **The switches.** Customer documents follow the customer sync's Start/Stop, job documents the job
  sync's. There is no third switch.
- **Per file** (`REMS_File`, migration `AddRemsFileMaconomyDocument`): `MaconomyDocumentSyncStatus`
  (NotSynced, Syncing, Synced, Failed), `MaconomyDocumentArchiveNumber`, `MaconomyDocumentName`,
  `MaconomyDocumentKey` (the document's own key in Maconomy, where it gives one),
  `MaconomyDocumentSyncedOnUtc`, `MaconomyDocumentSyncError`. A failed upload never touches the customer or
  the job, which exist by then; it writes `RemsMaconomyDocumentSynced` / `…SyncFailed` to the activity log.
- **By hand.** Tenant Settings → Maconomy → Job: the Approved requests table has a Documents column
  ("2 of 3") and an Upload documents action (`POST /api/integrations/maconomy/documents/{remsId}/run`). Each
  file's row on the request carries a tag: where it sits in Maconomy, "Not in Maconomy", or the failure.
- **No doubles.** A copy of a request carries its source's files: an attachment already uploaded for the
  client (the same media) is marked with the same document instead of being sent again. A name the archive
  already holds is numbered ("scan (2).pdf"), since the name is half of the document's key.
- **Not done.** A file removed in the portal is not removed in Maconomy. A host without the file store
  (the Workers host) leaves uploads to the API, where the files are.

**The routes.**

1. The file: `POST /filedrop/{instance}/create` (File Drop 3.0, the `filedrop:create` link of
   `GET /filedrop/{instance}`), the bytes as the body, the name in `Content-Disposition`. The reply's
   `Location` is the drop.
2. Customer: open a `customercard` instance, load the customer (`…/data;customernumber={n}`), then
   `POST …/data/panes/card/0/action;name=attachdocument` with `Maconomy-File-Callback: <drop>`. Maconomy
   makes the customer's archive the first time.
3. Job: no job container has an attach action (jobs, jobcard, jobinformation, joboverview and others were
   read). The file is added to the job's archive through `documentarchives`: load the archive, or create it
   as "Job {number}" and point the job's `documentarchivenumber` at it, then "Add Document" on its table
   (`…/data/panes/table/init`, then `…/data/panes/table` with the file callback).

### What the read-only check of 12 Nov 2026 settled, and what it could not

Read from thmasd01 through the console harness (specifications, pick lists, one customer and one archive
loaded, card instances opened and deleted; nothing created):

- Every job card field name — the eight in use since October and the four above — is in the `jobs`
  specification and was accepted on opening an instance. A customer and a job have both been created from
  the portal on thmasd01 since (customers 900001 and 900005, job 10102).
- Popup values travel as literals (`country=fl`, `popup1=nil`); dates as `yyyy-MM-dd`; text fields are 255
  characters; a key that does not exist answers 204 with no body.
- The file drop, the customer's attach action and the archive's Add Document are where the instance's own
  links say they are.

**Not yet run against Maconomy:** the upload itself. Three things are unproven until the first file goes:
that the drop takes the file name from `Content-Disposition`; that `attachdocument` needs nothing but the
callback; and the whole job route (an archive the portal creates is not one of Maconomy's "standard"
archives, whose type cannot be set over the API — whether the job's Documents tab shows it is to be seen).
Whatever Maconomy answers is recorded on the file, and each of the three is one place in
`MaconomyGateway` or `MaconomyDocumentWriter` to change.

Checked on a scratch copy of the dev database against a stand-in for Maconomy built from the saved
specifications (36 checks): creates with the new fields, the two refusals, uploads in order, a refused
upload then a retry, the numbered name, the copy's attachment, the switch.

## 15. People, commission, fiscal month and year, template by name — BUILT 13 Nov 2026

Supersedes the earlier sections where they differ: the fixed project manager (section 11), the fiscal year
in Popup 1 (section 14) and "a Service Line without a number blocks the sync" (section 13).

| From | Job card field | Rule |
|---|---|---|
| Engagement Executive | `projectmanagernumber` | The Maconomy Number on the user record; left out without one |
| CSE | `accountmanagernumber` | Same |
| Billing Manager | `employeenumber6` | Same |
| Commission recipients 1–3 | `employeenumber8`, `9`, `10` | In the order added; same |
| Commission share 1–3 | `jobheaderextensionamount1var`, `2var`, `3var` (Amount 1–3) | The percentage, in hundredths as Maconomy holds an amount (40% → 4000); goes only with its recipient |
| Fiscal year end | `popup2` (month) and `theyear` | "December" looked up in `popup_jobpopuptype2`, the year as a number; nothing when the date is blank |
| Service Line | `templatejobnumber` | See below |

`Maconomy:JobCard:ProjectManagerNumber` and `DefaultTemplateJobNumber` are no longer read by the mapping.
`MaconomyCardWrite.NumberFields` names the fields the gateway sends as JSON numbers.

**Template job.** In order: the template picked for the request (`REMS.MaconomyTemplateJobNumber`), the
number on the Service Line under Option Sets, the one Maconomy template job (`template = true` on `jobs`)
whose name equals the Service Line's label. With none, the attempt fails with "No Maconomy template job is
named as the Service Line "…"" and nothing is posted. Sync job now then reads Maconomy's templates
(`GET …/maconomy-job-sync/templates`), asks which to use, and posts the pick with the press
(`{ templateJobNumber }`); the pick is checked against Maconomy's list and kept on the request. It is not
remembered for the Service Line (user's decision): an administrator sets the line's number for that.
A missing template or project manager no longer blocks the button; the sync finds out when it runs.

**A refused job.** When Maconomy refuses the create and people on the engagement have no Maconomy Number,
the failure reason ends with who they are and where to add the number (Administration → Users).

**Instance facts (thmasd01, read-only, 13 Nov 2026).** `popup_jobpopuptype2` holds January–December;
`theyear` is an integer; the card's Amount 1–3 are the `jobheaderextensionamount…var` fields; 38 template
jobs, numbered "ZZ" + their name. Employee fields are checked against employee records the integration user
can see, and it sees 24 of them, none of the account managers in use: a job naming one of those is refused
("There is no access to Employee …") until the Maconomy administrator grants access.

**Live on thmasd01, 13 Nov 2026:** job 10108 under test customer 900009, template found by name ("Tax
Compliance" → ZZTax Compliance), project manager 82, employees 6/8/9/10, `popup2` december, `theyear` 2026.
Two things it taught: an amount is a whole number of hundredths (35.5 was stored as 35; the shares were
rewritten as 4000 / 3550 / 2450 and read back so), and `accountmanagernumber` takes only an employee
Maconomy has set up as an account manager ("Employee no. 101 … is not set up as account manager") — none
of the 24 employees the integration user sees is one, so that field is unproven.

**Checked** without a database or Maconomy (30 checks): the mapping against the saved job specification,
template resolution, and the gateway sending the year and the amounts as numbers. Not run: the job sync
service end to end, and the pick dialog in a browser.

## 16. First-Year Fee Estimate on the job's budget — BUILT 13 Nov 2026

User's mapping: Job → Budget, activity type Amount, line type Outlay, task FEE, quantity 1, billing price =
First-Year Fee Estimate. Lives in `Mapping/MaconomyJobBudgetDataMapping.cs`.

| From | `jobbudgets` table field | Rule |
|---|---|---|
| (fixed) | `linetype` = `outlay`, `taskname` = `FEE` | Finds the line; not written |
| (fixed) | `numberof` | 1, as a number |
| First-Year Fee Estimate | `billingpricecurrency` | In hundredths, as a number (12,500.50 → 1250050); nothing is written when the estimate is blank |

**Instance facts (thmasd01, read-only, 13 Nov 2026).** A job created from a template already carries the
line: job 10108 (template ZZTax Compliance) has one budget line — activity FEE "Fee Estimate", type
`amount`, line type `outlay`, task `FEE`, quantity 1, billing price 10000. `activitytype` cannot be
written: Maconomy reads it from the activity. A line is saved in place with POST `…/panes/table/{row}`.
Adding a line was NOT made to work: the budget table has line number control and `…/table/init`
answers "A row parameter is required on record initialization" to `?row=end`, `?row=1`, `?row=0` and
`;row=end` (and 404 to the row in the path).

**So** the sync updates the template's FEE line and never adds one. When the budget has no Outlay line on
task FEE the attempt fails with a message saying so.

**Since 17 Nov 2026 (user: "add the line itself when it is missing")** a budget with no Outlay line on
task FEE gets one, in one step: POST `…/panes/table` with `{"row":"end","data":{line type, task,
quantity 1, price}}` and no init before it (after an init Maconomy answers "A row parameter is required on
record initialization"). Proven on thmasd01, 17 Nov 2026: job 10119, whose budget was empty, took the line.

**Flow.** After the job create is recorded, `REMS.IsMaconomyJobBudgetSyncPending` is set when there is an
estimate, and the line is written. A failure leaves the job number in place, marks the job sync Failed
with "Job N is in Maconomy, but the First-Year Fee Estimate could not be put on its budget: …", and the
next Sync job now (or queued run) does only the budget — no second job. The confirmation then reads
"Finish the Maconomy job / Set budget".

**Checked** without Maconomy (37 checks): the mapping, the gateway saving row 1 of a two-line table with
only quantity and price as numbers, and nothing written when no line matches. NOT run: the budget write
against Maconomy, and the sync service end to end.
