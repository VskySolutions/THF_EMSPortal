# COP Portal — User Guide

A practical, task-oriented guide to using COP Portal from a user's point of view. It explains
what the platform does, who can do what, and how to perform every common task — from your first
login to triggering an import and checking whether it succeeded.

> COP Portal automates importing financial data from **Concur** into **Maconomy** —
> expense reports, vendor invoices, and vendor payments. It also handles
> user/role administration, email, dashboards, and a cross-cutting **Universal Features** layer.
> You can drive it through the **web app** (`WEB/`, a Quasar/Vue SPA) or directly through the REST API
> — via the **Scalar API reference** in your browser, `curl`, or any HTTP client (Postman, Insomnia, a script).

---

## Table of contents

1. [Key concepts](#1-key-concepts)
2. [Who can do what (roles)](#2-who-can-do-what-roles)
3. [How you interact with the platform](#3-how-you-interact-with-the-platform)
4. [Getting started: your first login](#4-getting-started-your-first-login)
5. [Managing your account](#5-managing-your-account)
6. [Setting up a tenant (Super Admin)](#6-setting-up-a-tenant-super-admin)
7. [Connecting Concur and Maconomy (Tenant Admin)](#7-connecting-concur-and-maconomy-tenant-admin)
8. [Field mappings](#8-field-mappings)
9. [Managing users](#9-managing-users)
10. [Running imports](#10-running-imports)
11. [Monitoring jobs, logs, and retries](#11-monitoring-jobs-logs-and-retries)
12. [Scheduled (automatic) imports](#12-scheduled-automatic-imports)
13. [Understanding responses and errors](#13-understanding-responses-and-errors)
14. [Common workflows end-to-end](#14-common-workflows-end-to-end)
15. [Troubleshooting](#15-troubleshooting)

---

## 1. Key concepts

| Term | What it means to you |
|------|----------------------|
| **Tenant** | An isolated workspace — usually one client/company. All data, credentials, and users belong to a tenant. You only ever see your own tenant's data. |
| **Person** | The CRM master record for an individual (name, contact details, job info, optional tenant). A person exists on its own and is later **promoted to a user** (login account). |
| **User** | A login account, always linked to one **Person**. Created by selecting an existing person and assigning a tenant + role. |
| **Integration / Interface** | A type of import: **Expense import**, **Invoice import**, or **Vendor payment import** (Concur → Maconomy). |
| **Job** | A single run of an import. Every time you trigger an import — or the scheduler does — a job is created and runs in the background. Each job has an ID and a status. |
| **Credentials / Config** | The Concur and Maconomy connection details for a tenant. Stored encrypted; an import can only run once both are configured. |
| **Mapping** | A rule that translates a field from the source system to the destination system (optionally with a transformation). |
| **Retry queue** | Jobs that failed for a transient reason are automatically retried on a backoff schedule. You can also retry manually. |
| **Universal Features** | A collaboration/personalisation toolkit available on every record: **notes** (with @mentions), **tags**, **attachments**, an **activity timeline**, **reminders**, **notifications**, **pins**, **colour codes**, **checklists**, **sticky notes**, **deleted-records management** (restore / permanently delete), and a **modified-log** of field changes. Admin-only items are gated by the `settings.manage` and `records.adminDelete` permissions. |

**Imports run asynchronously.** When you trigger an import you get back a **job ID immediately** (HTTP
`202 Accepted`). The work then happens on a background worker. To find out whether it succeeded, you
**check the job status and logs** (see [section 11](#11-monitoring-jobs-logs-and-retries)).

---

## 2. Who can do what (roles)

Access is **permission-based** (RBAC). Each role carries a set of permission keys (e.g. `users.write`,
`persons.delete`, `roles.assign`); endpoints check the relevant permission. The two seeded
**system roles**, in order of authority:

| Role | Can do |
|------|--------|
| **Super Admin** | Everything, across **all** tenants. Creates/archives tenants, manages people and users, **deletes people**, **assigns/changes roles**, switches between tenants, views all jobs/logs. |
| **Tenant Admin** | Everything **within their own tenant**: manage people and create users, reset passwords, manage permission groups, roles and settings. **Cannot delete a person or change a user's role** (Super-Admin-only). |

> **Super-Admin-only actions:** deleting a **Person** and changing a user's **role assignment**
> (assign/remove a tenant role) are reserved for Super Admins, even if a custom role was granted the
> permission. Beyond the system roles, a Super Admin can define **custom roles** from the permission
> catalogue and make them available per tenant.

Each action below is labelled with the **minimum role required**. If you try something above your
role you'll get `403 Forbidden`.

---

## 3. How you interact with the platform

You call HTTP endpoints under a base URL. In local development that is typically:

```
http://localhost:5080
```

Three convenient entry points (Development mode):

- **Scalar API reference (recommended)** — a browsable, try-it-out UI for every endpoint:
  `http://localhost:5080/scalar/v1`
- **OpenAPI document** — machine-readable spec you can import into Postman/Insomnia:
  `http://localhost:5080/openapi/v1.json`
- **Health check** — confirm the platform is up: `http://localhost:5080/health`

Every call except login/refresh and health requires an **access token** in the header:

```
Authorization: Bearer <your-access-token>
```

> Machine-to-machine callers may instead use a pre-registered API key in the `X-Api-Key` header.
> Most users will use the Bearer token from login.

---

## 4. Getting started: your first login

### Step 1 — Log in

Send your email and password to the login endpoint. (For a brand-new system, your Super Admin uses
the bootstrap credentials configured at install time and **must change the password immediately**.)

```bash
curl -X POST http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"YourPassword"}'
```

Response:

```json
{
  "success": true,
  "message": "Login successful.",
  "data": {
    "accessToken": "eyJhbGci...",
    "expiresIn": 3600,
    "refreshToken": "Base64Token...",
    "mustChangePassword": false
  }
}
```

- **`accessToken`** — use it as `Authorization: Bearer <accessToken>` on every subsequent call. It
  expires after `expiresIn` seconds (default 1 hour).
- **`refreshToken`** — use it to get a new access token without logging in again (valid ~7 days).
- **`mustChangePassword: true`** — you were given a temporary password and **must change it** before
  doing anything else (see [section 5](#5-managing-your-account)).

> **Using Scalar instead?** Open `http://localhost:5080/scalar/v1`, call **Auth → login**, copy the
> `accessToken`, and paste it into Scalar's **Authorization** box. All "try it" calls then carry it
> automatically.

### Step 2 — Keep your session alive (refresh)

When the access token is near expiry, exchange the refresh token for a fresh pair:

```bash
curl -X POST http://localhost:5080/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<your-refresh-token>"}'
```

You receive a **new** access token and a **new** refresh token; the old refresh token is now invalid
(tokens rotate on every refresh).

### Step 3 — Log out

```bash
# Log out this session (optionally pass the refresh token to revoke it)
curl -X POST http://localhost:5080/api/auth/logout \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<your-refresh-token>"}'

# Log out everywhere (revokes all your sessions on all devices)
curl -X POST http://localhost:5080/api/auth/logout-all \
  -H "Authorization: Bearer <token>"
```

---

## 5. Managing your account

### View your profile

Shows your identity and every tenant you belong to (with your role in each).

```bash
curl http://localhost:5080/api/auth/profile -H "Authorization: Bearer <token>"
```

### Change your password

Required after a first login with a temporary password. Changing your password **logs out all your
other sessions**.

```bash
curl -X PUT http://localhost:5080/api/users/me/change-password \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"currentPassword":"OldPass123!","newPassword":"NewStrongPass456!"}'
```

### Update your display name

```bash
curl -X PUT http://localhost:5080/api/users/me \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"displayName":"Jane Doe"}'
```

### Switch active tenant *(if you belong to more than one)*

Your access token is scoped to one **active tenant** at a time. Switch to operate on another:

```bash
curl -X POST http://localhost:5080/api/auth/switch-tenant \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"tenantId":"<tenant-guid>"}'
```

You get back a **new access token** scoped to that tenant. Use it for subsequent calls. (Super Admins
can switch to any tenant; others only to tenants they're assigned to.)

---

## 6. Setting up a tenant (Super Admin)

> **Role required: Super Admin.** All tenant-lifecycle endpoints are Super-Admin only.

### Create a tenant

`Identifier` is a short, unique, **permanent** code; `Name` is the display name.

```bash
curl -X POST http://localhost:5080/api/admin/tenants \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"Acme Corp","identifier":"acme"}'
```

### List / inspect tenants

```bash
# List (paginated; add ?includeArchived=true to see archived ones)
curl "http://localhost:5080/api/admin/tenants?page=1&limit=20" -H "Authorization: Bearer <token>"

# Inspect one — includes whether Concur & Maconomy credentials are configured
curl http://localhost:5080/api/admin/tenants/<tenant-guid> -H "Authorization: Bearer <token>"
```

### Rename, activate/deactivate, archive

```bash
# Rename (the identifier can never change)
curl -X PUT http://localhost:5080/api/admin/tenants/<id> \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"Acme Corporation"}'

# Activate / deactivate (deactivated tenants can't run imports)
curl -X PUT http://localhost:5080/api/admin/tenants/<id>/status \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"isActive":false}'

# Archive (permanent retirement — blocked if the tenant has active jobs)
curl -X PUT http://localhost:5080/api/admin/tenants/<id>/archive \
  -H "Authorization: Bearer <token>"
```

> You cannot archive a tenant while it has running jobs — wait for them to finish, then retry.

---

## 7. Connecting Concur and Maconomy (Tenant Admin)

> **Role required: Tenant Admin or above.** You can only configure **your own** active tenant
> (Super Admins can configure any). **Imports won't work until both systems are configured.**

Credentials are **encrypted at rest** and never returned in plain text — endpoints only tell you
whether a config exists.

### Store Concur credentials

```bash
curl -X PUT http://localhost:5080/api/admin/tenants/<id>/concur-config \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "clientId":"<concur-client-id>",
        "clientSecret":"<concur-client-secret>",
        "baseUrl":"https://us.api.concursolutions.com",
        "companyUuid":"<concur-company-uuid>"
      }'
```

### Store Maconomy credentials

```bash
curl -X PUT http://localhost:5080/api/admin/tenants/<id>/maconomy-config \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "baseUrl":"https://maconomy.example.com",
        "username":"<maconomy-user>",
        "password":"<maconomy-password>"
      }'
```

### Test a connection

Always test after storing credentials — this performs a **live authentication** against the system:

```bash
curl -X POST http://localhost:5080/api/admin/tenants/<id>/concur-config/test   -H "Authorization: Bearer <token>"
curl -X POST http://localhost:5080/api/admin/tenants/<id>/maconomy-config/test -H "Authorization: Bearer <token>"
```

Response tells you `connected: true/false` and a message. If `false`, re-check the credentials/URL.

### Remove credentials

```bash
curl -X DELETE http://localhost:5080/api/admin/tenants/<id>/concur-config   -H "Authorization: Bearer <token>"
curl -X DELETE http://localhost:5080/api/admin/tenants/<id>/maconomy-config -H "Authorization: Bearer <token>"
```

---

## 8. Field mappings

> **Role required: Tenant Admin or above**, own tenant.

Mappings translate a **source field** (e.g. from Concur) to a **destination field** (in Maconomy),
optionally applying a transformation rule. They're tenant-specific.

### List mappings

```bash
curl "http://localhost:5080/api/admin/tenants/<id>/mappings?page=1&limit=20" \
  -H "Authorization: Bearer <token>"
```

### Create (or replace) a mapping

If an active mapping already exists for the same source field, it is **replaced** rather than
duplicated.

```bash
curl -X POST http://localhost:5080/api/admin/tenants/<id>/mappings \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "sourceSystem":"Concur",
        "destinationSystem":"Maconomy",
        "sourceField":"ExpenseType",
        "destinationField":"AccountNumber",
        "transformationRule":null,
        "isActive":true
      }'
```

### Update / delete a mapping

```bash
# Update — only the fields you send are changed
curl -X PUT http://localhost:5080/api/admin/tenants/<id>/mappings/<mappingId> \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"destinationField":"GLAccount","isActive":true}'

# Delete
curl -X DELETE http://localhost:5080/api/admin/tenants/<id>/mappings/<mappingId> \
  -H "Authorization: Bearer <token>"
```

---

## 9. Managing people and users

> **Role required: Tenant Admin or above** to manage people and create/list users.
> **Deleting a person** and **changing a user's role** are **Super-Admin-only**.

Creating a user is a **two-step flow**: first create the **Person** (the CRM master record), then
**promote** that person to a login **User**.

### Step 1 — Create a person

```bash
curl -X POST http://localhost:5080/api/admin/persons \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "firstName":"New","lastName":"User",
        "primaryEmail":"newuser@example.com",
        "mobileNumber":"+12025550123","countryCode":"+1",
        "jobTitle":"Analyst","tenantId":"<tenant-guid>"
      }'
```

- The **tenant** is chosen by Super Admins; for other roles it is auto-set to your active tenant.
- List / search people: `GET /api/admin/persons?page=1&limit=20&search=...`.
- A person who isn't yet a user can be **promoted** (step 2) or **deleted** (Super Admin only).

### Step 2 — Create a user (promote a person)

Supply the `personId`. The system generates a **temporary password** and returns it **once** —
share it securely with the new user, who must change it on first login. The login email defaults to
the person's primary email when omitted.

```bash
curl -X POST http://localhost:5080/api/admin/users \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "personId":"<person-guid>",
        "email":"newuser@example.com",
        "roleId":"<role-guid>",
        "tenantId":"<tenant-guid>"
      }'
# → response includes { "userId": "...", "temporaryPassword": "..." }
```

- A person already linked to a user returns `409` (can't be promoted twice).
- **Tenant Admins** create within their own tenant (`tenantId` forced to your active tenant);
  **Super Admins** can target any tenant. `roleId` (RBAC) is preferred; the legacy `role` enum
  (`SuperAdmin`/`TenantAdmin`) is still accepted.

### List / inspect users

```bash
curl "http://localhost:5080/api/admin/users?page=1&limit=20" -H "Authorization: Bearer <token>"
curl http://localhost:5080/api/admin/users/<userId> -H "Authorization: Bearer <token>"
```

(Tenant Admins only see users in their own tenant. The list shows each user's **tenant**.)

### Enable / disable a user

Disabling a user immediately logs them out everywhere.

```bash
curl -X PUT http://localhost:5080/api/admin/users/<userId>/status \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"isActive":false}'
```

### Edit a user

```bash
curl -X PUT http://localhost:5080/api/admin/users/<userId> \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"displayName":"Updated Name","email":"updated@example.com"}'
```

### Assign / remove tenant roles *(Super Admin only)*

```bash
# Give a user a role in a tenant (re-assigning updates the existing role)
curl -X POST http://localhost:5080/api/admin/users/<userId>/tenant-assignments \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"tenantId":"<tenant-guid>","roleId":"<role-guid>"}'

# Remove a user's role in a tenant
curl -X DELETE http://localhost:5080/api/admin/users/<userId>/tenant-assignments/<tenant-guid> \
  -H "Authorization: Bearer <token>"
```

### Delete a person *(Super Admin only)*

A person linked to a user can't be deleted — remove the user first.

```bash
curl -X DELETE http://localhost:5080/api/admin/persons/<person-guid> \
  -H "Authorization: Bearer <token>"
```

---

## 10. Running imports

> **Role required: Tenant Admin or above.** The import runs against your **active tenant**.

**Before you run:** make sure the tenant has valid Concur **and** Maconomy credentials
([section 7](#7-connecting-concur-and-maconomy-tenant-admin)).

Three imports are available — each returns a **job ID immediately** and runs in the background:

```bash
# Expense reports
curl -X POST http://localhost:5080/api/concur/expenses/import -H "Authorization: Bearer <token>"

# Vendor invoices
curl -X POST http://localhost:5080/api/concur/invoices/import -H "Authorization: Bearer <token>"

# Vendor payments
curl -X POST http://localhost:5080/api/concur/payments/import -H "Authorization: Bearer <token>"
```

Response (`202 Accepted`):

```json
{ "success": true, "message": "Expense import enqueued.", "data": { "jobId": "a1b2c3d4-..." } }
```

**Save the `jobId`** — you'll use it to check progress. The import does **not** block; it has only
been *queued* at this point.

---

## 11. Monitoring jobs, logs, and retries

> **Role required: Tenant Admin or above.** Tenant Admins see only their tenant; Super Admins see
> all tenants, or filter to one with `?tenantId=<guid>`.

### Check job status

```bash
# All recent jobs (paginated)
curl "http://localhost:5080/api/admin/jobs?page=1&limit=20" -H "Authorization: Bearer <token>"

# Filter by status, interface, or date range
curl "http://localhost:5080/api/admin/jobs?status=Failed&interfaceName=ExpenseImport&fromDate=2026-06-01&toDate=2026-06-08" \
  -H "Authorization: Bearer <token>"
```

Each job shows its `jobId`, `interfaceName`, `status`, source/target systems, and created/processed
timestamps. Typical statuses progress from **Created → (running) → Completed** or **Failed**.

### Read logs

Logs are the detail behind a job — what happened and why it failed. Filter by `jobId` to see one
job's story:

```bash
curl "http://localhost:5080/api/admin/logs?jobId=<jobId>" -H "Authorization: Bearer <token>"
```

You can also filter by `status`, `fromDate`, `toDate`, and paginate.

### View the retry queue

Failed-but-retryable jobs land here with a retry count and the next scheduled retry time:

```bash
curl "http://localhost:5080/api/admin/retries?page=1&limit=20" -H "Authorization: Bearer <token>"
```

### Manually retry a failed job

```bash
curl -X POST http://localhost:5080/api/admin/retry/<jobId> -H "Authorization: Bearer <token>"
```

Returns `404` if the job doesn't exist or isn't in a failed state.

### Check platform health

```bash
curl http://localhost:5080/api/admin/health -H "Authorization: Bearer <token>"
```

Reports overall status plus each component (e.g. database). For unauthenticated liveness, use the
public `GET /health`, `/health/live`, and `/health/ready`.

---

## 12. Scheduled (automatic) imports

In addition to triggering imports yourself, the platform runs them **automatically on a schedule**.
A background **Worker** runs recurring jobs (expense, invoice, vendor-payment imports) according to
cron schedules and automatically retries transient failures. Scheduled runs appear in the **same
jobs/logs views** as manual runs ([section 11](#11-monitoring-jobs-logs-and-retries)), so that's
where you confirm they ran.

### Managing schedules (Schedules tab)

> **Role required: Tenant Admin or above** (`jobs.schedule`). Schedules are **per tenant**.

Open **Integration Jobs → Schedules**. You'll see one row per import flow (Expense, Invoice, Vendor
payment) with its cron expression and an **Active / Paused** badge. Click **Edit** to set the cadence:

- **Cron expression** — standard 5-field cron, **evaluated in UTC**. The drawer lists common presets
  (Daily 02:00, Weekdays 06:00, Every 15 minutes, Hourly, Monthly, …) you can click to fill in.
- **Active** — toggle a schedule on/off.

Changes take effect within **~1 minute** (no restart). Examples:

| Cadence | Cron |
|---|---|
| Daily at 02:00 UTC | `0 2 * * *` |
| Weekdays at 06:00 UTC | `0 6 * * 1-5` |
| Every 15 minutes | `*/15 * * * *` |
| Hourly | `0 * * * *` |
| 1st of month, 00:00 UTC | `0 0 1 * *` |

**Per-tenant behaviour**
- A **Tenant Admin** manages only **their own tenant's** schedules; the imports run for that tenant only.
- A **Super Admin** picks the tenant from a **Tenant dropdown** at the top of the Schedules tab, and
  can manage any tenant's schedules.
- A tenant's own active schedule **takes precedence** over any platform-wide default for that flow.

Via the API:

```bash
# List the active tenant's import schedules (Super Admin may add ?tenantId=<guid>)
curl "http://localhost:5080/api/admin/job-schedules" -H "Authorization: Bearer <token>"

# Set the expense import to run daily at 02:00 UTC
curl -X PUT "http://localhost:5080/api/admin/job-schedules/ExpenseImportJob" \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"cronExpression":"0 2 * * *","isActive":true}'
```

> Times are **UTC**. For a local time, convert first — e.g. 09:00 IST → `30 3 * * *`.
> The Worker must be running for schedules to fire.

**Automatic retries:** a job that fails for a transient reason is retried up to **4 times** on an
increasing backoff (≈5, 15, 30, 60 minutes). After that it's marked failed for good. Jobs that fail
**validation** are not retried — fix the underlying data/config and run again.

---

## 13. Understanding responses and errors

Every response uses one consistent envelope.

**Success:**

```json
{
  "success": true,
  "message": "Jobs retrieved.",
  "data": { /* ... */ },
  "meta": { "page": 1, "limit": 20, "totalRecords": 137 }   // only on list responses
}
```

**Error:**

```json
{
  "success": false,
  "message": "Validation failed.",
  "error": { "code": "VALIDATION_FAILED", "details": "tenantId is required for tenant-scoped roles." }
}
```

Common HTTP status codes:

| Code | Meaning | What to do |
|------|---------|------------|
| `200 OK` | Success | — |
| `201 Created` | Resource created | — |
| `202 Accepted` | Import queued (async) | Note the `jobId`, then poll job status |
| `400 Bad Request` | Validation error | Read `error.details` and fix the request body |
| `401 Unauthorized` | Missing/expired/invalid token | Log in again or refresh your token |
| `403 Forbidden` | Your role isn't high enough, or wrong tenant | Use the right role / switch tenant |
| `404 Not Found` | Resource doesn't exist (or you can't see it) | Check the ID |
| `409 Conflict` | Duplicate identifier, or tenant archived/has active jobs | Resolve the conflict |

> **Pagination:** list endpoints accept `page` (default 1) and `limit` (default 20, max 100), and
> return totals in `meta`.

---

## 14. Common workflows end-to-end

### A. Onboard a new client (Super Admin → Tenant Admin)

1. **Super Admin** creates the tenant ([6](#6-setting-up-a-tenant-super-admin)).
2. **Super Admin** creates a Tenant Admin user for it and shares the temporary password ([9](#9-managing-users)).
3. **Tenant Admin** logs in, changes the temporary password ([4](#4-getting-started-your-first-login), [5](#5-managing-your-account)).
4. **Tenant Admin** stores and **tests** Concur and Maconomy credentials ([7](#7-connecting-concur-and-maconomy-tenant-admin)).
5. **Tenant Admin** adds any field mappings ([8](#8-field-mappings)).
6. **Tenant Admin** creates users as needed ([9](#9-managing-users)).
7. A **Tenant Admin** triggers a test import and confirms it via jobs/logs ([10](#10-running-imports), [11](#11-monitoring-jobs-logs-and-retries)).

### B. Run and verify an expense import (Tenant Admin)

1. `POST /api/concur/expenses/import` → save the `jobId`.
2. `GET /api/admin/jobs?interfaceName=ExpenseImport` → find the job, check `status`.
   *(Viewing jobs requires Tenant Admin or above.)*
3. If `Failed`: `GET /api/admin/logs?jobId=<jobId>` to see why.
4. If it was a transient failure: `POST /api/admin/retry/<jobId>`, or let the automatic retry handle it.

### C. Rotate a tenant's Maconomy password (Tenant Admin)

1. `PUT /api/admin/tenants/<id>/maconomy-config` with the new password.
2. `POST /api/admin/tenants/<id>/maconomy-config/test` to confirm it connects.

---

## 15. Troubleshooting

| Symptom | Likely cause & fix |
|---------|--------------------|
| `401 Unauthorized` on every call | Access token missing or expired. Add `Authorization: Bearer <token>`, or refresh/re-login. |
| `403 Forbidden` | Your role is too low for that action, **or** you're acting on a tenant that isn't your active one. Switch tenant or ask a higher-role user. |
| Import returns `202` but nothing seems to happen | That's expected — it ran in the background. Check `GET /api/admin/jobs` and the logs. Also confirm the **Worker** is running. |
| Import job goes straight to `Failed` | Usually missing/invalid credentials. Re-test Concur & Maconomy configs. Read `GET /api/admin/logs?jobId=...` for the specific error. |
| Credential **test** says `connected: false` | Wrong client ID/secret, username/password, base URL, or company UUID. Re-enter and test again. |
| `409 Conflict` creating a tenant/user | The identifier or email is already in use. Choose a different one. |
| `409 Conflict` archiving a tenant | The tenant still has active jobs. Wait for them to finish, then archive. |
| `mustChangePassword: true` blocks me | Change your password first via `PUT /api/users/me/change-password`. |

---

### Quick reference — endpoints by task

| I want to… | Call | Min role |
|------------|------|----------|
| Log in | `POST /api/auth/login` | anyone |
| Refresh my token | `POST /api/auth/refresh` | anyone (with refresh token) |
| Log out / everywhere | `POST /api/auth/logout` · `/logout-all` | logged in |
| See my profile | `GET /api/auth/profile` | logged in |
| Change my password | `PUT /api/users/me/change-password` | logged in |
| Switch active tenant | `POST /api/auth/switch-tenant` | logged in |
| Create / list / archive tenants | `POST·GET /api/admin/tenants`, `.../status`, `.../archive` | Super Admin |
| Set/test/clear Concur or Maconomy config | `PUT·POST·DELETE /api/admin/tenants/{id}/{concur\|maconomy}-config[/test]` | Tenant Admin |
| Manage mappings | `GET·POST·PUT·DELETE /api/admin/tenants/{id}/mappings` | Tenant Admin |
| Create / list / search people | `POST·GET /api/admin/persons[...]` | Tenant Admin |
| Delete a person | `DELETE /api/admin/persons/{id}` | **Super Admin** |
| Create (promote a person) / list / disable users | `POST·GET·PUT /api/admin/users[...]` | Tenant Admin |
| Assign / change tenant roles | `POST·DELETE /api/admin/users/{id}/tenant-assignments` | **Super Admin** |
| Trigger an import | `POST /api/concur/{expenses\|invoices\|payments}/import` | Tenant Admin |
| View jobs / logs / retries | `GET /api/admin/{jobs\|logs\|retries}` | Tenant Admin |
| Manually retry a job | `POST /api/admin/retry/{jobId}` | Tenant Admin |
| Manage import schedules | `GET·PUT /api/admin/job-schedules[...]` | Tenant Admin |
| Check health | `GET /api/admin/health` · `GET /health` | Tenant Admin · anyone |

---

*For installation, configuration, and running the services, see [`RUN.md`](RUN.md). For the
interactive API explorer, see [`SCALAR.md`](SCALAR.md). For extending the platform, see
[`DEVELOPMENT.md`](DEVELOPMENT.md).*
