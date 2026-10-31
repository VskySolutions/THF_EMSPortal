# Posting a REMS engagement to Maconomy as a job — plan

**Status: BUILT on 31 Oct 2026, not yet run against Maconomy.** The user settled the shape (section 0)
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
3. **Sync is manual, per request.** No approval trigger, no Hangfire job: an eligible request is posted
   when somebody presses Sync job now, and a failed attempt stays failed until somebody presses it again.
   The button lives in the Actions column of the list pages (the Approvals list and EMS Review) and on the
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
with the reason; the engagement section repeats it read-only with a sentence. The lists (Approvals, EMS
Review, My Requests) get a "Maconomy job" column beside the customer one; the Approvals list and EMS
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
| `templatejobnumber` | the request's Job Template → the Job tab's map; else the tab's default template | required; refused when neither resolves |
| `customernumber` | `Client.MaconomyCustomerNumber` | required; refused when blank |
| `projectmanagernumber` | the Job tab's project manager number | required; 82 by default |
| `jobname` | "{client name}.{Service Line label}" — a person as "LastName FirstName", an organisation as its name (user, 31 Oct 2026) | refused when the request names no Service Line |
| `jobnumber` | — | sent empty; read back from the reply |

Not sent in this phase: company, location, job group (the template job fixes them — the probe's dry run
shows what it derives), dates, fees, billing, personnel rates, tasks, the REMS number (7.3). Each is a
later phase (section 8), none blocks the first job.

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
- Optional, cheap: a "Maconomy job" column on EMS Review and the Approvals list beside the customer
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
3. **The REMS number is not sent** in this phase, matching the customer decision of 22 Oct. Recommended
   for a later cut: a text field on the job (`description1` or whatever the spec names) carrying the REMS
   number, which gives a duplicate check on re-sync and a way to find the portal's job from Maconomy's side.
4. **Tab gating** = a saved, enabled connection. (Alternative: a token must have been issued at least
   once, i.e. Test connection passed, which would also lock the tabs again after a password change.)
5. ~~No Start/Stop switch~~ DECIDED 31 Oct 2026: Start/Stop on the Job tab; while stopped the Sync job
   now action is hidden, not disabled.
6. **Project manager is a plain number field** (default 82), not a picker from Maconomy's employees.
7. ~~Where Sync now lives~~ DECIDED 31 Oct 2026: only in the Actions column of the list pages (Approvals
   list, EMS Review) and on the Job tab's table. The request page shows the chip only.

---

## 8. Later phases

- **Link by hand**: type an existing job number onto a request; the portal reads it back to check it
  belongs to the client's customer. Needs `ReadByNumberAsync`, which the writer gets in phase 1.
- **Approval trigger**: post the job on full approval, queued the way the customer is — only if THF wants
  it after seeing the manual flow.
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
