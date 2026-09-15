import { reactive, computed } from "vue";

// Per-column filtering for AppDataTable lists. A column filters as a text "contains" box by default; with
// `filterOptions` as a dropdown (`filterMultiple: true` for any-of, held as an array); with
// `filterType: "dateRange"` as a From/To pair of dates, held as { from, to } in ISO "YYYY-MM-DD".
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

  // Empty means unset: null, "", an empty selection, or a range with neither end.
  const hasValue = (v) => {
    if (v === null || v === undefined || v === "") return false;
    if (Array.isArray(v)) return v.length > 0;
    if (typeof v === "object") return !!(v.from || v.to);
    return true;
  };

  // Client-side filtering — only used when server === false (server mode filters in the API). A date
  // range is not applied here: the lists that carry one are all server-side.
  const filteredRows = computed(() => {
    if (server) return (rows && rows.value) || [];
    let result = (rows && rows.value) || [];
    for (const col of filterableColumns.value) {
      const fv = filters[col.name];
      if (!hasValue(fv) || col.filterType === "dateRange") continue;
      if (Array.isArray(fv)) {
        result = result.filter((r) => fv.includes(valueOf(col, r)));
      } else if (col.filterOptions) {
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

  const optionLabel = (col, value) => col.filterOptions.find((o) => o.value === value)?.label ?? value;

  const chipLabel = (col) => {
    const fv = filters[col.name];
    if (Array.isArray(fv)) return `${col.label}: ${fv.map((v) => optionLabel(col, v)).join(", ")}`;
    if (col.filterOptions) return `${col.label}: ${optionLabel(col, fv)}`;
    return `${col.label}: ${fv}`;
  };

  // One chip per filter — and one per END of a date range, so either can be taken off on its own.
  const filterChips = computed(() =>
    filterableColumns.value
      .filter((c) => hasValue(filters[c.name]))
      .flatMap((c) => {
        if (c.filterType !== "dateRange") return [{ key: c.name, label: chipLabel(c) }];
        const range = filters[c.name];
        return ["from", "to"]
          .filter((edge) => range[edge])
          .map((edge) => ({ key: `${c.name}:${edge}`, label: `${c.label} ${edge}: ${range[edge]}` }));
      }));

  // A range chip's key names the end it stands for.
  const removeFilter = (key) => {
    const [name, edge] = key.split(":");
    if (!edge) {
      filters[name] = null;
      return;
    }
    const next = { ...(filters[name] || {}), [edge]: "" };
    filters[name] = hasValue(next) ? next : null;
  };
  const clearFilters = () => { filterableColumns.value.forEach((c) => { filters[c.name] = null; }); };

  // A date range's two ends in the shape the server takes, each converted by `toUtc(isoDate, edge)` —
  // useDateFormat's zonedDayBoundaryUtc, which turns a picked day into that day's real UTC boundaries.
  const rangeBounds = (name, toUtc) => ({
    from: toUtc(filters[name]?.from, "start"),
    to: toUtc(filters[name]?.to, "end")
  });

  return { filters, filterableColumns, filteredRows, filterChips, removeFilter, clearFilters, rangeBounds };
}
