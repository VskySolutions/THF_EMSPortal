# Posting a REMS client to Maconomy on full approval — plan

**Status: BUILT on 22 Oct 2026; customers have been created from the portal on thmasd01 since (900001 on
30 Oct, 900005 on 2 Nov 2026). Name parts, addresses and documents added 12 Nov 2026 (section 6); the
document upload has not yet run against Maconomy.** Written 19 Oct 2026 on branch
`feat/rems-maconomy-client-search`; all seven decisions in section 5 were answered on 22 Oct 2026 and the
code in section 2 was written the same day (migration `20261022112337_AddRemsMaconomyCustomerSync`). What
remains is section 3: the first create against the test instance, which confirms the customer card's write
field names (constants in `MaconomyCustomerDataMapping` since 31 Oct 2026; only the container is configuration) and the init-create / create link names the gateway
follows.

Builds on the client search as built (`REMS_MACONOMY_CLIENT_SEARCH_PLAN.md`): the per-tenant connection,
`MaconomySessionManager` (token by age, pause after a failure), `IMaconomyClientDirectory.ReadAsync` (one
customer by number) and `Person.MaconomyCustomerNumber`. Nothing in the codebase writes to Maconomy yet;
the customer create is the one new piece of integration.

---

## 1. How it works

**Two standalone processes.** Approval is one; posting the customer to Maconomy is another. Approval never
waits on Maconomy, never fails because of it and is never re-run because of it. A sync that failed, or that
was switched off when the request was approved, stays that way until an admin re-syncs it by hand.

**Trigger.** The last approver's Approve call in `RemsApprovalController` already detects "fully approved"
(the `fullyApproved` block, around line 722) and sets the request to Approved. At that point a sync row is
recorded for the request when both hold:

- the request type is `brand_new_client` (`RemsRequestTypes.BrandNewClient`);
- the client is not already a Maconomy customer (`Person.MaconomyCustomerNumber` is null).

With the tenant's customer sync switched on, the row is Pending and the job is queued. With it switched off,
the row is Not synced and nothing is queued, then or later. Existing-client requests and clients picked from
Maconomy are never posted.

**The sync job** runs in the background in the Workers process, as email does. One attempt, in order:

