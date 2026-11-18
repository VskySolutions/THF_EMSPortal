# COP Portal — Development Guidelines

This document defines how the COP Portal codebase is organized and how to extend it consistently. Read it before adding features. It complements the [README](../README.md).

---

## 1. Architecture rules

The solution is **Clean Architecture**. The single most important rule:

> **Dependencies point inward.** Inner layers never reference outer layers.

```
Domain  ←  Application  ←  Infrastructure  ←  Api / Workers / McpServer
   ▲                                              
 Shared  ── referenced by everything, references nothing else
```

| Layer | May reference | Must NOT reference | Contains |
|-------|---------------|--------------------|----------|
| **Domain** | Shared | Application, Infrastructure, ASP.NET, EF Core | Entities, enums. POCOs only — no attributes, no EF, no framework types. |
| **Shared** | (nothing) | everything | Config option classes, `ApiResponse` envelopes, security constants, connector result types. |
| **Application** | Domain, Shared | Infrastructure, ASP.NET, EF Core | **Interfaces** (`I…Repository`, `I…Service`, `IConnector`, …), MediatR commands/handlers, transformers, validators, flow services. |
| **Infrastructure** | Application, Domain, Shared | ASP.NET MVC (controllers) | EF Core `DbContext` + repositories, connectors, Hangfire jobs, Serilog, security, Data Protection. **Implements** Application interfaces. |
| **Api** | Infrastructure, Application, Domain, Shared | — | Controllers, middleware, auth, OpenAPI, composition root. |
| **Workers / McpServer** | Infrastructure, Application, Domain, Shared | — | Host + composition root. |

**Golden rules**
- Define an **interface in Application**, implement it in **Infrastructure**. Controllers and services depend on the interface, never the concrete class.
- EF Core, `HttpContext`, Hangfire, and Serilog types stay in Infrastructure/Api — never in Domain or Application abstractions.
- The Hangfire **job classes live in Infrastructure** (not Workers) so the API can enqueue them and the Worker can execute them.

---

## 2. Dependency injection

Each layer exposes one registration entry point:

- `IServiceCollection.AddApplication()` — MediatR, flow services, transformers (keyed by system pair), validators.
- `IServiceCollection.AddInfrastructure(IConfiguration)` — options binding, persistence, security, retry, connectors, Data Protection, health checks.

