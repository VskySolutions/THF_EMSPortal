using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// A named list of selectable input values scoped to a tenant — e.g. a
/// "Category" or "Department" list shown on a record. A row with a null
/// <see cref="TenantId"/> is a platform-standard list seeded in application code (visible to every
/// tenant, see <see cref="IsSystem"/>); a row with a <see cref="TenantId"/> is that tenant's own list.
/// <para>
/// Lists are either standalone (single group) or part of a dependency chain: a set whose
/// <see cref="ParentSetId"/> is non-null is driven by a parent set, and its items point at a parent
/// item via <see cref="OptionSetItem.Parents"/> (e.g. Industry depends on Entity Type). Inherits the
/// standard audit/soft-delete fields.
/// </para>
/// </summary>
public class OptionSet : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant, or null for a platform-standard (seeded) list.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Stable programmatic code, unique per tenant, e.g. <c>department</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-friendly display name, e.g. "Department".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// For a dependency list, the parent set that drives it; null for a standalone (single) list.
    /// </summary>
    public Guid? ParentSetId { get; set; }

    /// <summary>How the items are ordered in a picker.</summary>
    public OptionItemSortMode ItemSortMode { get; set; } = OptionItemSortMode.AlphabeticalAsc;

    /// <summary>True for a seeded platform-standard list; such lists are read-only in the app.</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// True when the application branches on this list's VALUES, so the set of codes is fixed and no new
    /// value may be added to it — an approval status the server never writes is a status nothing can
    /// reach, and a picker offering it is a dead end.
    /// <para>
    /// Distinct from <see cref="IsSystem"/>, which is about who owns the row. A closed list is still the
    /// tenant's own copy and is still managed in the app: its labels, descriptions, colours, icons and
    /// order are all theirs. Referral Source and Marketing Method are seeded too and are NOT closed —
    /// nothing branches on those, so a firm adding one of its own is exactly right.
    /// </para>
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>When false, the whole list is hidden from pickers without being deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The values belonging to this list.</summary>
    public ICollection<OptionSetItem> Items { get; set; } = new List<OptionSetItem>();
}
