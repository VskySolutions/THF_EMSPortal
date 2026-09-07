import { EntityType } from "services/api";

// Display metadata + permalink routing for each Universal Features entity type.
// Used by cross-entity surfaces (Pinned Records, Mention Inbox, Activity, permalinks).
const META = {
  [EntityType.Tenant]: {
    label: "Tenant",
    icon: "o_apartment",
    route: (id) => ({ name: "tenant_detail", params: { id } })
  },
  [EntityType.User]: {
    label: "User",
    icon: "o_group",
    route: (id) => ({ name: "user_detail", params: { id } })
  },
  [EntityType.UserGroup]: {
    label: "User Group",
    icon: "o_groups",
    route: () => ({ name: "user_groups" })
  },
  // REMS request/approval.
  [EntityType.Rems]: {
    label: "REMS",
    icon: "o_assignment",
    route: (id) => ({ name: "rems_request", params: { id } })
  }
};

const FALLBACK = { label: "Record", icon: "o_description", route: () => ({ name: "dashboard" }) };

export function useEntityMeta () {
  const metaFor = (entityType) => META[Number(entityType)] || FALLBACK;
  const labelFor = (entityType) => metaFor(entityType).label;
  const iconFor = (entityType) => metaFor(entityType).icon;
  const routeFor = (entityType, entityId) => metaFor(entityType).route(entityId);
  // Permalink slug used by the /entity/:type/:id route convention.
  const typeSlug = (entityType) => String(Number(entityType));
  return { metaFor, labelFor, iconFor, routeFor, typeSlug };
}