Conventions:
- Register **interface → implementation**: `services.AddScoped<IFooRepository, FooRepository>();`
- **Scoped** for anything touching the `DbContext` (repositories, unit of work, tenant/correlation context, flow services). **Singleton** for stateless helpers (password hasher, rule evaluator, signing key provider, API-key validator). Connectors are **scoped** (they cache a token per request/job).
- Group registrations into private `Add…()` helpers inside `Infrastructure.DependencyInjection` (e.g. `AddPersistence`, `AddSecurity`, `AddRetry`, `AddConnectors`).
- Host-specific overrides (e.g. the API's `HttpContextActorAccessor`) are registered in the host's extension and rely on `TryAdd…` defaults in Infrastructure.

---

## 3. Persistence

- One `DbContext`: `EmsPortalDbContext`. Entity mappings live in `Persistence/Configurations/*` as `IEntityTypeConfiguration<T>` (applied via `ApplyConfigurationsFromAssembly`). **No data annotations on entities.**
- **Repositories stage changes; they do not save.** Commit via `IUnitOfWork.SaveChangesAsync()`. This lets an action and its audit entry commit in one transaction.
- The `AuditTrail` repository is **append-only** — expose `AddAsync` only; never an update/delete path. The Universal Features `ActivityEvent` and `FieldModifiedLog` tables are append-only the same way (read API only; written in-process).
- All public repo methods are `async` and take a `CancellationToken` (default it).
- Enums are persisted via `.HasConversion<…>()` — **`<int>`** for most domain enums (`EntityType`, `NotificationType`, …) and **`<string>`** only where DB readability matters (e.g. `TenantStatus`). Pick one per enum and stay consistent across the entity config and any seed data.
- **One allowed Domain attribute:** entities are POCOs with no framework attributes, **except** the `[TrackedField]` marker (Universal Features — Modified Log) which carries no EF/framework dependency and is read by reflection at startup.

### Migrations

```bash
dotnet ef migrations add <Name> \
  --project src/EmsPortal.Infrastructure \
  --startup-project src/EmsPortal.Api \
  --output-dir Persistence/Migrations
```

The API applies migrations on startup. Never edit an already-applied migration — add a new one.

---

## 4. Multi-tenancy (non-negotiable)

- Tenant-scoped entities (`AuditTrail`, `Person`, `SmtpAccount`, `PermissionGroup`, `UserGroup`, and **every Universal Features table**) carry a `TenantId`.
- `EmsPortalDbContext` applies a **global query filter** keyed to `ITenantContext.TenantId`, and **stamps `TenantId` on insert** in `SaveChanges`. You normally don't write tenant filters by hand.
- **Checklist for any new tenant-scoped entity** (miss one and it leaks across tenants): (1) add the `DbSet`, (2) add the combined filter `(!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted` in `OnModelCreating`, (3) add a `StampTenant` switch case. Append-only tables with no soft-delete (e.g. `FieldModifiedLog`) drop the `&& !e.Deleted` part.
- The filter is a **no-op when no tenant is resolved** (background/global ops). For cross-tenant admin queries (Super Admin), use `.IgnoreQueryFilters()` with an explicit `TenantId` filter.
- `ITenantContext` is set by `TenantResolutionMiddleware` (API) from the JWT `activeTenantId`, and by the Hangfire tenant filter (Worker) from the job payload — **before** any data access.
- Tenant credentials are resolved per-tenant at runtime via `ITenantApiConfigurationService` and decrypted with `ICredentialEncryptionService` (Data Protection). Connectors never read credentials from `appsettings`.

---

## 5. API conventions (controllers)

- One `[ApiController]` per resource area. Route at the class or action level.
- **Every response goes through `ApiResponseFactory`** (`Success` / `Paginated` / `Error` / `NotFound` / `Unauthorized` / `Forbidden`). Never return raw objects.
- **Validation** is FluentValidation `AbstractValidator<TRequest>` classes (auto-registered from the assembly). Invalid models are turned into a `VALIDATION_FAILED` envelope by the global `ValidationActionFilter` — controllers assume the model is valid.
- **Authorization** via policy attributes: `[Authorize(Policy = AuthorizationPolicies.TenantAdminOrAbove)]` etc. In-code tenant-scope checks (e.g. a Tenant Admin acting on another tenant) return `403` explicitly.
- Read claims via `User.GetUserId()` / `GetActiveTenantId()` / `GetRole()` / `IsSuperAdmin()` (extensions in `Api/Security`).
- **Document** each action: `[Tags("Area")]` at the class, `[ProducesResponseType<T>(status)]` for success and `ApiErrorResponse` for errors. Use stable codes from `ApiErrorCodes`.
- Long-running work returns **`202 Accepted` + a `jobId`** and is enqueued to Hangfire — never executed in the request thread.

---

## 6. Error handling, logging & correlation

- Unhandled exceptions are caught by `ExceptionHandlingMiddleware` → `500` with an `INTERNAL_ERROR` envelope carrying the correlation id. Detail is exposed only when `ErrorHandling:IncludeExceptionDetails` is on (default: Development).
- Every request gets an `X-Correlation-Id` (`CorrelationIdMiddleware`), pushed into Serilog's `LogContext` so all log entries carry it.
- Use the injected `ILogger<T>`; let Serilog enrich (correlation id, service, environment). **Never log secrets** — the Authorization and `X-Api-Key` headers and credential fields are redacted.

Pipeline order (API): `CorrelationId → ExceptionHandling → RequestResponseLogging → Authentication → Authorization → TenantResolution → endpoints`.

---

## 7. Security

- Passwords: PBKDF2-SHA256, ≥100k iterations, per-user salt (`IPasswordHasher`). Never store or log plaintext. `GenerateTemporaryPassword()` returns a 16-char strong password (one of each character class).
- JWTs are RS256, signed and validated with the same key (`ISigningKeyProvider`). Claims: `sub`, `email`, `activeTenantId`, `role`, `tokenVersion`, `tenantAssignments`, and permission claims.
- Increment `tokenVersion` on password change, deactivation, email change, and logout to invalidate outstanding tokens.
- Tenant credentials are encrypted at rest via `ICredentialEncryptionService`; GET responses return masked indicators only.
- **Permission-based RBAC.** Gate endpoints with `[RequirePermission(Permissions.<Area><Action>)]` (keys live in `EmsPortal.Shared.Security.Permissions`). Seeded system-role permission sets (`ForSuperAdmin`/`ForTenantAdmin`) are re-applied on every startup by `BootstrapSeeder`, so adding a key to the catalogue grants it to system roles without a data migration.
- **Super-Admin-only actions** (delete a `Person`, assign/remove a user's tenant role) carry an explicit `User.IsSuperAdmin()` guard in the controller *in addition to* `[RequirePermission]`, so they stay Super-Admin-only even if a custom role is granted the permission. Tenant Admins do **not** hold `persons.delete` or `roles.assign`.
- **Identity (WO-61):** a `User` holds auth/account data only; personal/contact/professional data lives on a linked `Person` master record (with `Address`/`Media`). Creating a user **promotes an existing `Person`** (`personId`) rather than creating identity inline.

---

## 8. Extending the platform

### Add a new external system (connector)

1. Define `I<System>Connector` + its DTOs in `Application/Abstractions/Connectors/<System>`.
2. Implement `<System>Connector` in `Infrastructure/Connectors/<System>`: resolve per-tenant credentials via `ITenantApiConfigurationService`, cache the auth token in-memory, and normalize HTTP failures with `ConnectorError` (transient → `IsRetriable = true`).
3. Register it scoped in `Infrastructure.DependencyInjection.AddConnectors`.

### Add a new integration flow

1. **Transformer** — extend `TransformerBase<TSource, TDest>` (Application/`<Feature>`). Implement `ExtractFields` / `BuildDestination`; fall back to source values when a mapping is absent. Register keyed by `(SourceSystem, DestinationSystem)` via `AddTransformer<…>`.
2. **Validator** — implement `IValidator<TPayload>` (non-short-circuiting: return all violations).
3. **Integration service** — orchestrate `fetch → validate → transform → write`; on transient failure call `IRetryQueueManager.RegisterFailureAsync`, mark the job `Completed`/`Failed`/`PartiallyFailed`, and write `IntegrationLog` + audit.
4. **MediatR command + handler** — `record …Command(Guid JobId) : IRequest<IntegrationFlowResult>`; the handler delegates to the service.
5. **Hangfire job** — add a class in `Infrastructure/Jobs` (`RunForJobAsync` for API-triggered, `RunRecurringAsync` to fan out across tenants). Register it scoped.
6. **Controller endpoint** — create a tenant-stamped `IntegrationJob`, enqueue the job via `IBackgroundJobClient`, return `202 + jobId`.
7. **Schedule** (if recurring) — add a `JobScheduleConfiguration` seed row; `HangfireJobScheduler` registers required jobs and errors on a missing schedule.

### Add an admin/query endpoint

Add a controller action gated by `[RequirePermission(Permissions.<key>)]`, a FluentValidation request DTO, repository query methods (use `IgnoreQueryFilters` + explicit `tenantId` for Super-Admin cross-tenant reads), and `[ProducesResponseType]` annotations. For an action that must be Super-Admin-only regardless of permissions, also guard with `if (!User.IsSuperAdmin()) return 403`.

### Add a permission

Add the key to `EmsPortal.Shared.Security.Permissions` (`area.action`), include it in `All` and the relevant `For<Role>()` set(s). It is re-seeded onto system roles on the next startup. Mirror the key into the web side (`WEB/src/composables/usePermissions.js`) so the UI can gate controls. *(Phase 14 added `settings.manage` and `records.adminDelete` this way — both granted to Tenant Admin + Super Admin.)*

### Add a new feature module (full-stack checklist)

This is the canonical "where do I put everything" list for a new CRUD-style module (e.g. a new admin-managed entity). **File names match the type; one public type per file.** Work outward through the layers.

**Backend (`API/src`)** — create in this order, building after each layer:

| # | Layer · folder | File(s) to add | Naming standard |
|---|----------------|----------------|-----------------|
| 1 | `Domain/Entities` | `Widget.cs` (extends `AuditableEntity`; `Guid Id`; `Guid TenantId` if tenant-scoped) | Entity = singular PascalCase. Properties PascalCase. UTC fields suffixed `…OnUtc`. |
| 2 | `Domain/Enums` | `WidgetStatus.cs` (if needed) | Enum singular PascalCase; explicit integer values when persisted. |
| 3 | `Infrastructure/Persistence/Configurations` | `WidgetConfiguration.cs` (`IEntityTypeConfiguration<Widget>`, `internal sealed`) | `<Entity>Configuration`. Table name plural (`builder.ToTable("Widgets")`). Unique indexes filtered `[Deleted] = 0`. |
| 4 | `Infrastructure/Persistence/EmsPortalDbContext.cs` | add `DbSet<Widget>`, query filter, `StampTenant` case (see the tenant checklist above) | DbSet name plural. |
| 5 | `Application/Abstractions/Persistence` | `IWidgetRepository.cs` (interface; `async` + `CancellationToken`; repos **stage**, never save) | `I<Entity>Repository`. Methods `GetByIdAsync`/`ListAsync`/`AddAsync`/`Update`/`Remove`; Super-Admin reads via a `…ForTenantAsync`/`…UnscopedAsync` variant. |
| 6 | `Infrastructure/Persistence/Repositories` | `WidgetRepository.cs` (`internal sealed`, implements the interface) | `<Entity>Repository`. |
| 7 | `Infrastructure/DependencyInjection.cs` | `services.AddScoped<IWidgetRepository, WidgetRepository>();` in `AddPersistence` | interface → implementation, **Scoped** for anything touching the DbContext. |
| 8 | `Application/…` (optional) | `IWidgetService` + `WidgetService` for non-trivial business rules; register in `Application.DependencyInjection` | `I<Name>Service` / `<Name>Service`. |
| 9 | `Shared/Security/Permissions.cs` | new `area.action` keys + add to `All` / `For<Role>()` | constant `PascalCase`, value `area.action` (lower camel action). |
| 10 | `Api/Models/<Area>` | `WidgetModels.cs` — request **classes** (`Create…Request`/`Update…Request`) + response **records** (`…Response`) | Requests = mutable `class` (model binding); responses = `record`. No raw entity exposure. |
| 11 | `Api/Validators/<Area>` | `WidgetValidators.cs` (`AbstractValidator<TRequest>`, auto-registered) | `<Request>Validator`. |
| 12 | `Api/Controllers` | `WidgetsController.cs` (`[ApiController]`, `[Authorize]`, `[RequirePermission(...)]`, every response via `ApiResponseFactory`) | `<Plural>Controller`. Route `/api/admin/<plural>` (admin) or `/api/<plural>`. |
| 13 | migration | `dotnet ef migrations add Add<Feature>` | descriptive PascalCase; never edit an applied migration. |
| 14 | `tests/EmsPortal.UnitTests` | `<Type>Tests.cs` (xUnit + Moq + FluentAssertions) | `<Type>Tests`; one fact per behaviour. |

**Frontend (`WEB/src`)** — see also `WEB/README.md`:

| # | Folder | File(s) to add | Naming standard |
|---|--------|----------------|-----------------|
| 1 | `services/api.js` | a `widgetApi` resource group (`list`/`get`/`create`/`update`/`remove`); lists use `.then(envelope)`, single items `.then(unwrap)` | `<entity>Api` camelCase object. |
| 2 | `composables/usePermissions.js` | mirror any new permission keys into the `Permissions` object | key `PascalCase`, value matches backend. |
| 3 | `modules/<feature>/routes.js` | route block (lazy `component: () => import(...)`, `meta.requiresAuth`, `meta.permissions`) | route `name` snake_case (`widget_detail`). |
| 4 | `router/index.js` | `import` + `routes.push(...<feature>Routes)` | — |
| 5 | `modules/<feature>/pages/` | `index.vue` (list: `AppListHeader` + `AppDataTable` + `useListTable`) and `detail.vue` (`AppDetailHeader` + cards) | `<script setup>`; kebab-case component usage in templates. |
| 6 | `modules/<feature>/components/` | `WidgetFormDrawer.vue` (`AppFormDrawer` + `q-form`) | `<Entity>FormDrawer`. |
| 7 | `components/app_menu.vue` | a nav item under the right section, gated by `permissions: [Permissions.X]` | — |
| 8 | `test/unit` | `<Thing>.spec.js` (Vitest; mock `services/api` + composables, stub heavy children) | `<Thing>.spec.js`. |

**Reuse, don't hand-roll:** `AppDataTable`/`useListTable`, `AppFormDrawer`/`AppViewDrawer`/`AppFilterDrawer`, `AppDetailHeader`/`AppListHeader`, the `App*` field inputs, `useNotify`/`useConfirm`/`usePreferences`. Show resolved names, never raw ids.

### Add a Universal Feature to a new entity type

Universal Features (conversations, tags, attachments, activity, reminders, pins, colour codes, checklists, sticky notes, deleted-records, modified-log) attach to any entity via the shared **`(EntityType, EntityId)`** key — no per-entity tables (Universal Features ADR-001). To onboard a new entity type:

1. Add the value to the `EntityType` enum (`Domain/Enums/EntityType.cs`) — **no migration**; all UF tables already serve it.
2. Map it in `Api/Security/UniversalFeatureEntityAccess.cs` (its base read permission) so UF endpoints gate correctly.
3. Frontend: add it to `EntityType` in `services/api.js` and to `composables/uf/useEntityMeta.js` (label, icon, detail-route resolver for permalinks/pins/mentions).
4. To support **Deleted Records Management** for it, add a `case` to `DeletedRecordsRepository` (list/identity/restore/hard-delete projections).
5. To field-track a property for **Modified Log**, decorate it with `[TrackedField(EntityType.X, "Label", isSystemTracked: …)]` and render a `<FieldLogIcon>` beside its input (with `useFieldLogCounts` on the detail page).
6. Drop `<EntityUniversalPanel :entity-type :entity-id>` into the detail page and `<EntityHeaderActions>` into its header. `EntityUniversalPanel` is a configurable common component — props `tabs` (which sections + order), `show-tags`, `title`, `initial-tab`.
7. To expose the per-record actions (pin, colour, reminder, copy link, export PDF) in a **list row's "more" menu**, drop `<EntityRowActionsMenu :entity-type :entity-id>` and inject the page's own View/Edit/Delete `q-item`s into its default slot. It floats colour as a left-edge accent and a pin badge per row (see any entity list page): load the user's colours (`ufColourApi.batch`) and pins (`ufPinApi.list`) for the page, pass `:pinned-row-keys` to `AppDataTable` (floats pinned to the top after sort) and handle its `@colour-change`/`@pin-change`. Pins are capped at **5 per entity type** server-side (`PersonalFeaturesController`); to keep pinned rows on page 1, have the list endpoint accept `pinnedFirstIds` and order them first. Icons must use the **outlined** set (`o_push_pin`, `o_palette`, `o_chevron_right`) — filled names render as ligature text.

### Add a file upload to a module

Every module's uploads share one tree under the host content root. **Never compose a storage path by hand** — build a `StorageLocation` and let `IFileStorage` place it, or the module's files end up somewhere nothing else knows to look:

```
media-uploads/{tenantId:N}/{EntityType}/{recordKey}/{purpose}/{slug}__{shortId}{ext}
media-uploads/8f3a…/Rems/REMS-42/client-acceptance/caf-signed-2026__4a2e88c1.pdf
media-uploads/8f3a…/Person/PER-A1B2C3D4E5/profile/avatar__9de10f4b.png
media-uploads/8f3a…/_unassigned/2026/08/documents/…          ← no parent record; swept, not a home
```

- **`recordKey`** is the record's human number where it has one (`REMS-42`, `PER-A1B2C3D4E5`), else its id. `Api/Storage/UploadRecordKeyResolver.cs` decides — add a `case` there when a new entity type gains a number. Returning `null` means "no such record", which is what stops a file being filed against a made-up id.
- **`purpose`** comes from the file's `MediaCategory` via `StoragePaths.PurposeFor` — the category is what the uploader declares the file to *be*, so two screens uploading the same kind of document land in the same folder. Add a `MediaCategory` member (persisted **as its name**, ≤20 chars, never renamed) rather than passing a folder string.
- **Uploading:** `POST /api/media` with `entityType` + `entityId`; the SPA calls `mediaApi.upload(file, category, { type, id })`. Pass the parent id wherever it is known — every current caller has one in scope at the point of upload. Omitting it is legal but files to `_unassigned`.
- **Serving/deleting:** always through `IFileStorage.OpenAsync`/`DeleteAsync`. It is the only thing that turns a stored relative path back into a disk path, and the only thing that checks the result has not escaped the root. Rows written before this structure (`media-uploads/{guid}.png`) still resolve through it unchanged — the stored path is per-row, so there was nothing to migrate.
- `Attachment.StoredPath` is `nvarchar(500)`; a realistic worst-case path is ~160 chars. Uploaded names are slugged to ASCII for the on-disk name — the original is kept verbatim on the row and is what a download is named.

### Add or use an Option Set (tenant-configurable input value lists)

Option Sets are admin-managed dropdown value lists keyed by `Key` (the entity type was dropped on 16 Nov 2026) — e.g. a "Department" list. A list with `TenantId == null` is a platform-**standard** seeded list (`IsSystem = true`, visible to every tenant, read-only in the app); a `TenantId`-bearing list is that tenant's own. Tables `OptionSets` / `OptionSetItems`; sort modes `AlphabeticalAsc`/`AlphabeticalDesc`/`Custom` (drag-reorder); dependency chains via `ParentSetId` on the list + the `OptionSetItemParents` link table (a value may sit under several parent values; none means every parent).

- **Consume options in a form:** use the reusable `<AppOptionPicker :entity-type :option-key="'payment_terms'" v-model="…">` (`components/common/AppOptionPicker.vue`); pass `:parent-item-id` for a cascading child list. It resolves via `GET /api/option-sets/resolve` (composable `useOptionSet`).
- **Seed a new standard list:** add a `Definition` to `Application/OptionSets/DefaultOptionSets.cs` — `BootstrapSeeder` inserts it idempotently (`TenantId = null`, `IsSystem = true`). Scope to an existing `EntityType` member.
- **Scoping:** nullable-`TenantId` like `EmailTemplate` — query filter is `!Deleted` only (a tenant filter would hide the standard rows); the repo scopes explicitly as `TenantId == null || == current`. Write operations read the tenant from `ITenantContext`; standard sets are not editable through the API.
- **Permissions:** `optionSets.read` / `optionSets.manage` (Tenant Admin + Super Admin). API `/api/option-sets` (CRUD + `/items` + `/items/reorder` + `/resolve`); admin UI in `modules/option-set/`.

---

## 9. Coding conventions

- C# latest, **nullable reference types enabled**, implicit usings enabled. Treat warnings seriously.
- `async`/`await` end-to-end; accept and propagate `CancellationToken`. Don't block on async (`.Result`/`.Wait()`).
- Prefer `sealed` classes; `internal` for Infrastructure types unless a host needs them.
- One public type per file; file name matches the type.
- Match the surrounding code's style — comment density, XML docs on public abstractions, naming (`I` prefix for interfaces, `…Options` for config, `…Repository`/`…Service` for roles).
- Constants over magic strings (see `Roles`, `AuthorizationPolicies`, `ApiErrorCodes`, `ConfigurationSections`).

---

## 10. Testing

- **Unit tests** (`tests/EmsPortal.UnitTests`): xUnit + Moq + FluentAssertions. Mock all dependencies; test pure logic (validators, rule evaluator, backoff, hashing), services with mocked repos, and controllers by instantiating them with a mocked `ClaimsPrincipal` (`WithUser(...)`). `InternalsVisibleTo` exposes Infrastructure internals.
- **Integration tests** (`tests/EmsPortal.IntegrationTests`): `WebApplicationFactory<Program>` against a dedicated SQL test DB; a shared collection fixture boots the app once. Cover endpoint auth/status codes, repository round-trips, and migration idempotency.
- RBAC **policy** enforcement (e.g. `SuperAdminOnly`) is verified by integration tests, not unit tests (policies aren't applied when a controller method is called directly).
- Run `dotnet test EmsPortal.sln` before opening a PR; keep it green.

---

## 11. Git & workflow

- Branch off `main`; never commit directly to `main` for feature work.
- Conventional, descriptive commit messages; reference the work order (e.g. `WO-21: …`) where applicable.
- Keep the build and tests green in every commit. Run `dotnet build -c Release` and `dotnet test`.
- Don't commit secrets. `.gitignore` excludes `bin/`, `obj/`, `.vs/`; keep real credentials out of `appsettings.json`.

---

## 12. Configuration & secrets

- Bind config to strongly-typed `…Options` in `Shared.Configuration`; reference sections via `ConfigurationSections` constants.
- For local dev use `dotnet user-secrets`; for deployment use environment variables or a secrets manager for the connection string, RSA signing key, and bootstrap password.
- The Data Protection key ring is persisted to SQL so all instances share it — don't let it diverge across environments.
</content>
