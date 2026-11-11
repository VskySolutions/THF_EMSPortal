# One intake per client — plan

**Status: BUILT on 7 Nov 2026** (planned and approved on 2 Nov 2026, held until the user asked for it).
The user settled the rules (section 0); section 7 lists the points decided by me rather than the user, and
section 8 what was built, where it departs from the plan, and how it was checked.

The intake form already lives on the client, not on the request: `RemsClientIntake` builds the form from
the client's rows and writes the client's rows back, and a request carries only when its form was sent,
saved and submitted. This plan makes the workflow say what the storage already does — a client fills the
intake once — and closes the gap it leaves today, where a returning client is sent the form again and
their new answers silently replace the old ones under every earlier request.

## 0. What was decided on 2 Nov 2026

- **Every client fills the intake form once.** A brand-new client, or a Maconomy customer who has never
  completed the intake in the portal, is sent the form. A client with a completed intake on file is not
  sent it again, and their request shows nothing about a form link.
- **Until a request has been sent to the client, nobody can correct the intake** — not the initiator, not
  an admin. On a returning client the on-file answers are shown, read-only.
- **Once it has been sent, either the client or a portal user can complete the intake form.** The client
  through their link, as today; a user from the request page, which was not possible before the client
  answered.
- **After sending, the only email the request offers is the reminder.** No re-send, no "ask the client to
  update their details".
- **Existing clients are backfilled:** anyone with a request the client ever submitted counts as done.
- The admin's correction of a completed intake from the Submitted COP Form pane stays as it is.

## 1. How it works

**A returning client.** The initiator raises the request as today and picks the client. From the first
save the page knows the intake is on file: the left pane shows the on-file answers read-only with the
caption *"Intake on file — submitted on 14 Oct 2026 via REMS-0031. It is not sent again."* The finishing
button on the Commission tab reads **Submit for review** instead of Submit to Client. Pressing it asks
*"&lt;client&gt;'s intake is on file since &lt;date&gt; (REMS-0031). This request goes to COP Review for an
admin to pick up; no form is sent. Continue?"* and the request moves straight to Admin Review, waiting for
pickup. No invite code is minted, no email is sent, no email event is written, and the request never
offers Email log, Client link or Send reminder. Everything from pickup onwards is unchanged: the admin
reads the same pane, may correct it, sends back, routes for approval.

**A new client, or a Maconomy customer with no intake on file.** Unchanged: Submit to Client emails the
link, the request waits for the client, reminders can be sent. New: from the moment the form is out, the
pane shows what the client has saved so far (or the empty form) and a user may **complete it for them**.
Saving it submits the intake on the client's behalf — the request moves to Admin Review exactly as a client
submission does, and the client's link reports the form as already submitted from then on.

**Afterwards.** The client's intake is on file for every later request. Corrections happen on a request,
from the pane, by an admin, as today.

## 2. The rule as data

| Where | Column | Meaning |
|---|---|---|
| `Clients` | `IntakeSubmittedOnUtc` | When the intake was completed; null until it is. THE flag. |
| `Clients` | `IntakeSubmittedRemsId` | The request it was completed on (FK → REMS). |
| `Clients` | `IntakeSubmittedByUserId` | The user who completed it for the client; null when the client did (FK → Users). |
| `REMS` | `IntakeOnFileFromRemsId` | On a request that reused an intake: the request it came from (FK → REMS). Null on a request that sent its own form. |
| `REMS` | `IntakeCompletedByUserId` | On a request whose form a user completed for the client (FK → Users). Null when the client submitted. |

Set `IntakeSubmitted*` on: the client's submit (public form), a user's completion (section 3), and the
backfill. Never on a draft save, never on a later correction. Both follow [column naming](../docs/DEVELOPMENT.md):
booleans would start with `Is`; these are dates and FKs, which end in `Id` and name their role.

