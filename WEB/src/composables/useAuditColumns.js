import { useDateFormat } from "composables/useDateFormat";

// The four audit columns every list carries: who created a record and when, who last touched it and when.
export function useAuditColumns () {
  const fmt = useDateFormat();

  const dateCell = (key) => (row) => (row?.[key] ? fmt.formatDateTime(row[key]) : "—");
  const textCell = (key) => (row) => row?.[key] || "—";
  // What the column ORDERS by where a table sorts its own rows. The cell reads "MM/DD/YYYY hh:mm AM",
  // which as text sorts by month before year; the raw timestamp is an ISO instant and sorts as it stands.
  const dateSort = (key) => (row) => row?.[key] || "";

  return function auditColumns ({ overrides = {}, only = null } = {}) {
    const key = (name) => overrides[name] || name;

    // Created By / Updated By are NOT sortable.
    const all = [
      { name: "createdBy", label: "Created By", field: textCell(key("createdBy")), align: "left", sortable: false, default: false, filterable: false },
      { name: "createdOnUtc", label: "Created On", field: dateCell(key("createdOnUtc")), sort: dateSort(key("createdOnUtc")), align: "left", sortable: true, default: false, filterable: false },
      { name: "updatedBy", label: "Updated By", field: textCell(key("updatedBy")), align: "left", sortable: false, default: true, filterable: false },
      { name: "updatedOnUtc", label: "Updated On", field: dateCell(key("updatedOnUtc")), sort: dateSort(key("updatedOnUtc")), align: "left", sortable: true, default: true, filterable: false }
    ];

    return only ? all.filter((c) => only.includes(c.name)) : all;
  };
}
