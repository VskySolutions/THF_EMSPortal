import { useDateFormat } from "composables/useDateFormat";

// The four audit columns every list carries: who created a record and when, who last touched it and when.
export function useAuditColumns () {
  const fmt = useDateFormat();

  const dateCell = (key) => (row) => (row?.[key] ? fmt.formatDateTime(row[key]) : "—");
  const textCell = (key) => (row) => row?.[key] || "—";
  // What the column ORDERS by where a table sorts its own rows. The cell reads "MM/DD/YYYY hh:mm AM",
  // which as text sorts by month before year; the raw timestamp is an ISO instant and sorts as it stands.
  const dateSort = (key) => (row) => row?.[key] || "";

  return function auditColumns ({ overrides = {}, only = null, dateRanges = false } = {}) {
    const key = (name) => overrides[name] || name;
    // Created On / Updated On filter as From/To ranges on the lists whose server can narrow on them.
    const dateFilter = dateRanges ? { filterType: "dateRange" } : { filterable: false };

    // Created By / Updated By sort on the server by the name shown: every list's sort map resolves the actor's
    // name the way its cells do (see ActorNames on the API).
    const all = [
      { name: "createdBy", label: "Created By", field: textCell(key("createdBy")), align: "left", sortable: true, default: false, filterable: false },
      { name: "createdOnUtc", label: "Created On", field: dateCell(key("createdOnUtc")), sort: dateSort(key("createdOnUtc")), align: "left", sortable: true, default: false, ...dateFilter },
      { name: "updatedBy", label: "Updated By", field: textCell(key("updatedBy")), align: "left", sortable: true, default: true, filterable: false },
      { name: "updatedOnUtc", label: "Updated On", field: dateCell(key("updatedOnUtc")), sort: dateSort(key("updatedOnUtc")), align: "left", sortable: true, default: true, ...dateFilter }
    ];

    return only ? all.filter((c) => only.includes(c.name)) : all;
  };
}
