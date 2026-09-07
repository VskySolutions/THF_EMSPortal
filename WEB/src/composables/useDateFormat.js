import { useTenantStore } from "stores/tenant";

/** A calendar date — a DateOnly "YYYY-MM-DD" — as MM/DD/YYYY. Deliberately NOT part of the composable
    below and deliberately not routed through a time zone. */
export function formatDateOnly (value, placeholder = "—") {
  if (!value) return placeholder;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value));
  return m ? `${m[2]}/${m[3]}/${m[1]}` : String(value);
}

// Renders UTC timestamps in the active tenant's time zone.
export function useDateFormat () {
  const tenantStore = useTenantStore();

  const tenantTimeZone = () => tenantStore.activeTenant?.timeZoneId || "UTC";

  // Parse a backend value as a UTC instant (assume UTC when no tz designator).
  const toUtcDate = (value) => {
    if (!value) return null;
    if (value instanceof Date) return value;
    let s = String(value);
    if (!/[zZ]$|[+-]\d{2}:?\d{2}$/.test(s)) {
      s += "Z";
    }
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? null : d;
  };

  // en-US so the day-period renders as AM/PM; explicit 2-digit options keep numbers locale-stable.
  const partsFor = (date, options) => {
    const fmt = new Intl.DateTimeFormat("en-US", { timeZone: tenantTimeZone(), ...options });
    return Object.fromEntries(fmt.formatToParts(date).map((p) => [p.type, p.value]));
  };

  const formatDateTime = (value, placeholder = "—") => {
    const d = toUtcDate(value);
    if (!d) return placeholder;
    const p = partsFor(d, { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: true });
    return `${p.month}/${p.day}/${p.year} ${p.hour}:${p.minute} ${p.dayPeriod}`;
  };

  const formatDate = (value, placeholder = "—") => {
    const d = toUtcDate(value);
    if (!d) return placeholder;
    const p = partsFor(d, { year: "numeric", month: "2-digit", day: "2-digit" });
    return `${p.month}/${p.day}/${p.year}`;
  };

  // The clock time alone, for somewhere the DATE is already established — a conversation groups its
  // messages under a day heading.
  const formatTime = (value, placeholder = "—") => {
    const d = toUtcDate(value);
    if (!d) return placeholder;
    const p = partsFor(d, { hour: "2-digit", minute: "2-digit", hour12: true });
    return `${p.hour}:${p.minute} ${p.dayPeriod}`;
  };

  // How far the tenant's clock runs ahead of UTC at a given instant (DST-aware, since the offset is
  // read at that instant rather than assumed constant).
  const tzOffsetMs = (utcMs) => {
    const p = partsFor(new Date(utcMs), {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false
    });
    // Intl renders midnight as "24" in some locales' hour-cycle; normalise it back to 0.
    return Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour % 24, +p.minute, +p.second) - utcMs;
  };

  // The inverse of the formatters above: the UTC instant at which the tenant's clock reads the start (or
  // end) of the given yyyy-mm-dd.
  const zonedDayBoundaryUtc = (isoDate, edge = "start") => {
    if (!isoDate) return undefined;
    const [y, m, d] = String(isoDate).split("-").map(Number);
    if (!y || !m || !d) return undefined;
    const wall = edge === "end"
      ? Date.UTC(y, m - 1, d, 23, 59, 59, 999)
      : Date.UTC(y, m - 1, d, 0, 0, 0, 0);
    // One correction pass: read the offset at the naive instant, then shift by it. Only a boundary
    // falling inside a DST transition could land an hour out, which no day filter can express anyway.
    return new Date(wall - tzOffsetMs(wall)).toISOString();
  };

  return { formatDateTime, formatDate, formatTime, tenantTimeZone, zonedDayBoundaryUtc };
}
