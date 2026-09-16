# EMS Portal

A multi-tenant integration platform that automates the import of financial data from **Concur** into **Maconomy** (expense reports, vendor invoices, vendor payments). It exposes a REST API for triggering and administering integration flows, runs the flows asynchronously on a background worker via Hangfire, and isolates every tenant's data and credentials.

Beyond the core integration flows it also provides RBAC permission groups, per-tenant SMTP accounts & email templates, role-aware dashboards, and a platform-wide **Universal Features** collaboration layer — notes (with @mentions), tags, attachments, an activity timeline, reminders, notifications, pins, colour codes, saved views, checklists, sticky notes, deleted-records management, and a field-level modified-log — that attaches to **any** entity via a shared `(EntityType, EntityId)` key without per-entity schema changes.

Built on **.NET 9** following **Clean Architecture**, with a **Quasar 2 / Vue 3** admin SPA (`WEB/`).

---

## Table of contents

- [Architecture](#architecture)
- [Technology stack](#technology-stack)
- [Solution structure](#solution-structure)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Running the platform](#running-the-platform)
- [API surface](#api-surface)
- [Authentication & roles](#authentication--roles)
- [Database & migrations](#database--migrations)
- [Background jobs](#background-jobs)
- [Testing](#testing)
- [Further reading](#further-reading)

---

## Architecture

The platform is three deployable containers sharing one SQL Server database:

| Container | Project | Responsibility |
|-----------|---------|----------------|
| **Integration API** | `EmsPortal.Api` | HTTP entry point. Authenticates requests, triggers flows (enqueues Hangfire jobs), serves admin/tenant/user/auth endpoints, owns EF Core migrations. Never calls external systems directly. |
| **Background Worker** | `EmsPortal.Workers` | Runs the Hangfire server. Executes recurring and enqueued integration jobs; the only container that calls Concur and Maconomy. |
| **MCP Server** | `EmsPortal.McpServer` | Host placeholder for the Model Context Protocol surface (AI-agent access). |

Layering follows Clean Architecture — dependencies point inward:

```
Domain  ←  Application  ←  Infrastructure  ←  Api / Workers / McpServer
                                   ▲
                Shared  ───────────┘   (referenced by every project)
```

- **Domain** — entities and enums. No dependencies except `Shared`.
- **Application** — use cases, abstractions (interfaces), MediatR command/handlers, transformers, validators. Depends only on `Domain` + `Shared`.
- **Infrastructure** — EF Core, repositories, connectors, Hangfire, Serilog, security, Data Protection. Implements the Application abstractions.
- **Shared** — cross-cutting contracts: configuration option types, API response envelopes, security constants, connector result types.
- **Api / Workers / McpServer** — composition roots that wire everything via `AddApplication()` + `AddInfrastructure()`.

---

## Technology stack

| Concern | Technology |
|---------|------------|
| Runtime | .NET 9 |
| Web API | ASP.NET Core 9 (controllers) |
| Persistence | Entity Framework Core 9, SQL Server |
| Background jobs | Hangfire (SQL Server storage) |
| Mediation | MediatR |
| Validation | FluentValidation (auto-validation) |
| Logging | Serilog → SQL Server sink + console |
| Auth | Platform-issued JWT (RS256) + API Key (PBKDF2); RBAC policies |
| Secrets at rest | .NET Data Protection (SQL-persisted key ring) |
| API docs | Native OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar UI |
| Tests | xUnit, Moq, FluentAssertions; `WebApplicationFactory` for integration |

---

## Solution structure

```
EmsPortal.sln
├── src/
│   ├── EmsPortal.Domain          # entities, enums
│   ├── EmsPortal.Shared          # contracts, config options, security constants
│   ├── EmsPortal.Application      # abstractions, MediatR flows, transformers, validators
│   ├── EmsPortal.Infrastructure   # EF Core, repos, connectors, Hangfire, security, logging
│   ├── EmsPortal.Api              # ASP.NET Core host (controllers, middleware, auth, OpenAPI)
│   ├── EmsPortal.Workers          # Hangfire worker + DB-driven scheduler
│   └── EmsPortal.McpServer        # console host (MCP placeholder)
└── tests/
    ├── EmsPortal.UnitTests        # xUnit/Moq unit tests
    └── EmsPortal.IntegrationTests # WebApplicationFactory + real SQL
```

---

## Getting started

### Prerequisites

- **.NET 9 SDK** (the repo pins the SDK via `global.json`)
- **SQL Server** (2019+ or SQL Express; Azure SQL works as a drop-in)

### Clone & build

```bash
git clone https://github.com/VskySolutions/FS_THF_Integrations.git
cd FS_THF_Integrations
dotnet build EmsPortal.sln -c Release
```

---

## Configuration

Configuration lives in each host's `appsettings.json` and is overridable via environment variables / secrets. Key sections (`EmsPortal.Api`):

| Section | Purpose |
|---------|---------|
| `ConnectionStrings:SqlServer` | Shared SQL Server connection string |
| `Authentication` | `Issuer`, `Audience`, `PrivateKeyPem`/`PublicKeyPem` (RS256), `AccessTokenMinutes` (60), `RefreshTokenDays` (7), `ApiKeyHeaderName` |
| `Authentication:Microsoft` | "Login with Microsoft": `TenantId`, `ClientId`, `ClientSecret` from the Entra ID app registration, in the API's `.env` (below) or the git-ignored `appsettings.json`. Optional, normally omitted: `RedirectUri` (proxy setups only), `Prompt` |
| `ApiKeys` | Registered machine-to-machine keys (PBKDF2 hashes) |
| `Hangfire` | `WorkerCount`, `ServerName`, `SchemaName`, `DashboardEnabled` |
| `Retry` | `MaxAttempts` (4), `BackoffMinutes` (5/15/30/60), `RetryFailedJobsCron` |
| `ExternalSystems` | Paycor/Concur/Maconomy base URLs (per-tenant credentials override these at runtime) |
| `Serilog` | `MinimumLevel` |
| `Bootstrap` | First-run Super Admin (`Email`, `Password`, `TenantIdentifier`, `TenantName`) |
| `ErrorHandling` | `IncludeExceptionDetails` (defaults to Development) |

> **Security:** never commit real secrets. Use environment variables, user-secrets, or a secrets manager for the connection string, signing keys, and the bootstrap password before any non-local deployment.

**`.env` file.** The API loads `API/src/EmsPortal.Api/.env` (or a `.env` beside the published `EmsPortal.Api.dll`) into the process environment at startup, so any setting can be written there with `__` in place of `:` — e.g. `Authentication__Microsoft__ClientSecret=…`. A real environment variable with the same name wins. Copy `.env.example` to get started; `.env` is git-ignored. Putting the same keys in the git-ignored `appsettings.json` works just as well. See [`API/docs/RUN.md`](API/docs/RUN.md#microsoft-365-sign-in) for the Microsoft 365 setup.

---

## Running the platform

The **API** owns schema migrations and seeds a bootstrap Super Admin on first run, so start it first.

```bash
# Terminal 1 — API (applies migrations + seeds, serves HTTP)
dotnet run --project src/EmsPortal.Api --urls http://localhost:5080

# Terminal 2 — Worker (Hangfire server: executes jobs)
dotnet run --project src/EmsPortal.Workers
```

In **Development** the API serves:

- Scalar API reference → `http://localhost:5080/scalar/v1`
- OpenAPI document → `http://localhost:5080/openapi/v1.json`
- Hangfire dashboard → `http://localhost:5080/hangfire` (admin role)
- Health → `http://localhost:5080/health`, `/health/live`, `/health/ready`

### First login

The bootstrap seeder creates a Super Admin (defaults — change them):

```
email:    admin@integrationhub.local
password: ChangeMe123!
```

```bash
curl -X POST http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@integrationhub.local","password":"ChangeMe123!"}'
# → { "data": { "accessToken": "...", "refreshToken": "...", ... } }
```

Use the `accessToken` as `Authorization: Bearer <token>` on subsequent calls.

---

## API surface

All responses use the standard envelope (`ApiResponse<T>` / `ApiErrorResponse`).

| Area | Endpoints |
|------|-----------|
| **Auth** | `POST /api/auth/login` · `refresh` · `logout` · `logout-all` · `switch-tenant` · `GET /api/auth/profile` · `PUT /api/users/me/change-password` · Microsoft sign-in: `GET /api/auth/microsoft/login` · `GET …/microsoft/callback` · `POST …/microsoft/exchange` |
| **People** | `POST/GET /api/admin/persons` · `GET/PUT/DELETE /api/admin/persons/{id}` · `GET /api/admin/persons/selectable` (the CRM master record; a person is promoted to a user) |
| **Users** | `POST/GET /api/admin/users` (create **promotes an existing Person** via `personId`) · `GET/PUT/PUT…/status` · `reset-password` · tenant-assignment endpoints · `PUT /api/users/me` · `GET/PUT /api/users/me/profile` · `…/admin/users/{id}/profile` |
| **Roles (RBAC)** | `POST/GET/PUT/DELETE /api/admin/roles` · `GET /api/admin/permissions` · tenant↔role availability endpoints |
| **Tenants** | `POST/GET /api/admin/tenants` · lifecycle (`status`, `archive`) · `concur-config`/`maconomy-config` (store/clear/test) · mapping CRUD |
| **Concur** | `POST /api/concur/expenses/import` · `invoices/import` · `payments/import` (202 + `jobId`) |
| **Admin** | `GET /api/admin/jobs` · `logs` · `retries` · `health` · `POST /api/admin/retry/{jobId}` |
| **Schedules** | `GET/PUT /api/admin/job-schedules` — per-tenant import cron schedules (`jobs.schedule`) |
| **Health** | `GET /health` · `/health/live` · `/health/ready` (anonymous) |

---

## Authentication & roles

- **Two schemes:** platform JWT (RS256, primary) and API Key (`X-Api-Key`, machine-to-machine). A composite `AnyOf` scheme tries JWT first.
- **Login with Microsoft (Microsoft 365 / Entra ID):** the API runs the authorization-code flow server-side (`/api/auth/microsoft/login` → Microsoft → `/api/auth/microsoft/callback`), verifies the ID token against the tenant's published keys, matches the account's email to an **existing, active user**, and hands the SPA a one-time code it exchanges for the normal JWT + refresh-token pair. Nobody is auto-provisioned. Keys live in the API's `.env`.
- **Permission-based RBAC:** endpoints are gated by `[RequirePermission("area.action")]` against the permission keys carried on the caller's role. Seeded **system roles** — `SuperAdmin` (all permissions) > `TenantAdmin` — can be supplemented by custom roles. Permission catalogue (`EmsPortal.Shared.Security.Permissions`): `tenants.*`, `persons.*`, `users.*`, `roles.*`, `mappings.*`, `jobs.*`, `logs.read`, `health.read`. System-role permission sets are re-seeded on every startup, so catalogue changes apply without a data migration.
- **Super-Admin-only actions:** deleting a `Person` and changing a user's role assignment (assign/remove tenant roles) are restricted to **Super Admins** regardless of any granted permission — Tenant Admins no longer hold `persons.delete` or `roles.assign`.
- **Tenant isolation:** the JWT carries `activeTenantId`; `TenantResolutionMiddleware` resolves and validates it, and all tenant-scoped queries filter by it automatically (EF global query filters). Background jobs carry the tenant id in their Hangfire payload.
- **Session invalidation:** a `tokenVersion` on the user is incremented on password change, deactivation, email change, and logout; the JWT handler rejects stale tokens.

---

## Database & migrations

- **Code-first EF Core**, applied automatically by the API on startup (`Database.Migrate()`).
- Generate a new migration:

```bash
dotnet ef migrations add <Name> \
  --project src/EmsPortal.Infrastructure \
  --startup-project src/EmsPortal.Api \
  --output-dir Persistence/Migrations
```

- Core tables: `IntegrationJobs`, `IntegrationLogs`, `RetryQueue`, `MappingConfigurations`, `AuditTrail` (all tenant-scoped), plus `Tenants`, `TenantApiConfigurations`, `JobScheduleConfigurations`, `Users`, `UserTenantRoles`, `RefreshTokens`, and `DataProtectionKeys`. Identity/CRM is normalized (WO-61) across `Persons`, `Addresses`, and `Media` — a `User` links to a `Person` master record; `Persons` carries an optional `TenantId`. RBAC adds `Roles` and `TenantRoles` (role↔tenant availability). Hangfire and Serilog manage their own schemas.

---

## Background jobs

- The **Worker** hosts the Hangfire server. `HangfireJobScheduler` loads cron schedules from `JobScheduleConfigurations` on startup and polls every minute for runtime changes.
- Recurring jobs: `ExpenseImportJob`, `InvoiceImportJob`, `VendorPaymentImportJob`, `RetryFailedJobsJob`. Recurring jobs fan out across all active tenants.
- **Retry strategy:** transient failures retry on a 5/15/30/60-minute incremental backoff; after 4 attempts a job is dead-lettered. Validation failures fail permanently without retry.

---

## Testing

```bash
# All tests
dotnet test EmsPortal.sln

# Unit only
dotnet test tests/EmsPortal.UnitTests
```

- **Unit tests** mock all external dependencies.
- **Integration tests** boot the real app via `WebApplicationFactory` against a dedicated SQL test database (`EMS_Portal_Test`), created by EF migrations on first run. Ensure SQL Server is reachable.

---

## Further reading

- [`docs/RUN.md`](docs/RUN.md) — build, configure, run each host, verify end-to-end, and troubleshoot.
- [`docs/SCALAR.md`](docs/SCALAR.md) — run and use the Scalar API reference and the OpenAPI document.
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) — coding conventions, architecture rules, and how to extend the platform (add a connector, flow, endpoint, or migration).
</content>
