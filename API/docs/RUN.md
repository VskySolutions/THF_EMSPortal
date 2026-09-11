# Running EMS Portal

How to build, configure, and run the platform locally, and how to verify it end-to-end.

See also: [README](../README.md) · [DEVELOPMENT](DEVELOPMENT.md) · [SCALAR](SCALAR.md)

---

## 1. Prerequisites

- **.NET 9 SDK** — the repo pins the SDK via `global.json`. Verify: `dotnet --version` → `9.0.x`.
- **SQL Server** — 2019+ or SQL Express (Azure SQL works as a drop-in). The platform creates its schema via EF Core migrations.
- (Optional) **sqlcmd** for the integration-test database.

---

## 2. Build

```bash
git clone https://github.com/VskySolutions/FS_THF_Integrations.git
cd FS_THF_Integrations
dotnet build EmsPortal.sln -c Release
```

---

## 3. Database & configuration

The platform uses one shared SQL Server database. Set the connection string for **both** the API and the Worker (`appsettings.json` → `ConnectionStrings:SqlServer`), or override via environment variables / user-secrets.

```jsonc
"ConnectionStrings": {
  "SqlServer": "Data Source=.\\SQLEXPRESS;Initial Catalog=EMS_Portal;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True;"
}
```

You do **not** create the schema manually:

- The **API applies all EF Core migrations on startup** (`Database.Migrate()`). Hangfire and Serilog create their own tables on first use.
- On first run, a **bootstrap Super Admin** and a default `system` tenant are seeded from the `Bootstrap` section.

```jsonc
"Bootstrap": {
  "Email": "admin@integrationhub.local",
  "Password": "ChangeMe123!",
  "TenantIdentifier": "system",
  "TenantName": "System"
}
```

