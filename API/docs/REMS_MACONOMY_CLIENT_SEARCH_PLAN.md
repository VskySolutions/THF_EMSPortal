# REMS client search across system clients and Maconomy customers — as built

**Built on 5 Oct 2026.** This replaces the working plan of 29–30 Sep. The open question that plan ended on
("number only, or number plus a stored copy") was settled as **number only**: for a client that is a
Maconomy customer the portal stores the Maconomy customer number and nothing else.

Example names and numbers below are made up.

---

## 1. What is stored

| | Client typed into the portal | Client that is a Maconomy customer |
|---|---|---|
| `Persons.MaconomyCustomerNumber` | null | the customer number |
| Name columns, `PrimaryEmail`, `MobileNumber`, `Suffix` | the client's details | **empty** |
| Where the name, email and phone come from | the row | Maconomy, read by the number |

One client per customer number in a tenant (filtered unique index). The request still points at the client
through `REMS.ClientPersonId`, so one Maconomy customer picked for several requests is one client.

What the workflow itself produces is still stored, for every client alike, because it is the record of
what happened rather than a copy of the customer:

- the entity type on the request's intake form (`REMSForm.EntityTypeId`), which decides the questions asked;
- the client's own answers on the intake form (`REMSFormSubmission`, `REMSClient`, `REMSEntity`), including
  the address the form was locked to;
- the email log (`REMSFormEmailEvent.RecipientEmail`) and the queued email;
- the text of in-app notifications ("REMS-12 — Acme Holdings, LLC").

---

## 2. How it works

**Search.** The Client box asks two places at once and shows one list. Each row carries an `Unapproved` tag
(a client the portal holds, not yet in Maconomy) or a `Maconomy` tag; a Maconomy row also shows the customer
number.

```
 [org]    Acme Trading LLC                         Unapproved Commercial
          accounts@acme.example
 [org]    ACME HOLDINGS, LLC                       Maconomy   Commercial
          10023B · ap@acme.example
 [person] DOE, JOHN & JANE                         Maconomy   Individual
          10450 · no email
```

A client the search does not find is added with the **New client** button beside the box; the list itself
offers no "add as new" row.

- `GET /api/rems/clients/lookup?q=` — clients the portal holds (unchanged; Maconomy clients are not in it).
- `GET /api/rems/clients/maconomy?q=` — Maconomy customers, by name, number, email or telephone. The name
  is matched word by word in any order, so "john smith" finds "SMITH, JOHN". Two characters minimum. Always
  a 200: `status` is `ok`, `notConnected` or `unavailable`, and an unreachable Maconomy is a note under the
  portal's own results.

**Pick and save.** A Maconomy pick sends `{ type, maconomyCustomerNumber }` and nothing else. On save the
server reads the customer from Maconomy by number (so a number that is not a customer is refused), reuses
the client already holding that number or creates one holding only the number, and files the request as
"existing client". Anything typed is ignored, and nothing is ever written to a Maconomy client's record.

**Reading.** Wherever a client is read, a Maconomy client's name, email and phone come from memory:

- `IMaconomyClientDirectory` holds, per tenant, what Maconomy says about the customers the tenant's
  clients are. Nothing in it is written to the database.
- `[LoadMaconomyClients]` on the REMS and Persons controllers makes sure it is loaded before an action runs.
- `MaconomyClientInterceptor` puts the details on each `Person` as EF reads it, so `REMS.ClientDisplayName`,
  `CustomerEmail` and `CustomerMobileNumber` work everywhere they did.

**Lists.** SQL cannot search or sort a name it does not hold. For the four REMS lists and their Client
filters, the search is answered as "the customer numbers that match", and a sort by client is worked out
in memory over the whole filtered set before the page is read (`MaconomyClientReader`).

**On the form.** A Maconomy client's name, email and phone are read-only, with **Refresh from Maconomy**
to re-read them at once. The entity type comes from Maconomy's specification 6 and is locked; a customer
with none has it chosen by hand. The industry is filled from specification 1 (THF's values 17–38, mapped to the
REMS.Industry codes) when the tenant's list offers that trade under the entity type; otherwise it is left
for the initiator to choose.

**Sending.** Send and Remind read the customer from Maconomy at that moment, so the email goes to the
address Maconomy holds today. No address, or one that is not a single valid address, is refused with a
message naming the customer number.

**When Maconomy cannot be reached**

| | What happens |
|---|---|
| Lists, request pages, approvals | Keep working from what was last read. |
| The search | The portal's own clients are listed, with a note that Maconomy could not be searched. |
| Picking a new customer | Refused with "Maconomy could not be reached"; nothing is saved. |
| Send / Remind | Go ahead on details read within the cache lifetime; refused if they are older. |
| After a restart, before Maconomy is back | The name reads "Maconomy customer 10023B" and the contact details are blank. |
| The client's own intake link | Still opens; the email falls back to the address the form was sent to. |

