# Option sets fed from Maconomy — plan

Status: **planned, not built** (25 Nov 2026). Nothing has been probed, migrated or coded yet.

## Why

Some option lists (Department is the first) are maintained by hand in the portal but already exist in
Maconomy. When Maconomy changes a text or a value, or removes one, the portal does not know until a sync
to Maconomy fails. A list marked as fed from Maconomy is read from it directly, and a nightly job keeps
it in step.

## Decisions (user, 25 Nov 2026)

1. The option set stores a **source**, not a full URL. The server address and instance code stay on the
   tenant's Maconomy connection.
2. A value removed in Maconomy is **deactivated automatically** in the portal.
3. **Department** is the first list.
4. A **read-only probe** of the test instance (thmasd01) is approved.

## Source design

Only the container name is stored (`specification2`), not `/containers/thmasd01/specification2/filter`:
the portal already builds `/containers/{instance}/{container}/filter` from the connection, and a stored
path would break when the instance changes from test to production. The filter payload is built from
plain columns rather than stored as JSON.

New columns on `OptionSets`:

| Column | Example | What it is |
|---|---|---|
| `IsMaconomyOption` | true | The list is fed from Maconomy |
| `MaconomyContainer` | `specification2` | The container to filter |
| `MaconomyValueField` | `specification2name` | Field holding the value; stored on the item as its Maconomy number |
| `MaconomyTextField` | `description` | Field holding the text; becomes the item's label |
| `MaconomyRestriction` | blank, or `blocked = false` | Optional filter |
| `MaconomyLastSyncedOnUtc` | | When the last sync ran |
| `MaconomyLastSyncResult` | "2 added, 1 renamed, 0 deactivated" or the error | What it did |

Payload sent:

```json
{ "restriction": "", "fields": ["specification2name", "description"], "limit": 500 }
```

Items need no new column: `OptionSetItems.MaconomyNumber` takes the value field, `Label` the text field.

## Sync rules

- **Match:** by `MaconomyNumber`.
- **Renamed in Maconomy:** the label is updated.
- **New in Maconomy:** added as an active item.
- **Removed in Maconomy:** deactivated, never deleted (requests point at the item).
- **Internal code:** `Value` on an existing item is never rewritten; rules in code look for values such as `tax`.
- **Safety stop:** an unreachable Maconomy or an empty reply changes nothing.
- **Still editable in the portal:** colour, sort order, default, dependencies between lists. Label and
  Maconomy number become read-only on a fed list.

## Build order

1. **Probe (read-only, thmasd01).** Which container and fields return Department; whether its values
   match the numbers already on the items (Assurance 1, Tax 2, Admin 3, GCS 4, CAS 5); whether there is
   a "blocked" field to filter on.
2. **Migration.** The seven columns above.
3. **Option Sets screen.** A "Maconomy" section on the list's edit dialog (switch, container, value
   field, text field, restriction), a "Test" button showing the rows Maconomy returns without saving,
   the last-sync line, and "Sync now".
4. **Sync service.** One service used by Sync now and the nightly job; every change audited.
5. **Nightly job.** One recurring job covering every tenant with a connection, visible on the
   Background Jobs screen. Emails the tenant's developer addresses when anything changed or failed.
6. **Department first.** Switch on for Department only, Test, then Sync now, and check the result
   before any other list (Entity Type, Industry, Job Type are the likely next ones).

## Open points

- **Department may not be in a `specification` container.** The job sync sends Department as a location
  number, so its list may live elsewhere. The design handles either; the probe settles it.
- **Audit and Consulting.** Audit has no Maconomy number today; Consulting (6) exists in Maconomy with
  no portal department. After the first sync Audit would be deactivated and Consulting added, unless
  decided otherwise.
- **Who may edit the source.** Proposed: Super Admin and Tenant Admin only (not confirmed).
- **Lists differ per tenant.** A tenant without a Maconomy connection keeps maintaining lists by hand.
- **A deactivated item can block a draft** that already holds it, until a new value is picked.