1. Re-read the client's name, email, phone, entity type (the intake form's `REMSForm.EntityTypeId`) and
   industry (the engagement's `IndustryId`) from the portal. No address is read: none is sent.
2. Duplicate check by email: filter `customercard` by `electronicmailaddress`, compared exactly in memory.
   - no match: continue;
   - one match: create nothing, link the request to that customer (status Linked) and write its number
     onto the Person;
   - several matches: Failed, listing the numbers in the reason — the firm has to choose. Many of THF's
     customers share an email (218 of 410 in a live sample), so this case is real.
3. Mark the row Syncing, then create the customer with the containers API's init-create then create
   sequence. The customer number is sent EMPTY; Maconomy's number series assigns it. The fields sent are
   `name1`, `electronicmailaddress`, `telephone`, specification 6 as the entity type NUMBER (the reverse of
   `MaconomyCustomerService.EntityTypeOf`) and specification 1 as the industry NUMBER when the engagement
   has one (the reverse of `MaconomyCustomerService.Industries`, 17-38). Every other field takes Maconomy's
   defaults from the init template.
4. Read `customernumber` back from the create reply and save it as the sync row's `CustomerNumber` and the
   Person's `MaconomyCustomerNumber`. Then read the customer through `IMaconomyClientDirectory.ReadAsync`,
   which also seeds the in-memory directory so the lists can name them at once. A reply without a number
   is Failed.
5. Record the outcome: number, time and attempt count on success; the reason on failure.

**Name format** (`name1`). An organisation sends its corporate name as typed. An individual sends
`LastName, FirstName Suffix` — "Smith, John Jr." — as typed, not upper-cased.

**Failure.** Whatever the cause — Maconomy unreachable, a timeout, a refused token, a record Maconomy
rejected (its own `errorMessage` is the reason), an ambiguous email match, a client with no email — the row
is Failed after the one attempt, with the reason, and stays Failed. There are no automatic retries.

**Manual re-sync.** Sync now lives in TWO places only (user, 22 Oct 2026): the Actions column of the
Approvals list, for the approvers on the request, and the Maconomy settings page's Customer sync card, for
Tenant Admins and Super Admins. It runs the whole job INLINE in the API, so the caller sees the result
immediately and it works when the Workers process is not running. It is offered on any approved Brand New
Client request whose client holds no customer number: Failed and Not synced rows, and requests approved
before this feature existed (their row is created on demand). It needs the customer sync switched on. The
endpoint admits an approver on the request or anyone holding `rems.engagements.manage`.

**Double-create guard** (no REMS number goes to Maconomy). The row is Syncing before the create is sent. A
crash between the create and the record leaves it Syncing; the next Sync now's email check then finds the
customer just made and links it instead of creating a second one. A row left Syncing longer than a job can
run is shown as Failed ("the portal lost track of this attempt").

**Start and stop.** The Maconomy settings page gets a Customer sync card: Start/Stop, who changed it and
when, counts of synced, failed and not synced, and the last two listed with a Sync now on each row. Start
posts nothing by itself. Stop does not interrupt a job already running.

**What users see.** The request page header shows a Maconomy chip beside the status badge: "Maconomy
10023B", or Not in Maconomy, queued, posting, sync failed. The client section repeats it with the failure
reason — read-only, no button. The REMS lists (My Requests, COP Review, Approvals) get a small Maconomy
status column. Every attempt writes an activity event.

**After success** the portal client becomes a Maconomy client: the generated number is written onto the
Person (`Person.MaconomyCustomerNumber`). Nothing new is needed to keep them from showing twice in the client
search: the portal half (`RemsRepository.LookupClientsAsync`) already lists only people with NO customer
number, and the Maconomy half (`GET /api/rems/clients/maconomy`) already tags a customer whose number a
Person holds as on file and returns that Person's id, so picking them reuses the same record and every
earlier request stays attached. From the next search on, the synced client appears once, as a Maconomy row.
The typed name and email stay on the record as the fallback shown when Maconomy cannot be read (one-line
change in `Person.ClientName`: prefer the stored `ClientDisplayName` over the placeholder). If another Person
already holds that number (an email match to a customer somebody else picked), the number stays on the sync
row only.

---

## 2. What gets built

**Data** (one migration, `AddRemsMaconomyCustomerSync`):

- `REMSMaconomyCustomerSyncs`, one row per request: `REMSId` (unique), `ClientPersonId`, `Status`
  (NotSynced, Pending, Syncing, Synced, Linked, Failed), `CustomerNumber`, `Attempts`, `LastAttemptOnUtc`,
  `SyncedOnUtc`, `FailureReason`, `PayloadJson` (what was sent, for audit), audit columns.
- `MaconomyConnections`: `CustomerSyncEnabled` (default false), `CustomerSyncChangedOnUtc`, `CustomerSyncChangedById`.

**API**

- `IMaconomyGateway`: the create sequence THF's Maconomy team gave on 22 Oct 2026, spelt out rather than
  discovered from links — `POST .../dpl:customercard/instances` naming the card fields, `POST
  .../instances/{id}/data/panes/card/init` with `{}` for a blank template, `POST .../instances/{id}/data/panes/card`
  with `{"data": {...}}` to create — each carrying the previous reply's `Maconomy-Concurrency-Control`; a typed
  failure (`MaconomyFailure.Rejected`) for a record Maconomy refuses. (`init-create` is a different action: it
  validated and refused an empty record.)
- Application: `IMaconomyCustomerWriter` (duplicate check, create, read-back) and
  `MaconomyCustomerSyncService` (orchestrates and records; one code path for the job and for Sync now).
- `MaconomyCustomerSyncJob` in `EmsPortal.Infrastructure/Jobs`, beside `EmailSendJob`; enqueued with
  `IBackgroundJobClient`. No scheduled retries.
- `RemsApprovalController.Approve`: record (and enqueue when the sync is on) in the `fullyApproved` block.
- Endpoints:
  - `POST /api/rems/requests/{id}/maconomy-sync` (Sync now) — `rems.engagements.manage` (REMS Admin, Tenant Admin, Super Admin);
  - `GET /api/integrations/maconomy/customer-sync`, `POST .../customer-sync/start`, `POST .../customer-sync/stop`,
    `GET .../customer-sync/unsynced`, `POST .../customer-sync/{remsId}/run` (the settings page's Sync now) —
    `integrations.maconomy.manage` (Tenant Admin, Super Admin). ALWAYS the caller's own tenant: no `?tenantId=`
    override, unlike the connection endpoints (user, 22 Oct 2026).
- Configuration: `Maconomy:CustomerCard:Container` only since 31 Oct 2026; the write field names (`name1`, `electronicmailaddress`,
  `telephone`, `specification6name`, `specification1name` — the instance refused `specification6` as a card
  field on 22 Oct 2026, the popup column IS the `…name` field) and `Maconomy:CustomerSyncStaleMinutes` (10).
- Sync status on `RemsRequestDetail` and the list items; `ActivityEventTypes.RemsMaconomyCustomerSynced` /
  `...SyncFailed`.

**Web**

- `MaconomySettingsPage.vue`: the Customer sync card (switch, counts, unsynced list with Sync now).
- `RemsRequestForm.vue`: the chip in the header, the detail block and Sync now.
- `EmsReview.vue`, `PartnerDashboard.vue`: the status column.
- `services/api.js`: the calls.

**Not touched:** the approval route, the REMS statuses, the number-only rule for clients picked from Maconomy.

---

## 3. Before any write

- Step zero: `GET /containers/{instance}/customercard` (read-only, the container specification) to confirm
  field names (specification 6, specification 1), required fields and the create action links. Deltek's API
  guide is behind a login, so the sequence is confirmed against THF's instance, not from documentation.
- The connected instance is a TEST instance (confirmed 22 Oct 2026), so the first real create runs against
  it, pressed by the user.
- Workers must be rebuilt and restarted (the job class lives in Infrastructure), as with the email switch.

---

## 4. Build order

1. Migration and entities.
2. Gateway write calls, verified against the container specification.
3. Sync service and job.
4. Approval hook and endpoints.
5. The three screens.

---

## 5. Decisions (asked 19 Oct 2026, all answered 22 Oct 2026)

1. **Customer numbering.** Maconomy's number series assigns it. The portal sends the number empty and reads
   the generated one back from the create reply; the "availability check" from the original ask is dropped.
2. **One email match.** Link the request to that customer; create nothing.
3. **Approval and sync are standalone.** A sync that failed, or was off at approval, stays as it is. Start
   posts nothing by itself; an admin re-syncs by hand with Sync now once the sync is on.
4. **Address.** None is sent. SUPERSEDED 12 Nov 2026: the mailing and physical addresses go on the card
   (section 6).
5. **Name format** for an individual: `LastName, FirstName Suffix`, as typed.
6. **Test instance.** The connection the tenant holds today is a test instance; the first create goes there.
7. **REMS number on the customer card.** Not sent. The double-create guard is the Syncing status plus the
   email check on re-sync.

---

## 6. Name parts, addresses and documents — BUILT 12 Nov 2026

The user's instruction of 12 Nov 2026 ("New Mapping Confirmed For Maconomy Integration"), with three
points settled the same day: an individual keeps `name1` as it is AND sends the name in parts; the middle
name is skipped for now; the marketing string on the job uses a comma and a space. This replaces decision 4
of section 5 (no address is sent). Everything below is in `MaconomyCustomerDataMapping`.

| Portal field | Card field | Rule |
| --- | --- | --- |
| First Name | `customerremark27` | individuals only; `name1` stays "Last, First Suffix" |
| Last Name | `customerremark29` | individuals only |
| Suffix | `customerremark30` | individuals only |
| Mailing Address Line 1 | `name2` | |
| Mailing Address Line 2 | `name3` | |
| Mailing City | `postaldistrict` | |
| Mailing State or Country | `country` | United States: the state code. Any other country: the country as selected, the state not sent |
| Mailing Zip Code | `zipcode` | |
| Physical Address Line 1 | `customerremark20` | |
| Physical Address Line 2 | `customerremark21` | |
| Physical City | `customerremark23` | |
| Physical State or Country | `customerremark24` | the same rule, as text |
| Physical Zip Code | `customerremark25` | |

A blank is left out, as before. Nothing is updated on a customer that already exists (linked by email, or
picked from Maconomy).

- **The remarks are `customerremark…` on the API.** "Remark 27" is the field's title; the card refuses
  `remark27` when it is opened ("Invalid field name"). Every name above was accepted on opening a
  `customercard` instance on thmasd01 (12 Nov 2026, read-only: the instance was deleted, nothing created).
- **`country` is a popup, and a popup takes its own literal.** THF's list (`popup_countrytype`) holds the
  fifty states and DC by code and the countries by name; a record carries the literal (`fl`,
  `united_kingdom`), not the title (`FL`, `United Kingdom`). `IMaconomyPickLists` reads the list (kept
  `Maconomy:PickListCacheSeconds`, ten minutes) and the mapping sends the literal for the state code or the
  country name. A place the list does not hold stops the sync before anything is posted, with the place
  named in the reason ("…names "UM-81", which is not on Maconomy's Country list…"): the client's address is
  corrected, or the value added in Maconomy, and the sync pressed again. The physical address's
  `customerremark24` is free text, so it takes the code or the country as it is.
- **Mailing same as physical.** The form stores the mailing address as a copy when the box is ticked, so
  both sets go out. On dev every client with a physical address also holds a mailing one.
- **Middle name.** Not sent and not asked: neither form has the box. `customerremark28` is left for it.
- **Attachments.** The request's attachments are uploaded to the customer's documents once the client is
  in Maconomy: `REMS_MACONOMY_JOB_SYNC_PLAN.md`, section 14.
- `ClientRepository.GetSyncSubjectAsync` (the Sync now read) loads the addresses, as the queued run's
  `FullGraph` already did.

Checked on a scratch copy of the dev database against a stand-in for Maconomy that validates field names
against the instance's saved specifications: an individual and an organisation created with the values
above, a state code and a country the list does not hold both refused with the reason, a listed country
sent as its literal.

## 7. Contact persons, CSE, middle name, physical address first — BUILT 13 Nov 2026

Supersedes section 6 where the two differ (the address fields and the middle name).

**Order: contact company, then customer, then contact persons** (`MaconomyContactCompanyDataMapping`).
A customer and its contact company share one number, name, address, email and phone: across 5,092 pairs
on thmasd01 `name1`–`name3`, `zipcode`, `postaldistrict`, `country`, `telephone` and
`electronicmailaddress` never differ. Until 11 Nov creating a customer made the company by itself; on
13 Nov the live create was refused three ways, which fixed the order:

- "Next number is 0 for the number series Contact Company Series … no number has been specified": Maconomy
  no longer hands out the number, so the portal picks it: one more than the highest all-digit contact
  company number (lettered ones such as 20280A are sub-entities), stepped past any customer holding it.
- "Contact Company 900009 does not exist" when the customer named a company not yet there: the company is
  created first (`contactcompanys` card: number, `name1`, the physical address block, email, phone), then
  the customer with `customernumber` and `contactcompanynumber` both set to it.
- The customer's email and phone read back blank until they were put on the company: they live there.

The number is kept on the client (`Client.MaconomyContactCompanyNumber`) as soon as the company exists, so
a retry after a refused customer does not make a second company. Two syncs picking the same number at the
same moment is not guarded: Maconomy refuses the second company, and syncing again picks the next.

**The CSE fields need access in Maconomy.** `accountmanagernumber` was refused with "There is no access to
Employee 200366": the integration user sees 24 employees and none of the 16 account managers in use.
`specification5name` was refused with "Specification 5 82 does not exist": it is checked against
Maconomy's own Specification 5 list. Until the Maconomy administrator gives the integration user access to
the CSEs' employee records, a client whose CSE has a Maconomy Number fails at the customer step.

**Live on thmasd01, 13 Nov 2026:** contact company 900009, customer 900009 (made-up "ZZ COP Test" client,
without the CSE fields) and contact persons 83294–83298, whose numbers Maconomy assigned; a second attempt
added nobody.

**Customer card.**

| From | Field |
|---|---|
| Physical address (the primary one) | `name2`, `name3`, `postaldistrict`, `country`, `zipcode` |
| Mailing address (secondary) | `customerremark20`, `21`, `23`, `24`, `25` |
| Middle name (individuals) | `customerremark28` |
| CSE's Maconomy Number (`Users.MaconomyNumber`) | `specification5name` and `accountmanagernumber` |

A CSE without a number is left out of both. The country check now reads the physical address: a place
Maconomy's Country list lacks stops the sync and tells the user to ask the Maconomy administrator to add it.

**Contact persons** (`MaconomyContactPersonDataMapping`): rows of the table under the `contactcompanys`
card whose `contactcompanynumber` is the customer number.

| From | Field |
|---|---|
| What the person is | `remark2`: "Primary Client Contact", "Financial Contact", "Other Contact", "Trust And Estates", "Spouse And More Individuals" |
| First name, last name, suffix | `firstname`, `lastname`, `initials` |
| Email, phone | `electronicmailaddress`, `telephone` |
| Individual's type, filing type, minor, billing preference | `contactpersonpopup2`, `3`, `4`, `5` |

- Contact roles sent: primary client contact, financial contact, other contact, trust and estate contact.
  Any other role is not sent. Individuals are the spouse / child / other relationships; a related entity is not.
- A popup value goes only when Maconomy's list for it holds the value. On 13 Nov all four lists were empty,
  so none is sent yet.
- The step runs straight after the customer is created. `Client.IsMaconomyContactSyncPending` is set with
  the customer and cleared when the people are in. If the step fails the client shows Failed with
  "Customer N is in Maconomy, but its contact persons could not be added…", the job and documents are
  queued regardless, and Sync now runs the step alone. A row already under the company (same `remark2`,
  names and email) is not added twice.
- Clients linked to an existing Maconomy customer get no contact persons.
- Create only: a contact added in the portal after the sync is not sent.

**Middle name box.** On Client Information and the client's form, behind `SHOW_MIDDLE_NAME` in
`WEB/src/modules/rems/remsFeatureFlags.js` (off). While off the forms do not send the field, and an omitted
middle name leaves the record's alone.

**Checked** without a database or Maconomy: the two mappings on a made-up client and the real gateway's
`AddTableRowsAsync` against a stand-in that validates field names against the saved specifications
(36 checks), and one live create on thmasd01 through the mappings and the card writer. Not run: the sync
service itself end to end (its retry path, the columns it writes), and the browser.