---

## 3. Keeping calls to Maconomy down

Measured against THF's Maconomy on 5 Oct 2026 (5,087 customers): a search or a read by number takes about
0.3 s; the first call after a sign-in about 2 s; every customer in one call about 4 s.

| What | Calls to Maconomy |
|---|---|
| Opening a list, a request or an approval, however many Maconomy clients are on it | 0 |
| Keeping those details fresh | 1 per 100 clients, every 10 minutes, in the background, only while REMS is in use |
| A search for a new term | 1 |
| The same term again within 2 minutes | 0 |
| A longer term, once a shorter one came back whole ("acme" then "acme h") | 0 |
| Picking a customer and saving the request | 1 |
| Every auto-save after that | 0 |
| Send, Remind, Refresh from Maconomy | 1 each |

The techniques behind those numbers:

1. **One in-memory copy per tenant**, read by number in batches (`customernumber = 'a' or …`, 100 per call).
   Reads never wait on a refresh: an old copy is served while the new one is fetched behind the request.
2. **Search results cached** for `SearchCacheSeconds`, and a longer term answered from a shorter term's
   complete result without a call.
3. **The browser** waits 500 ms after the last keystroke, asks Maconomy only from two characters, remembers
   each term for 2 minutes, and stops asking altogether for a tenant with no Maconomy connection.
4. **A pause after a failure** (`FailureCooldownSeconds`): after a refused sign-in or a timeout Maconomy is
   left alone for a minute. Without it every search is one more failed sign-in on the service account.
5. **Fewer fields per call**: only the five the portal shows are asked for.
6. **Optional: hold every customer** (`PreloadAllCustomers`). One call of about 4 s every 10 minutes, after
   which no search calls Maconomy at all. Off by default, because it keeps the whole customer list in the
   API's memory and a customer created in Maconomy is not found until the next refresh.

---

## 4. Settings (`Maconomy:` in appsettings)

| Key | Default | Meaning |
|---|---|---|
| `CustomerCacheSeconds` | 600 | How long client details are served before being re-read. |
| `NumbersPerCall` | 100 | Customer numbers asked for in one call (capped at 200). |
| `FailureCooldownSeconds` | 60 | Pause after a refused sign-in or an unreachable Maconomy. |
| `SearchCacheSeconds` | 120 | How long a search result is reused. |
| `PreloadAllCustomers` | false | Hold every customer in memory; searches then make no call. |
| `PreloadMaxRows` | 50000 | Row cap for that one read. |

---

## 5. Where the code is

**API**

- `Person.MaconomyCustomerNumber`, `Person.Maconomy`, `Person.ClientName/ClientEmail/ClientPhone`;
  migration `AddPersonMaconomyCustomerNumber`.
- `MaconomyClientDirectory` (the in-memory copy), `MaconomyCustomerService` (search, read by number, read
  all), `MaconomySessionManager` (the pause after a failure).
- `MaconomyClientInterceptor`, `LoadMaconomyClientsAttribute`, `MaconomyClientReader`.
- `RemsRequestsController`: `MaconomyLookup`, `MaconomyCustomer`, `PickMaconomyClientAsync`,
  `ResolveClientPersonAsync`. `RemsFormController.ClientEmailBlockedAsync`. `RemsPublicFormController`.

**WEB**

- `ClientInformationFields.vue` (merged list, pick, read-only fields, refresh), `RemsRequestForm.vue`
  (payload and checks), `services/api.js` (`maconomyClientLookup`, `maconomyCustomer`).

---

## 6. Defaults taken, and limits

Taken as the sensible default; each is one place to change:

- A Maconomy pick is filed as "New Engagement, Existing Client".
- The entity type from Maconomy is locked. Specification 6 value 6 is read as Trust and Estate (inferred).
- The name is shown exactly as Maconomy holds it ("DOE, JOHN & JANE"). The intake form offers an individual's
  name split at the comma for the client to correct.
- The one-email-one-client rule does not apply to Maconomy clients; many of them share an address.
- A Maconomy client cannot be edited on the Persons screen or made a user: both would store details.

Limits:

- A phone search matches the text as Maconomy holds it ("850-555-0100"), not digits typed without dashes.
- The Persons list shows a Maconomy client's name but sorts them as blank.
- Clients already in the portal are not linked to their Maconomy customers; that would be a separate job.
- The link depends on a Maconomy customer number never changing once issued (to confirm with THF).
