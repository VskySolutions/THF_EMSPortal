# Job Type on the Engagement Setup — plan

**Status: BUILT on 11 Nov 2026**, the same day it was proposed, and corrected later that day (section 9: the
template job follows the Service Line, and the Job Type goes to the job card's specification 2). The user cut the plan down before it was
built (section 0); section 2 keeps the service line table that was proposed and NOT built, for the record;
section 8 says what was built, where it departs from the plan, and how it was checked.

Until this change the Engagement Setup asked for a **Job Template** (`REMS.JobTemplate`, 17 values THF
supplied), the picker narrowed it to the templates THF's department table pairs with the chosen Department
and Service Line, and the Maconomy job sync posted the job against the template job whose number the
chosen value carries (`OptionSetItems.MaconomyNumber`, section 10 of `REMS_MACONOMY_JOB_SYNC_PLAN.md`).

## 0. What was decided on 11 Nov 2026

- **Job Template goes, Job Type comes**: a dropdown with THF's 46 values, in the order given, labels verbatim.
- **No dependency between Service Line and Job Type.** The user: *"Lets not keep any dependency between
  service line and Job Type. Keep them independent."* No narrowing, no auto-fill, no tax-form derivation.
- **The Maconomy template job comes from the Job Type**, through the number on its option item, as the
  template did. The user fills the numbers in, except where I could match them to Maconomy's template jobs:
  *"if you find matches then update them, rest I will update manually."*
- **The tax job types all take ZZTax Compliance**, Maconomy's one tax template; **Estate Planning and
  NONCHARGEABLE - INTERNAL IT stay empty** (the user, later the same day: *"For tax related keep ZZTax
  Compliance. 2 and 3 Keep empty"*).
- Not asked, so kept as today: Job Type is **optional** on the form and before approval; a request without
  one is refused by the job sync with its reason, unless a default template job is configured.

## 1. How it works

**On the form.** Department → Service Line as before (the department still narrows the lines). Then
**Job Type**, a plain select over the whole list, optional, with the info text saying what it is and that
its Maconomy number is the template job. Nothing clears or fills it.

**On the job card.** `MaconomyJobDataMapping.ResolveTemplate` reads the request's Job Type and sends the
template job number its option item carries, else the configured default. The Job tab counts *"n of 46
job types carry a Maconomy template job number"*; its table and the sync preview show the Job Type; the
mapping card says "The Job Type's Maconomy number, else the default template job".

**The numbers.** THF's template jobs on thmasd01 are numbered **"ZZ" + the job's name** (section 7).
Where a Job Type has the same name as a template job, or is the same work renamed, the number is seeded
in `DefaultOptionSets` and written by the migration: 31 of 46 by name, and the thirteen tax job types all
take ZZTax Compliance, Maconomy's one tax template: 44 of 46. Estate Planning and NONCHARGEABLE - INTERNAL
IT stay empty on the user's decision, so a request under either falls to the default template job, or is
refused by the sync while none is configured.

## 2. The service line table — proposed, NOT built

Kept so the proposal is on record. Department → Service Line → Job Types, default first:

| Department | Service Line | Job Types offered |
|---|---|---|
| Tax | Tax Compliance | by the Tax Form: 1040 → 1040 - Individual, 1041_trust → 1041 - Trust, 1041_estate → 1041 - Estate, 1065 → 1065 - Partnership, 1120 → 1120 - C Corporation, 1120_pc → 1120-PC, 1120_s → 1120-S, 990 → 990 - Tax-Exempt, 5500 → 5500 - Employee Benefit Plan, the rest → Tax Other; 706, 709 and 8752 by hand |
| Tax | Consulting | Consulting*, Business Valuation |
| Tax | Mergers & Acquisitions | Mergers & Acquisitions* |
| Tax | Plan Administration | Plan Administration and 5500* |
| Tax | Employee Benefits | Plan Administration and 5500*, 5500 - Employee Benefit Plan |
| Tax | Estate Planning | Estate Planning*, 706 - Estate, 709 - Gift, 1041 - Estate, 1041 - Trust |
| Assurance | Assurance Services | Audit*, Review, Compilation, Examination, Agreed Upon Procedures, Peer Review |
| Assurance | Consulting / IT Services / SOC / Litigation Support / Forensic Accounting | the namesake |
| Assurance | Employee Benefits | Audit*, 5500 - Employee Benefit Plan, Plan Administration and 5500 |
| GCS | Assurance Services, Consulting | Governmental Consulting Services* |
| GCS | IT Services | IT Services* |
| CAS | Client Accounting Services, Outsourced CFO | Client Accounting Services* |
| CAS | Mergers & Acquisitions / Consulting | the namesake |
| Admin | Non-Chargeable Internal | Nonchargeable - Administrative*, then the other fifteen |
| Admin | Internal-IT | NONCHARGEABLE - INTERNAL IT* |

If THF ever wants it, it goes back as `REMS_JOB_TYPES_BY_SERVICE_LINE` in `useRemsMeta.js` beside the
department table, with the narrowing and clearing code the Job Template had (in git history, before this
change).

## 3. Data

**`REMS.JobType`** ("REMS Job Type"), a platform list in `DefaultOptionSets`, custom order, open (not
closed, seeded values not locked: nothing on the server branches on a code). The 46 values in the order
given, labels exactly as supplied. Codes: the tax-form codes are reused where it is the same form
(`1040`, `1041_estate`, `1041_trust`, `1065`, `1120`, `1120_pc`, `1120_s`, `990`, `5500`), the rest are the
label in snake case (`tax_other`, `706`, `709`, `8752`, `plan_administration_and_5500`,
`nonchargeable_internal_it`, …). `BootstrapSeeder` adds the list to a catalog that lacks it and
`TenantOptionSetSeeder` copies it to a new tenant; THF holds no tenant copy of any REMS list.

**`REMS.JobTypeId`**, the renamed `JobTemplateId` (same column, index `IX_REMS_JobTypeId`, foreign key
`FK_REMS_OptionSetItems_JobTypeId`).

**Migration `20261111110241_AddRemsJobType`**, EF-scaffolded for the rename, with the data steps as SQL:
1. Insert `REMS.JobType` and its 46 items where the key is missing (the backfill needs the ids before the
   API has started).
2. Rename the column, index and foreign key.
3. Remap every request that named a template to the job type that stands for it: Audit, Examination,
   Compilation, Review, Peer Review, Agreed Upon Procedures, SOC, Business Valuation, Forensic Accounting,
   Client Accounting Services and Governmental Consulting Services to their namesakes; Litigation →
   Litigation Support; Information Technology Services → IT Services; Mergers and Acquisitions → Mergers &
   Acquisitions; Consulting Engagement → Consulting; Pension Administration and Tax Compliance → Plan
   Administration and 5500; Tax Compliance → the job type of the request's tax form, Tax Other without
   one. Anything left pointing at a template is emptied. The template's Maconomy number is carried to the
   job type where it has none (ZZAudit). The `REMS.JobTemplate` set and items are soft-deleted.
4. The 44 template job numbers (section 7) onto the job types that lack one.

`Down` renames the schema back and leaves the data. The migration's SQL is the server's data step.

## 4. API

- `RemsRequest.JobTypeId` / `JobType`; `RemsModelConfigurations`; `RemsOptionSetKeys.JobType`.
- `RemsEngagementModels` (`JobType` in and out), `RemsEngagementController` save, `RemsEngagementValidators`,
  `RemsWorkspaceMapper`, `RemsApprovalModels` + controller packet, `RemsRequestsController` copy, the three
  repository includes.
- Maconomy: `MaconomyNumberMaps.JobTypeSetKey`, `MaconomyNumberMap.JobTypes` (`TemplateMap` keyed by job
  type item), `MaconomyJobDataMapping.ResolveTemplate` + `Describe`, `MaconomyJobSyncRules` messages,
  `RemsMaconomyJobSyncModels` (`JobType`, `MaconomyJobTypeRow`, overview `JobTypes`),
  `MaconomyController.JobSyncOverviewAsync`.
- Comments and doc strings that named the template, including `MaconomyConnection`, `MaconomyOptions`,
  `OptionSetItem.MaconomyNumber`, `IMaconomyNumberMaps`, `IMaconomyJobSyncService`, `IRemsRequestRepository`.

## 5. Web

- `useRemsOptionCatalog.js`: `jobType: "REMS.JobType"`.
- `useRemsMeta.js`: `REMS_JOB_TEMPLATES_BY_SERVICE_LINE`, `CLAIMED_JOB_TEMPLATE_CODES`, `jobTemplatesFor`,
  `remsJobTemplateOptions` and `remsJobTemplateFitsServiceLine` removed; `jobTypeLabel`, `jobTypeOptions`.
- `EngagementSetupForm.vue`: the Job Type select (v-model, every value, optional); the job template
  reconciliation, hint and cleared-note removed; the department and service line handlers no longer touch it.
- `RemsRequestForm.vue` (wiring, View row), `ApprovalTaskDetail.vue` (packet row), `MaconomySettingsPage.vue`
  (column, count line, card wording, `job.jobTypes`), `api.js` comments, `OptionItemDialog.vue` hint.

## 6. Build order (as done)

1. Rename sweep over API and web, scaffold the migration (`dotnet ef migrations add … --configuration
   Release`), write its data steps, `database update` on `THF_EMSPortal_New`.
2. The probe (section 7), then the numbers into `DefaultOptionSets` and a fourth migration step, applied
   to the local catalog by the same SQL.
3. Docs, memory.

## 7. The probe — run 11 Nov 2026

Read-only, through a console harness over `AddApplication` + `AddInfrastructure` (with a stub
`IBackgroundJobClient`, which `EmailDispatcher` demands), using the one enabled connection on dev
(thmasd01, on the VSky Solutions tenant). `POST /containers/thmasd01/jobs/filter`:
- `templatejob = true` → 400, "Invalid expression in the 'restriction' parameter".
- **`template = true` → 38 template jobs**, every number "ZZ" + the job's name: ZZAgreed Upon Procedures,
  ZZAudit, ZZBusiness Valuation, ZZClient Accounting Services, ZZCompilation, ZZConsulting Engagement,
  ZZExamination, ZZForensic Accounting, ZZGovernmental Consulting Services, ZZInformation Technology
  Services, ZZLitigation, ZZMergers and Acquisitions, ZZPeer Review, ZZPension Administration and Tax
  Compliance, ZZReview, ZZSOC, ZZTax Compliance, and 21 Nonchargeable ones (Accounting Department,
  Administrative, Administrative Tasks, Chamber of Commerce Conference, Client Social, Education, FINREP,
  Firm Forum, Holiday Party, INTERNAL FIRM ACCOUNTING, Leave Time, Marketing, Meetings, Personnel,
  Practice Development, Professional, Recruiting, Recruiting and Activities, Shareholder Meetings,
  Synergy, Travel).

Matched and seeded (31): the 26 whose name is a job type's name, and five that are the same work renamed —
Consulting ← ZZConsulting Engagement, IT Services ← ZZInformation Technology Services, Litigation Support
← ZZLitigation, Mergers & Acquisitions ← ZZMergers and Acquisitions, Plan Administration and 5500 ←
ZZPension Administration and Tax Compliance. The user then decided the rest: the thirteen tax job types
take ZZTax Compliance, Maconomy's one tax template (44 seeded in all); Estate Planning and NONCHARGEABLE -
INTERNAL IT stay empty. Template jobs with no job type: the six Nonchargeable ones not on the list of 46.

## 8. Built, 11 Nov 2026

Everything in sections 3 to 5, uncommitted on `feat/rems-data-model-v2`. Departures from the first
proposal: no service line table, no auto-fill, no tax-form rule on the form (the tax-form rule survives
only as the one-time remap of the six Tax Compliance requests in the migration); Job Type not mandatory.

Checked:
- Migration on `THF_EMSPortal_New`: 46 items under `REMS.JobType`, `REMS.JobTemplate` retired with 0 live
  items, 22 of 98 requests carry a job type (Consulting 4, Governmental Consulting Services 4, Tax Other 4,
  Plan Administration and 5500 3, 1040 - Individual 2, Audit 2, Client Accounting Services 2, IT Services
  1), `JobTemplateId` gone, Audit carries ZZAudit; 44 job types numbered after step 4 (31 by match, the
  thirteen tax types on ZZTax Compliance), Estate Planning and NONCHARGEABLE - INTERNAL IT empty; the
  numbers SQL is idempotent (a rerun sets 0).
- API builds in Release with 0 errors; eslint clean on every changed web file; no `JobTemplate` or "Job
  Template" left outside migrations, history scripts and this doc's section 2.
- Not run: the in-process controller harness and the browser; the setup save, the copy and the packet are
  renames of fields that worked under the old name, and the form is a v-model select with no logic.

Still to do: the REMS workflow guide and the deployment guide docs (the migration is a data step at the
next deploy). Nothing on the numbers: the two empty ones are empty by decision.

## 9. Correction, later on 11 Nov 2026

The user: *"Service Line will go as Job Template while creating job in maconomy not Job Type. And Job
Type value will go as specification2name while creating job."* So:

- **`templatejobnumber` is the Service Line's Maconomy number**, else the configured default. The map is
  `MaconomyNumberMap.TemplateMap` keyed by Service Line item, `ResolveTemplate` reads `ServiceLineId`, the
  eligibility message says Service Line, and the Job tab counts service lines. Seeded on thirteen lines from
  THF's department table where a line had one template: Tax Compliance and Estate Planning → ZZTax
  Compliance; Client Accounting Services → ZZClient Accounting Services; Consulting → ZZConsulting
  Engagement; Business Valuation; IT Services → ZZInformation Technology Services; Plan Administration and
  Employee Benefits → ZZPension Administration and Tax Compliance; Mergers & Acquisitions → ZZMergers and
  Acquisitions; Peer Review; SOC; Litigation Support → ZZLitigation; Forensic Accounting. Left empty, for the
  tenant: Assurance Services (six template jobs could apply), Non-Chargeable Internal (sixteen), and the two
  lines THF added by hand (Outsourced CFO, Internal-IT). Under GCS, Consulting now takes the Consulting
  Engagement template where the old table gave Governmental Consulting Services — one template per line.
- **`specification2name` is the Job Type**: the value's label, which is what Maconomy's job type list calls
  it (the 46 were supplied verbatim), or the item's Maconomy number where a tenant sets one to say it
  differently (`MaconomyNumberMap.JobTypeValueMap`, `ResolveJobType`). Left out when the request names no
  Job Type. The sync preview shows it as "Job type (specification 2)".
- The job type numbers seeded in the morning (31 matched, 13 on ZZTax Compliance) are **cleared**: on a
  job type the number now means a specification 2 override, and ZZ… is not one. The migration's step 4
  seeds the service lines instead; the catalog that had already run the first version was brought to the
  same state by hand.
- The field name `specification2name` follows the customer card, where the specification popups accepted
  their `…name` form; it is confirmed by the first live create, like the rest of the job card.
