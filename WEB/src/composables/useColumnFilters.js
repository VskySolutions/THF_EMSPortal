import { reactive, computed } from "vue";

// Per-column filtering for AppDataTable lists.
export function useColumnFilters (columnsInput, rows, options = {}) {
  const server = options.server !== false; // default: server-side
  const colList = () => (Array.isArray(columnsInput) ? columnsInput : (columnsInput.value || []));

  const filterableColumns = computed(() =>
    colList().filter((c) => c.name !== "actions" && c.filterable !== false));

  const filters = reactive({});

  // The value a column shows for a row (honours function `field` accessors, e.g. formatted dates).
  const valueOf = (col, row) => {
    const f = col.field;
    return typeof f === "function" ? f(row) : row[f ?? col.name];
  };

  const hasValue = (v) => v !== null && v !== undefined && v !== "";

  // Client-side filtering — only used when server === false (server mode filters in the API).
  const filteredRows = computed(() => {
    if (server) return (rows && rows.value) || [];
    let result = (rows && rows.value) || [];
    for (const col of filterableColumns.value) {
      const fv = filters[col.name];
      if (!hasValue(fv)) continue;
      if (col.filterOptions) {
        // `filterMatch: "includes"` matches when the (possibly multi-valued) cell contains the
        // selected option — e.g. a user assigned to several tenants. Default is exact equality.
        if (col.filterMatch === "includes") {
          result = result.filter((r) => { const v = valueOf(col, r); return v != null && String(v).includes(fv); });
        } else {
          result = result.filter((r) => valueOf(col, r) === fv);
        }
      } else {
        const needle = String(fv).toLowerCase();
        result = result.filter((r) => {
          const v = valueOf(col, r);
          return v != null && String(v).toLowerCase().includes(needle);
        });
      }
    }
    return result;
  });

  const chipLabel = (col) => {
    const fv = filters[col.name];
    if (col.filterOptions) {
      const opt = col.filterOptions.find((o) => o.value === fv);
      return `${col.label}: ${opt ? opt.label : fv}`;
    }
    return `${col.label}: ${fv}`;
  };

  const filterChips = computed(() =>
    filterableColumns.value
      .filter((c) => hasValue(filters[c.name]))
      .map((c) => ({ key: c.name, label: chipLabel(c) })));

  const removeFilter = (key) => { filters[key] = null; };
  const clearFilters = () => { filterableColumns.value.forEach((c) => { filters[c.name] = null; }); };

  return { filters, filterableColumns, filteredRows, filterChips, removeFilter, clearFilters };
}
