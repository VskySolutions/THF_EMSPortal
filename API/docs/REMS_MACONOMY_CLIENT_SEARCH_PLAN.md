# REMS client search across system clients and Maconomy customers — working plan

**Status on 30 Sep 2026: plan only, nothing is built.** Proposed on 29 Sep, parked the same day, picked up
again on 30 Sep. Branch: `feat/gcs-rate-card-and-maconomy`.

This is a working note, not product documentation. Remove it, or turn it into a spec, once the feature
is built.

**Start here tomorrow:** settle decision 6 in [section 6](#6-decisions-waiting), then decisions 1–5.
Building starts at step 2 of [section 7](#7-build-order).

---

## 1. What is wanted

The Client box on the REMS request form should find a client in both places at once:

- clients the portal already holds ("system clients"), and
- customers in Maconomy.

Each row says which of the two it comes from, and otherwise shows what a row shows today (name, email,
entity type). Picking a Maconomy customer must leave the portal knowing which Maconomy customer the client
is, so the **Maconomy customer number is saved on the client's `Person` record**.

The requirement and the number-on-`Person` idea are the user's. Sections 3 and 4 are the proposed design.
None of the decisions in section 6 has been made yet.

---

## 2. Where things stand today

| | Today |
|---|---|
| System client search | `GET /api/rems/clients/lookup?q=` — `RemsRequestsController.ClientLookup` → `RemsRepository.LookupClientsAsync`. Searches the `Persons` that a request names as its client, on name, email and phone. 20 rows. Every term searches, however short. |
| Maconomy customer search | `GET /api/integrations/maconomy/customers?search=` — `MaconomyController.SearchCustomers` → `MaconomyCustomerService.SearchAsync`. Matches the number or the name. Nothing below 2 characters. Results are cached for 120 s (`Maconomy:SearchCacheSeconds`). Open to every signed-in user. |
| What a Maconomy customer returns | `value` (the customer number), `text`, `specification6Name`, `entityType` (the REMS code), `emailAddress`, `phoneNumber`. |
| The Client box | `WEB/src/modules/rems/components/ClientInformationFields.vue`. `runLookup` (500 ms debounce) calls `remsApi.clientLookup`; `pickClient` links a client on file; `startNewClient` starts a new one. |
| Saving the client | `RemsRequestsController.Create` / `Update` → `ResolveClientPersonAsync` writes the client's `Person`. A request holds no client fields of its own: it reads the name, email and mobile through `REMS.ClientPerson`. |
| Maconomy in the portal | Only on Tenant Settings → Maconomy (`/settings/maconomy`), as a trial picker. Nothing in the REMS module calls Maconomy. |
| Maconomy number stored | Nowhere. `Person` has no such column. |
| Read one customer by number | Not there. The service only searches. |
| Pause after a refused Maconomy login | Not there. Every search tries the login again (`MaconomySessionManager.LoginCoreAsync`). |

---

## 3. Proposed design

### 3.1 One search box, two sources

As the user types, the form asks the portal and Maconomy at the same time. System clients appear at once,
as now. Maconomy customers are added underneath when Maconomy answers (0.4–0.6 s, about 2.3 s when it has
to sign in first). If Maconomy is down or not connected, the box still works with system clients and says
that Maconomy could not be searched.

Each row keeps what it shows today and gains a source tag:

```
SYSTEM
 [org]    Acme Trading LLC                System     Commercial
          accounts@acme.example · Maconomy 10023A
 [person] Smith John Jr.                  System     Individual
          john@smith.example
MACONOMY
 [org]    ACME HOLDINGS, LLC              Maconomy   Commercial
          10023B · ap@acme.example
 [person] DOE, JOHN & JANE                Maconomy   Individual
          10450 · no email
 + Add "acme" as a new client
```

(The names and numbers above are made up.)

- A Maconomy customer already linked to a system client appears once, as the system client, with its
  number in the caption.
- Maconomy rows show the customer's own email (returned by the lookup since 29 Sep), or "no email".
- The two searches differ below 2 characters: the system search runs, the Maconomy search returns nothing.

### 3.2 Picking a Maconomy customer

The pick behaves like "New client", prefilled. The browser sends only the customer number. The server
reads name, email, phone and entity type from Maconomy by that number and fills the client's record. The
initiator corrects what needs correcting (see section 5: names, shared emails, phone formats).

### 3.3 What is stored on `Person`

- **`MaconomyCustomerNumber`**: a new column, unique per tenant among the records that have one.
- **The details themselves**, in the columns a client already uses: `CorporateName`, or `FirstName` and
  `LastName`; `PrimaryEmail`; `MobileNumber`. This is the portal's working copy.
- **What Maconomy last said** (the name at least; email and phone too if they are to be tracked) and when
  it was last checked. A later check compares against this, so a name corrected by hand is not reported
  as a change.

### 3.4 Save rules

On the first save of a request whose client was picked from Maconomy, the server:

1. Re-reads the customer from Maconomy by number, to confirm it exists.
2. Links the request to the system client that already holds that number, if there is one.
3. Otherwise links to an organisation on file under exactly that name with no Maconomy number yet, and
   adds the number to its record.
4. Otherwise creates a new client record holding the number.

From then on that customer is a system client.

### 3.5 Keeping the copy in step with Maconomy

When a linked client is used (turns up in a search, or a request for them is opened for editing), the
server re-reads the customer by number and compares it with what Maconomy last said.

- A changed name is shown as "Maconomy now has this customer as X — Update name". It is one click for an
  organisation. For an individual it opens the First/Last boxes, because the name has to be split by a
  person.
- The change is logged in the activity trail.
- The same check can catch a changed specification 6, or a customer that is no longer found in Maconomy.
- Later, if needed: a nightly check in the Workers process, for clients nobody searches for.

Whether a change is applied automatically or only offered is decision 4.

---

## 4. Why not "number only, everything else by API"

Raised on 30 Sep: save only the customer number on `Person` and read name, email and phone from Maconomy
each time. Saving the number is right. Reading everything else live, instead of storing it, does not fit
this codebase:

- **Lists.** The three REMS lists (requests, EMS Review, approvals) search, sort and filter by client name
  in SQL, on the computed column `Persons.ClientDisplayName` (`RemsRepository`, `RemsFormRepository`,
  `RemsApprovalRepository`). A client whose name exists only in Maconomy cannot be searched or sorted.
- **Emails and the intake form.** The intake link and its reminders go to `Person.PrimaryEmail`
  (`RemsFormController`), and the public form the client opens is locked to that email
  (`RemsPublicFormController`). With live reads, none of that works while Maconomy is down or its login
  is refused.
- **Maconomy's values need correcting.** See section 5. A correction needs somewhere to be kept.
- **Not every client is in Maconomy.** No client on file today has a number, and a brand-new client will
  not have one either. Their details must stay in the portal, so the code would handle two kinds of client
  in each of the 100+ places that read one.

Section 3 gives the same result (Maconomy stays the source of truth) while the lists, emails and the
client's form keep working from the portal's own database.

---

## 5. What live Maconomy data showed

Read-only samples taken on 29 Sep 2026.

**Names** (150 individuals in a sample of 808 customers)

- 148 are written "LAST, FIRST".
- 80 name two people, in the form "DOE, JOHN & JANE".
- Only 71 would pass the portal's first/last-name rules after a split at the comma. So a pick can prefill
  an individual's name but cannot lock it.
- 2 are evidently organisations filed as individuals.

**Email and telephone** (sample of 482 customers)

| | Customers |
|---|---|
| With an email | 410 |
| With a telephone | 458 |
| Sharing their email with another customer | 218 (42 addresses) |
| Email value holding more than one address | 3 |

- Shared emails collide with the rule that an email belongs to one client (decision 5).
- An email value with several addresses fails the request's email check, so the initiator has to pick one.
- Telephone is free text. Most are `999-999-9999`; some carry an extension or a note.

**Specification 6** is the customer's entity type, as a number: 1 Individual, 2 Government,
3 Not-for-Profit, 4 Insurance, 5 Commercial. **6 = Trust and Estate is inferred** (120 of 124 customers
carrying it are trusts or estates) and still needs confirming. The mapping is in
`MaconomyCustomerService.EntityTypeOf`.

**Connection.** The reconnect token lives about 15 minutes. A refused login is not throttled: with bad
credentials every search is one more failed login at Maconomy.

---

## 6. Decisions waiting

The recommendations are Claude's. None has been accepted yet.

| # | Decision | Recommendation |
|---|---|---|
| 1 | Request type on a first Maconomy pick | "New Engagement, Existing Client" (`existing_client`): the customer is already in THF's books, and nothing in the workflow branches on it. |
| 2 | Entity type that comes from Maconomy | Locked, as for a client on file. A wrong one is corrected in Maconomy first (about 1% of picks). |
| 3 | Individuals whose Maconomy name holds two people | The initiator names the one person. First/Last are prefilled but editable. |
| 4 | A change found in Maconomy later (a rename, for example) | Apply automatically for organisations; show it for someone to accept for individuals. Note: an automatic change also alters how already-approved requests read, because they take the client's name from the same record. |
| 5 | Shared emails | A Maconomy pick keeps its email even if another client holds it, because the number already identifies the customer. The one-email-one-client rule stays for clients typed in by hand. |
| 6 | Number only, or number plus a stored copy (section 4) | Number plus a stored copy, filled and refreshed by API. **Settle this first**: it shapes 3.2 to 3.5. |

---

## 7. Build order

1. ~~Confirm the email and phone fields on the Maconomy customer card.~~ Done on 29 Sep
   (`electronicmailaddress`, `telephone`).
2. Add the storage (section 3.3) and the save rules (section 3.4), with a read of one customer by number.
3. Add a REMS lookup for the Maconomy half, which hides customers already linked.
4. Change the dropdown: sections, source tags, the pick, and the note when Maconomy cannot be searched.
5. Pause after a refused Maconomy login. This box will be in daily use.
6. Keep the copy in step (section 3.5).
7. Test: system only, Maconomy only, both, already linked, Maconomy down, two-person name, same-name
   organisation, email already taken.

---

## 8. Where each change lands

**API**

- `EmsPortal.Domain/Entities/Person.cs` — the new columns.
- `EmsPortal.Infrastructure/Persistence/Configurations/PersonConfiguration.cs` — lengths, and the unique
  index per tenant. The file already has the pattern: `HasIndex(...).IsUnique().HasFilter("[Deleted] = 0")`.
- A new migration.
- `IMaconomyCustomerService` / `MaconomyCustomerService` — a read of one customer by number.
- `MaconomySessionManager.LoginCoreAsync` — the pause after a refused login.
- `IPersonRepository` / `PersonRepository` — find a client by Maconomy number.
- `RemsRequestModels.cs` — `CreateRemsRequestRequest` and `UpdateRemsRequestRequest` take the number;
  `RemsClientLookupItem` returns it for the caption.
- `RemsRequestsController` — a REMS-side Maconomy lookup beside `ClientLookup` (same permission,
  `rems.requests.create`); the save rules in `ResolveClientPersonAsync`; decision 5 in
  `RejectDuplicateClientEmailAsync`.

**WEB**

- `src/services/api.js` — `remsApi` gains the Maconomy half of the lookup.
- `src/modules/rems/components/ClientInformationFields.vue` — `runLookup` asks both sources; the menu
  gains sections and tags; a pick path for Maconomy rows; the failure note.
- `src/modules/rems/pages/RemsRequestForm.vue` — sends the number on save.

**Watch out for**

- The linked-customer filter must run after the 120 s search cache, not inside it, or a customer picked a
  minute ago is still offered as unlinked.
- `linkExactMatchIfSettled` auto-picks a sole exact-name match from `clientOptions` when the box is left.
  It should keep looking at system rows only.
- The request type is derived in `syncTypeToClient` and `typeDisabled`: a new client is "Brand-New Client".
  Decision 1 changes that for a Maconomy pick.
- The entity type lock is `entityTypeReadonly`, which reads the linked client's record. Decision 2 extends
  it to a Maconomy pick. The server enforces the same lock on save (`REMS_ENTITY_TYPE_OFF_RECORD`).
- Keyboard navigation (`moveActive`, `onClientEnter`) indexes into one flat list plus the "add new" row.

---

## 9. Not included

- Writing anything back to Maconomy.
- Bulk-linking the clients already on file to their Maconomy customers.

---

## 10. To confirm with THF

- A Maconomy customer number never changes once issued. The link depends on it.
- Specification 6 value 6 means Trust and Estate.
