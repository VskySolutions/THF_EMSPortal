# Posting a REMS client to Maconomy on full approval — plan

**Status: BUILT on 22 Oct 2026, not yet run against Maconomy.** Written 19 Oct 2026 on branch
`feat/rems-maconomy-client-search`; all seven decisions in section 5 were answered on 22 Oct 2026 and the
code in section 2 was written the same day (migration `20261022112337_AddRemsMaconomyCustomerSync`). What
remains is section 3: the first create against the test instance, which confirms the customer card's write
field names (`Maconomy:CustomerCard` in configuration) and the init-create / create link names the gateway
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
reason — read-only, no button. The REMS lists (My Requests, EMS Review, Approvals) get a small Maconomy
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
- Configuration: `Maconomy:CustomerCard` (container and the write field names: `name1`, `electronicmailaddress`,
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
4. **Address.** None is sent.
5. **Name format** for an individual: `LastName, FirstName Suffix`, as typed.
6. **Test instance.** The connection the tenant holds today is a test instance; the first create goes there.
7. **REMS number on the customer card.** Not sent. The double-create guard is the Syncing status plus the
   email check on re-sync.
