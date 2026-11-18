# COP Portal — Web

The admin SPA for COP Portal, built with **Quasar 2 / Vue 3**. It provides the UI for tenants,
people, users, roles, mapping configuration, and integration monitoring (jobs, logs, retries)
against the COP Portal REST API.

## Getting started

```bash
npm install        # install dependencies
quasar dev         # run in dev (hot reload)
npm run lint        # lint
quasar build       # production build
```

Configure the API base URL and other settings via `quasar.config.js` / environment (see
[Configuring quasar.config.js](https://v2.quasar.dev/quasar-cli-vite/quasar-config-js)).

---

## UI standards & shared building blocks

These are enforced conventions — **reuse the shared pieces; do not hand-roll equivalents.** When a
pattern repeats, extract a component/composable.

### Inside pages (detail / view / manage / account)
- **`components/common/AppDetailHeader.vue`** — carded header: breadcrumbs left, **Back** right,
  optional `#actions` slot. No separate page-title row.
- Full-width `q-card flat bordered` cards (no centered `max-width`); blue `text-subtitle1` headers.
- Read-only rows: `q-item` as *icon + caption label + value*. Use `q-gutter-*` for flex children
  like avatars — **never** `q-col-gutter-*` (grid-only; breaks avatar centering).

### List pages (CRUD)
- **`components/common/AppListHeader.vue`** — breadcrumbs, search, Filters button, Create, Back.
- **`components/common/AppDataTable.vue`** + **`composables/useListTable.js`** — server pagination,
  sortable columns, show/hide columns, **drag-to-reorder** columns (`useColumnOrder`), resizable
  columns, multi-select (`selectable`) with a `#bulk-actions` slot, "Actions" column title,
  **View action as a direct icon** outside the `⋮` menu, 20px action icons.
- **Filters (standard on every list):** `components/common/AppColumnFilters.vue` +
  `composables/useColumnFilters.js` (a control per column) inside the resizable `AppFilterDrawer`.
  Wire `show-filters` + `:filter-count="filterChips.length"` on `AppListHeader` so the **Filters
  button shows an applied-filter count badge**, and `:chips="filterChips"` on the drawer for the
  active-filter chips. Server-paginated lists filter in the API (`useColumnFilters(cols, rows)`,
  default server mode); load-all lists filter the loaded rows (`{ server: false }`, bind
  `:rows="filteredRows"` + `:total-records="filteredRows.length"`). Mark date/computed/count columns
  `filterable: false`; give enum/boolean columns `filterOptions` to render a select.

### Forms & fields (define once, reuse)
- Field components: `AppTextField`, `AppDateField`, `AppSelect`, `AppPhoneInput`,
  `AppDatePicker` (all **dense** for consistent height; selects show multi-select as badges).
- `AppPhoneInput` — country dial-code dropdown + as-you-type formatting (libphonenumber-js),
  stores **E.164**. Country dropdowns pin **US (default) + India** on top (`composables/useCountries.js`).
- **`AppFormDrawer`** (slide-in create/edit) and **`AppFilterDrawer`** are **drag-to-resize**
  via `composables/useDrawerResize.js` (widths persist until logout).
- Person form is defined once in `components/person/PersonFormFields.vue`, reused by the People
  drawer and the quick-add `PersonFormDialog.vue`.

### Tenancy & roles
- **`composables/useTenantOptions.js`** — only super/platform admins (`tenants.write`) get a tenant
  dropdown; everyone else is auto-scoped to their active tenant.
- Deleting a person and changing a user's role are **Super-Admin-only** (the UI hides these via the
  `persons.delete` / `roles.assign` permissions).

### Visual conventions (`src/css/custom.scss`)
- **Titles** 17px, theme blue (`var(--q-primary)`); **labels / buttons / breadcrumbs / badges** 15px.
- Dates display app-wide as **`MM-DD-YYYY hh:mm AM/PM`** in the active tenant's time zone
  (`composables/useDateFormat.js`).

### Universal Features (Phase 15)
Platform-wide collaboration/personalisation that attaches to **any** entity via an `(entityType, entityId)` key.
- **Reusable components** live in `components/universal/`: `EntityUniversalPanel` (Conversation / Activity /
  Checklists / Attachments tabs + tags), `EntityHeaderActions` (Pin · Colour · Reminder · Copy Link ·
  PDF), `FieldLogIcon` + `FieldModifiedLogDrawer` (field change history), `DeletedRecordsPanel`
  (Show Deleted · Restore · Permanently Delete), `NotificationCentre`, `StickyNoteLayer`.
- **Composables** live in `composables/uf/`: `useEntityMeta` (label/icon/permalink route per `EntityType`),
  `usePins`, `useColourCodes`, `useFieldLogCounts`, `useShowDeleted`, `useNotificationMeta`.
- **API** groups in `services/api.js`: `ufConversationApi`, `ufTagsApi`, `ufAttachmentsApi`, `ufActivityApi`,
  `ufReminderApi`, `ufNotificationApi`, `ufPinApi`, `ufColourApi`, `ufPdfApi`,
  `ufChecklistApi`, `ufStickyNoteApi`, `ufDeletedApi`, `ufModifiedLogApi` (+ the `EntityType` enum).
- **Standalone pages** + settings/admin pages live in `modules/universal/`.
- To attach UF to a new entity type, extend `EntityType` (api.js) + `useEntityMeta` and drop
  `<EntityUniversalPanel>` / `<EntityHeaderActions>` into its detail page. See
  `API/docs/DEVELOPMENT.md` → *Add a Universal Feature to a new entity type*.

---

## Project structure

```
src/
├── components/common/      # shared UI: AppDataTable, AppFormDrawer, AppFilterDrawer, AppDetailHeader,
│                           #            AppListHeader, AppSelect, AppTextField, AppPhoneInput, …
├── components/universal/   # Universal Features components (entity panels, header actions, sticky notes, …)
├── components/person/      # PersonFormFields, PersonFormDialog
├── composables/            # useListTable, useColumnFilters, useColumnOrder, useDrawerResize,
│   └── uf/                 # universal-features composables (useEntityMeta, usePins, useFieldLogCounts, …)
├── modules/                # feature modules: tenant, person, user, role, mapping, integration, …, universal
│   └── <feature>/{routes.js, pages/, components/}
├── services/api.js         # API client (resource groups per controller, incl. uf*Api)
├── stores/                 # Pinia: auth, tenant
└── css/                    # quasar.variables.scss, app.scss, typography.scss, custom.scss
```

> **Adding a new module/page?** Follow the full-stack checklist in
> [`API/docs/DEVELOPMENT.md`](../API/docs/DEVELOPMENT.md) → *Add a new feature module*, which lists every
> backend **and** frontend file to create, with the naming standard for each.
