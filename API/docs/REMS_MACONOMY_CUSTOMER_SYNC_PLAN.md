# Posting a REMS client to Maconomy on full approval — plan

**Status: plan only, nothing built.** Written 19 Oct 2026 on branch `feat/rems-maconomy-client-search`.
Seven decisions in section 5 are open; the build starts once they are answered.

Builds on the client search as built (`REMS_MACONOMY_CLIENT_SEARCH_PLAN.md`): the per-tenant connection,
`MaconomySessionManager` (token by age, pause after a failure), `IMaconomyClientDirectory.ReadAsync` (one
customer by number) and `Person.MaconomyCustomerNumber`. Nothing in the codebase writes to Maconomy yet;
the customer create is the one new piece of integration.

---

## 1. How it works

**Trigger.** The last approver's Approve call in `RemsApprovalController` already detects "fully approved"
(the `fullyApproved` block, around line 722) and sets the request to Approved. At that point a sync is
recorded for the request when all three hold:

- the request type is `brand_new_client` (`RemsRequestTypes.BrandNewClient`);
- the client is not already a Maconomy customer (`Person.MaconomyCustomerNumber` is null);
- the tenant's customer sync is switched on.

Existing-client requests and clients picked from Maconomy are never posted. Approval never waits on
Maconomy and never fails because of it.

**The sync job** runs in the background in the Workers process, as email does. In order:

1. Re-read the client's name, email, phone, entity type (the intake form's `REMSForm.EntityTypeId`) and
   billing address (`REMSEntityAddress` of type Billing on the main entity) from the portal.
2. Duplicate check by email: filter `customercard` by `electronicmailaddress`, compared exactly in memory.
   - no match: continue;
   - one match: create nothing, link the request to that customer (status Linked);
   - several matches: fail, listing the numbers in the reason — the firm has to choose. Many of THF's
     customers share an address (218 of 410 in a live sample), so this case is real.
3. Customer number: propose the next free number, confirm it is unused with a read by number, re-propose
   on a collision. (Depends on decision 1.)
4. Create the customer with the containers API's init-create then create sequence, mapping `name1`, email,
   telephone, specification 6 as the entity type NUMBER (the reverse of `MaconomyCustomerService.EntityTypeOf`)
   and the billing address. Every other field takes Maconomy's defaults from the init template.
5. Record the outcome: number, time and attempt count on success; the reason on failure.

**Failure handling.**

| Kind | Examples | What happens |
|---|---|---|
| Transient | Maconomy unreachable, timeout, refused token | retried automatically 3 times with growing delays (1, 5, 15 min), then Failed |
| Permanent | Maconomy rejected the record, ambiguous email match, client with no email and no address | Failed at once, with Maconomy's own `errorMessage` as the reason |

A Retry button re-runs the whole job INLINE in the API, so the admin sees the result immediately and it
works when the Workers process is not running.

**Start and stop.** The Maconomy settings page gets a Customer sync card: Start/Stop, who changed it and
when, and counts of pending, failed and synced. While stopped, newly approved requests are recorded as
Held and not posted. Start queues every Held row. Stop does not interrupt a job already running.

**What users see.** The request page header shows a Maconomy chip beside the status badge: "Synced
19 Oct 2026 14:32 · 10023B", or Pending, Held, Failed. The client section repeats it with the failure
reason and a Retry button for admins. The REMS lists get a small Maconomy status column. Every attempt
writes an activity event.

**After success** the portal client becomes a Maconomy client: the new number is written onto the Person,
so the next request for them is picked from Maconomy and no second customer is ever created. The typed
name and email stay on the record as the fallback shown when Maconomy cannot be read (one-line change in
`Person.ClientName`: prefer the stored `ClientDisplayName` over the placeholder). If another Person already
holds that number (an email match to a customer somebody else picked), the number stays on the sync row only.

---

## 2. What gets built

**Data** (one migration, `AddRemsMaconomyCustomerSync`):