Migration `AddRemsIntakeOnce`: adds the five columns, then backfills `Clients.IntakeSubmittedOnUtc` and
`IntakeSubmittedRemsId` from each client's earliest request with `FormSubmittedOnUtc` set (one `UPDATE …
FROM` with a `ROW_NUMBER` over `REMS` by `ClientId`). Applied by the API on its next start, like the
others.

## 3. API

- **`POST /api/rems/requests/{id}/submit-for-review`** (RemsFormController, beside `form/send`).
  Permission `rems.forms.send`, record rule as the send (the initiator or a privileged caller). Refuses
  unless the request is a Draft and its client has `IntakeSubmittedOnUtc`; runs the send's readiness
  checks minus the email address. Effects, in one transaction: `IntakeOnFileFromRemsId =
  client.IntakeSubmittedRemsId`, `FormSubmittedOnUtc = now`, status → `customer_submitted` with the status
  history row, the activity event, and the admin-pool notification the public submit sends. Stamping
  `FormSubmittedOnUtc` is what keeps every downstream surface unchanged — COP Review, Related Entities,
  the pane, the approval packet all key on it.
- **`POST form/send`** refuses a client with an intake on file: *"This client's intake is on file; submit
  the request for review instead."* The page never offers it, but the server says so too.
- **`GET requests/{id}/submission`** returns the current rows with `submitted: false` once the form is
  out, instead of 404 — the pane needs them to show what the client has saved so far.
- **`PUT requests/{id}/submission`** (today: the admin's correction, after submission) is widened: allowed
  from `awaiting_customer`, and when `FormSubmittedOnUtc` is null the save **completes** the intake —
  sets it, `IntakeCompletedByUserId`, the client's `IntakeSubmitted*`, moves the status to
  `customer_submitted` with history, activity and the same notifications as a client submission. The
  permission for that pre-submission completion is "may work the request at this stage" (initiator, CSE,
  REMS Admins, elevated) — see section 7. After completion the correction stays the admins' as today.
- **Reminder** endpoints unchanged (only while `awaiting_customer`, so never on a reused intake).
- **Detail** (`RemsRequestDetail`): `intakeOnFile { submittedOnUtc, remsId, remsNumber, completedBy }`
  from the client, `intakeReused` (bool), `formSent` (bool: an invite code exists). **Rows** on all four
  lists: `intakeReused`, so Email log hides and nothing form-related is offered.
- **Public form**: unchanged. A user's completion sets `FormSubmittedOnUtc`, which is already what makes
  the link answer "Submitted".

## 4. Web

- **Request page, Draft, returning client**: the pane appears (read-only, no Edit Form) with the on-file
  caption; the finishing button is Submit for review with a `useConfirm` instead of the Send COP dialog;
  `readyToSend` keeps every data check (CSE, entity type, industry, commission) but words the email check
  as a required field rather than "to send the form to". `canSendToClient` splits into itself (no intake
  on file) and `canSubmitForReview`.
- **Request page, form out with the client**: the pane appears from `awaiting_customer` with the caption
  *"The client has not answered yet. Complete the form for them, or wait."*; Edit Form is offered to the
  people in section 3; the dialog's save says it will submit on the client's behalf and confirms. After
  submission the pane and the dialog behave as today.
- **Hidden on a reused intake**: Email log (page and lists), Client link, Send reminder, the "sent"
  banners. `emsFormActivity(row)` returns false when `intakeReused`.
- **Lists**: COP State stays "Submitted" (section 7); Email log follows `intakeReused`.
- **Public form, approval task page**: unchanged.

## 5. Build order

1. Entity columns, migration, backfill.
2. `RemsDerived.IsIntakeOnFile`, the detail and row fields.
3. `submit-for-review`, the send refusal.
4. Submission GET for an unsubmitted form; submission PUT completion; permissions.
5. Request page (both scenarios), then the lists.
6. Plan status, memory, the demo guide's sentence on returning clients.

## 6. Checks before it is called done

- New client: send, reminder, client submit — unchanged, no email-event or status regressions.
- Returning client: pane read-only in Draft; Submit for review lands in COP Review waiting for pickup; no
  `RemsEmailEvents` row; Email log / Client link / reminder absent on the page and on every list.
- User completion: form out → Edit Form → save → Admin Review; the public link then reports Submitted;
  the pane names who completed it.
- Backfill: clients with `IntakeSubmittedOnUtc` = clients with at least one request submitted by the
  client, and the date is the earliest.
- Create COP on a related business still sends the form (a new client has no intake).
- The View page shows the pane and the on-file caption to everyone, with no actions.

## 7. Assumptions to confirm or change

1. **Who may complete a form that is out with the client**: the initiator, the CSE named on the request,
   the REMS Admins and the elevated roles. After completion, corrections stay the admins' (today's rule).
2. **A user's completion sends the client no email**; their link simply reports the form as submitted.
3. **COP State stays "Submitted"** for a reused intake; the request page says it was carried over. A
   dedicated "Intake on file" value would mean an option item inserted into every tenant's copy of the
   set (option sets are per-tenant copies).

## 8. Built, 7 Nov 2026

Migration `20261107133731_AddRemsIntakeOnce` adds the five columns of section 2 and backfills them. The
client's intake is on file from their earliest request whose own form came back (`InviteCode` set); a copy
submitted for review on it is marked as reusing it. The API applies it on its next start; for a scripted
deploy use `dotnet ef migrations script <previous> --idempotent` (the backfill is wrapped in `EXEC`).

Where the build departs from, or narrows, the plan above — my positions, not the user's:

- **A client with the form out on another request is not sent a second one.** A draft for them offers
  Submit for review, blocked with *"Waiting for the client's intake form, sent with REMS-n"* until that form
  comes in; `form/send` refuses it (`REMS_INTAKE_OUTSTANDING`). This is the "once" rule applied before the
  intake is on file; the plan only covered the on-file case.
- **A copy never sends the form** (the 5 Nov rule, kept): it waits for the request it was copied from, and
  the server now refuses the send too (`REMS_FORM_NOT_SENDABLE`), not only the page.
- **Who may finish a draft** (Submit to Client or Submit for review) is `RemsSetupAccess.CanWork`, the rule
  the send already applied: the initiator, the CSE on it, a REMS Admin, the elevated roles. The copy's
  narrower "creator or privileged" rule was widened to match.
- **Submit for review stamps `FormSubmittedOnUtc` with the time of the hand-off** (section 3), so COP Review
  lists it as received then; the pane and the View page show the intake's own date and request, read off
  the client. `IntakeOnFileFromRemsId` is what marks the request as having reused it.
- **Completing a form that is out** (`PUT requests/{id}/submission` while `awaiting_customer`) is open to
  `RemsSetupAccess.CanCompleteIntake` (assumption 1) with `rems.requests.update` or
  `rems.engagements.manage`. It runs through the same code as the client's own submission
  (`RemsIntakeSubmission`, shared with the public form): status, history, the people on the request told
  in-app and by the staff email, the admin pool told when nobody holds it — the completer is left out — and
  a `RemsFormCompletedByUser` activity row. The client is not emailed (assumption 2).
- **A reused-intake request stays off the Related Entities board** (assumption 7): the parties are listed
  under the request that asked for them.
- The "Corrected by" banner allows five seconds between the intake date and the client row's own stamp, so a
  submission or completion is not reported as a correction of itself.
- The COP Review client filter now admits requests that reached the queue without an invite.

Checked on 7 Nov 2026 with the in-process harness over a scratch copy of the dev catalog (section 6):
the copy of an unsent draft waits; a returning client's draft shows the on-file intake read-only, refuses
the send and a correction, submits for review with no email event and lands in COP Review flagged as
reused and off the Related Entities board; a draft for a client whose form is out waits, then submits once
a user completes that form (status, completer, the client's link reporting Submitted); the client's own
submission through the link stamps their intake with no user. The backfill marked 33 clients, the earliest
request each. Not walked in a browser; the dev catalog itself was not migrated.
4. **Submit for review uses the send permission** (`rems.forms.send`): it is the same finishing move.
5. **An on-file intake is reused whatever became of the earlier request** — declined, approved, or still
   in review.
6. **The entity type carries with the client** (it is locked on pick today), so a returning client is
   never asked under a different entity type.
7. **Related clients and additional individuals** named on the earlier intake are the client's; nothing
   about them is re-asked, and the Related Entities board keeps showing them under the earlier request.
