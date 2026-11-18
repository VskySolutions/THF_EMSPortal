# Scalar API Reference

COP Portal documents its HTTP API with the native ASP.NET Core OpenAPI generator and serves an interactive reference UI with **Scalar**.

See also: [README](../README.md) · [RUN](RUN.md)

---

## 1. What's served, and where

| URL | What | Use |
|-----|------|-----|
| `/scalar/v1` | **Scalar UI** — interactive, try-it-out API reference | Explore & call endpoints from the browser |
| `/openapi/v1.json` | **OpenAPI 3 document** | Import into Postman, Insomnia, code generators, etc. |

> **Exposure is environment-gated.** Both are available **only in Development and Staging**. In Production they return `404` (the API spec and explorer are not published publicly).

---

## 2. Open it

Run the API in Development (the `http` launch profile does this and opens Scalar automatically):

```bash
dotnet run --project src/EmsPortal.Api
# → http://localhost:5032/scalar/v1
```

If you start without the launch profile, force the environment:

```bash
# bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/EmsPortal.Api --urls http://localhost:5080
# PowerShell
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project src/EmsPortal.Api --urls http://localhost:5080
```

Then browse to `http://localhost:<port>/scalar/v1`.

---

## 3. Layout

Endpoints are grouped by **tag**, one per controller:

- **Auth** — login, refresh, logout, logout-all, switch-tenant, profile, change-password
- **Users** — user CRUD, status, tenant-role assignments, self-profile
- **Tenants** — tenant lifecycle, credential store/clear/test, mapping CRUD
- **Concur** — expense / invoice / payment import triggers
- **Admin** — jobs, logs, retries, health, manual retry

Each operation shows its request schema, the `ApiResponse<T>` success shape, and the documented error responses (`400` `ApiErrorResponse`, `401`, `403`, `404`, `500`).

---

## 4. Authenticate, then call endpoints

The API uses two security schemes (visible in Scalar's auth panel):

- **`Jwt`** — platform-issued JWT bearer token (primary, for users)
- **`ApiKey`** — `X-Api-Key` header (machine-to-machine)

Most endpoints require auth; `POST /api/auth/login` and the health checks are anonymous.

### Step by step (JWT)

1. In Scalar, open **Auth → `POST /api/auth/login`**, send:
   ```json
   { "email": "admin@integrationhub.local", "password": "ChangeMe123!" }
   ```
   (the bootstrap Super Admin — see [RUN](RUN.md)).
2. Copy `data.accessToken` from the response.
3. Open the **Authentication** panel (top of Scalar), choose the **`Jwt`** scheme, and paste the token. Scalar now adds `Authorization: Bearer <token>` to every request.
4. Call a protected endpoint, e.g. **Concur → `POST /api/concur/expenses/import`** → `202` + `jobId`, or **Admin → `GET /api/admin/jobs`**.

### Using an API key instead

Select the **`ApiKey`** scheme and provide the key value; Scalar sends it as `X-Api-Key`. Keys are configured under `ApiKeys` in `appsettings.json` (stored as PBKDF2 hashes).

> Access tokens expire (default 60 min). Use `POST /api/auth/refresh` with your `refreshToken` to get a new one without re-entering credentials.

---

## 5. Use the spec elsewhere

Grab the raw document and import it into your tool of choice:

```bash
curl http://localhost:5032/openapi/v1.json -o integrationhub-openapi.json
```

- **Postman / Insomnia:** Import → File → select the JSON.
- **Client generation:** feed `openapi/v1.json` to `openapi-generator`, NSwag, Kiota, etc.

---

## 6. Troubleshooting

| Symptom | Fix |
|---------|-----|
| `/scalar/v1` or `/openapi/v1.json` returns **404** | App is in **Production**. Run with the `http` profile or set `ASPNETCORE_ENVIRONMENT=Development`. |
| `401` on every call | No token set, or it expired — log in again / refresh and re-apply the bearer token in the auth panel. |
| `403` on an admin/tenant call | Your role is insufficient (e.g. a Tenant Admin calling a `SuperAdminOnly` endpoint), or you're acting on a different tenant. |
| Endpoint missing from the UI | Rebuild/restart the API — the OpenAPI document is generated from the running controllers. |

---

## 7. How it's wired (for maintainers)

- `builder.Services.AddEmsPortalOpenApi()` registers the OpenAPI document and a transformer that sets the title and the `Jwt` + `ApiKey` security schemes (`Api/OpenApi/OpenApiConfiguration.cs`).
- `app.MapOpenApi()` and `app.MapScalarApiReference(...)` are mapped only when `IsDevelopment() || IsStaging()` (`Program.cs`).
- Controllers carry `[Tags("…")]` and `[ProducesResponseType<…>]` so the document reflects the real grouping and envelope shapes. XML doc comments are compiled in (`GenerateDocumentationFile`).
</content>