> **Change the bootstrap password** (and don't commit real secrets) before any shared/deployed environment. Prefer `dotnet user-secrets`, environment variables, or a secrets manager for the connection string, the RSA signing key, and the bootstrap password.

Other notable settings (`EmsPortal.Api`): `Authentication` (JWT/RS256 + token lifetimes), `ApiKeys`, `Hangfire`, `Retry`, `ExternalSystems`, `Serilog`, `ErrorHandling`. See the [README configuration table](../README.md#configuration).

### Secrets in `.env`

The API loads a `.env` file at startup — `src/EmsPortal.Api/.env` when run from Visual Studio / `dotnet run`, or a `.env` next to `EmsPortal.Api.dll` on a server — and turns each `KEY=value` line into an environment variable before configuration is built. Any setting can therefore go there with `__` in place of `:`. A real environment variable with the same name wins over the file, and `.env` is git-ignored.

```bash
cp src/EmsPortal.Api/.env.example src/EmsPortal.Api/.env   # then fill in the values
```

### Microsoft 365 sign-in

"Login with Microsoft" on the sign-in page needs an Entra ID app registration; the API does the whole exchange with Microsoft, so the client secret never reaches the browser.

1. **Entra admin center → App registrations → New registration.** Any name (e.g. *EMS Portal*); supported account types: **this organizational directory only**.
2. **Authentication → Add a platform → Web.** Redirect URI: `https://<api-host>/api/auth/microsoft/callback` (add `http://localhost:5032/api/auth/microsoft/callback` for local development; plain `http` is right, the local API has no https port). It must be under **Web**, not **Single-page application**: an SPA-registered URI makes Microsoft refuse the server-side code exchange (`AADSTS9002325`). No implicit grant needed.
3. **Certificates & secrets → New client secret.** Copy the secret **Value** (it is shown once).
4. **Overview.** Copy the **Directory (tenant) ID** and **Application (client) ID**.
5. Put the three values in `.env`, or under `Authentication:Microsoft` in the git-ignored `appsettings.json`. Nothing else is required.

   ```dotenv
   Authentication__Microsoft__TenantId=<directory (tenant) id>
   Authentication__Microsoft__ClientId=<application (client) id>
   Authentication__Microsoft__ClientSecret=<secret value>
   ```

   ```jsonc
   "Authentication": {
     "Microsoft": {
       "TenantId": "<directory (tenant) id>",
       "ClientId": "<application (client) id>",
       "ClientSecret": "<secret value>"
     }
   }
   ```

6. Make sure `App:BaseUrl` points at the web app (e.g. `http://localhost:9000`): that is where the API sends the browser back.

A Microsoft account can only sign in when its email address (the `email`, `preferred_username` or `upn` claim) matches an **existing, active** EMS Portal user; nobody is created on the fly. Multi-tenant aliases (`common`, `organizations`, `consumers`) are refused for `TenantId`. With the three keys empty the button still shows, and clicking it reports that Microsoft sign-in is not set up.

Two optional keys exist and are normally left out: `RedirectUri`, only for an API behind a proxy that hides its public host (set it to the exact callback URI registered in step 2, never to a Microsoft address), and `Prompt` (`select_account` to always show Microsoft's account picker, `login` to force re-authentication).

---

## 4. Run the hosts

Start the **API first** (it owns migrations + seeding).

### API

```bash
# Development profile (recommended) — sets ASPNETCORE_ENVIRONMENT=Development, opens Scalar
dotnet run --project src/EmsPortal.Api
# → http://localhost:5032  (Scalar opens at /scalar/v1)
```

To run on a custom URL or force the environment explicitly (e.g. when not using the launch profile):

```bash
# bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/EmsPortal.Api --urls http://localhost:5080
# PowerShell
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project src/EmsPortal.Api --urls http://localhost:5080
```

### Worker (executes jobs)

The API only **enqueues** jobs; the Worker runs them. Start it to actually process imports and recurring schedules.

```bash
dotnet run --project src/EmsPortal.Workers
```

It boots the Hangfire server and the DB-driven scheduler (requires the same `ConnectionStrings:SqlServer`).

### MCP Server (optional)

```bash
dotnet run --project src/EmsPortal.McpServer
```

---

## 5. Environment differences

| Behavior | Development / Staging | Production |
|----------|-----------------------|------------|
| Scalar UI (`/scalar/v1`) + OpenAPI (`/openapi/v1.json`) | Exposed | Hidden (404) |
| Exception detail in 500 responses | On (unless `ErrorHandling:IncludeExceptionDetails=false`) | Off |
| Default log level | Debug | Information |

`ASPNETCORE_ENVIRONMENT` controls this (`Development`, `Staging`, or `Production`).

---

## 6. Verify it's running

```bash
# Health (anonymous)
curl http://localhost:5032/health/ready
# → 200, per-component status (sqlserver, concur, maconomy)

# Login as the bootstrap admin
curl -X POST http://localhost:5032/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@integrationhub.local","password":"ChangeMe123!"}'
# → { "data": { "accessToken": "...", "refreshToken": "..." } }
```

---

## 7. Trigger a flow end-to-end

```bash
TOKEN=...   # accessToken from login

# Enqueue an expense import (returns 202 + jobId)
curl -X POST http://localhost:5032/api/concur/expenses/import \
  -H "Authorization: Bearer $TOKEN"

# Watch the job (the Worker must be running to process it)
curl http://localhost:5032/api/admin/jobs -H "Authorization: Bearer $TOKEN"
```

- **Hangfire dashboard:** `http://localhost:5032/hangfire` (requires Tenant Admin or above).
- To actually reach Concur/Maconomy, store per-tenant credentials via `PUT /api/admin/tenants/{id}/concur-config` (and `…/maconomy-config`) and test them with `…/concur-config/test`.

---

## 8. Run the tests

```bash
dotnet test EmsPortal.sln              # all tests
dotnet test tests/EmsPortal.UnitTests  # unit only (no DB needed)
```

Integration tests boot the real app against a dedicated test database `EMS_Portal_Test` (created by migrations on first run). If your SQL login can't auto-create databases, pre-create it once:

```sql
IF DB_ID('EMS_Portal_Test') IS NULL CREATE DATABASE [EMS_Portal_Test];
```

---

## 9. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Cannot open database … login failed` | Connection string wrong, SQL not running, or the login lacks rights. For the test DB, pre-create it (section 8). |
| `/scalar/v1` returns 404 | App is running in **Production**. Use the `http` launch profile or set `ASPNETCORE_ENVIRONMENT=Development`. |
| Login returns 401 for the bootstrap admin | Seeder didn't run (a user already exists) or the `Bootstrap` password differs. Check the `Users` table / config. |
| Triggered import stays `Created` | The **Worker isn't running** — start `EmsPortal.Workers`. |
| Port already in use | Pass a free port with `--urls http://localhost:<port>`. |
| `dotnet --version` is not 9.x | Install the .NET 9 SDK; `global.json` pins it. |
</content>