- `REMSMaconomyCustomerSyncs`, one row per request: `REMSId` (unique), `ClientPersonId`, `Status`
  (Held, Pending, Syncing, Synced, Linked, Failed), `CustomerNumber`, `Attempts`, `LastAttemptOnUtc`,
  `SyncedOnUtc`, `FailureReason`, `FailureKind` (Transient/Permanent), `PayloadJson` (what was sent, for audit),
  audit columns.
- `MaconomyConnections`: `CustomerSyncEnabled` (default false), `CustomerSyncChangedOnUtc`, `CustomerSyncChangedById`.

**API**

- `IMaconomyGateway`: the create calls (container instance, init-create, create) carrying
  `Maconomy-Concurrency-Control`; a typed failure (`MaconomyFailure.Rejected`) for a record Maconomy refuses.
- Application: `IMaconomyCustomerWriter` (duplicate check, number proposal, create) and
  `MaconomyCustomerSyncService` (orchestrates, records, decides transient vs permanent).
- `MaconomyCustomerSyncJob` in `EmsPortal.Infrastructure/Jobs`, beside `EmailSendJob`; enqueued with
  `IBackgroundJobClient`, transient retries by `BackgroundJob.Schedule`.
- `RemsApprovalController.Approve`: record + enqueue in the `fullyApproved` block.
- Endpoints:
  - `POST /api/rems/requests/{id}/maconomy-sync/retry` — `rems.engagements.manage` (REMS Admin, Tenant Admin, Super Admin);
  - `GET /api/integrations/maconomy/customer-sync`, `POST .../customer-sync/start`, `POST .../customer-sync/stop`,
    `GET .../customer-sync/failed` — `integrations.maconomy.manage` (Tenant Admin, Super Admin; `?tenantId=` as today).
- Sync status on `RemsRequestDetail` and the list items; `ActivityEventTypes.RemsMaconomyCustomerSynced` /
  `...SyncFailed`; `Maconomy:CustomerSync` options (retry delays, max automatic attempts).

**Web**

- `MaconomySettingsPage.vue`: the Customer sync card (switch, counts, failed list with Retry).
- `RemsRequestForm.vue`: the chip in the header, the detail block and Retry.
- `EmsReview.vue`, `PartnerDashboard.vue`: the status column.
- `services/api.js`: the calls.

**Not touched:** the approval route, the REMS statuses, the number-only rule for clients picked from Maconomy.

---

## 3. Before any write

- Step zero: `GET /containers/{instance}/customercard` (read-only, the container specification) to confirm
  field names (address fields, specification 6), required fields and the create action links. Deltek's API
  guide is behind a login, so the sequence is confirmed against THF's instance, not from documentation.
- The first real create needs a test instance or an agreed throwaway customer, pressed by the user.
- Workers must be rebuilt and restarted (the job class lives in Infrastructure), as with the email switch.

---

## 4. Build order

1. Migration and entities.
2. Gateway write calls, verified against the container specification.
3. Sync service and job.
4. Approval hook and endpoints.
5. The three screens.

---

## 5. Open decisions (asked 19 Oct 2026, not yet answered)

1. **Customer numbering.** Hand-assigned by THF with a rule, or Maconomy's number series? Existing numbers
   are 5–6 characters and not all digits, so "next free number" needs the rule. If Maconomy assigns, the
   availability check becomes reading the number back from the create reply.
2. **One email match:** link the request to that customer (recommended), or fail and let an admin decide?
3. **Approvals while stopped:** held and posted on Start (recommended), or skipped for good?
4. **Which address** goes on the customer card: billing (recommended) or physical?
5. **Name format** for an individual: "SMITH, JOHN" as Maconomy files people (recommended), or "John Smith"?
6. **Is there a test Maconomy instance?**
7. **A spare text field on the customer card** to hold the REMS number — makes the duplicate check exact and
   guards against a double create if the portal crashes between posting and recording.
