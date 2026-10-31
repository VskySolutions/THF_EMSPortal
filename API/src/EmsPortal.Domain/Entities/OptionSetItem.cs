namespace EmsPortal.Domain.Entities;

/// <summary>
/// A single selectable value within an <see cref="OptionSet"/> — e.g. "NET 30" in the Payment Terms
/// list. A null <see cref="TenantId"/> is a standard (seeded) value; a tenant may add its own values
/// (with its <see cref="TenantId"/>) to a standard list. For a dependency list, <see cref="ParentItemId"/>
/// links the value to the parent item it is valid under (e.g. State "California" → Country "USA").
/// Inherits the standard audit/soft-delete fields.
/// </summary>
public class OptionSetItem : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The list this value belongs to.</summary>
    public Guid OptionSetId { get; set; }

    /// <summary>Owning tenant, or null for a standard (seeded) value.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// For a dependency list, the parent-set item this value is valid under; null for a single group.
    /// </summary>
    public Guid? ParentItemId { get; set; }

    /// <summary>Stable programmatic code, unique per (set, tenant), e.g. <c>net_30</c>.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Human-friendly display label, e.g. "NET 30".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// What this value actually means, e.g. "Payment is due 30 days from the invoice date." Surfaced as
    /// the tooltip wherever the value is offered or displayed, so a list whose labels look alike can
    /// explain itself where it is used rather than in a manual nobody opens. Optional — a self-evident
    /// value needs none, and an empty description simply renders no tooltip.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Explicit position, honoured only when the set's sort mode is Custom.</summary>
    public int SortOrder { get; set; }

    /// <summary>When true, this value is pre-selected by default in the picker.</summary>
    public bool IsDefault { get; set; }

    /// <summary>When false, the value is hidden from pickers without being deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Optional background colour (hex, e.g. "#e3f2fd") shown for this value in the UI.</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Optional text colour (hex, e.g. "#0d47a1") shown for this value in the UI.</summary>
    public string? TextColor { get; set; }

    /// <summary>
    /// Optional Material icon name (e.g. <c>o_workspace_premium</c>) shown beside this value in the UI.
    /// A display property like the two colours above it — a list whose values are roles or events reads
    /// far faster with one, and hard-coding the icon per value in the front end is the thing this column
    /// exists to stop.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// True for a value the application itself depends on — one the server writes and branches on, such
    /// as an approval status. Seeded, and thereafter LOCKED in the ways that would break the workflow:
    /// it cannot be deleted, deactivated, or given a different <see cref="Value"/>.
    /// <para>
    /// Everything a tenant would actually want to change stays open: the <see cref="Label"/>, the
    /// <see cref="Description"/> behind its tooltip, the colours, the icon and the sort order. That is
    /// the whole point of the flag — these lists are managed in the app like every other, and only the
    /// codes underneath them are not up for negotiation.
    /// </para>
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>Optional extra metadata as a JSON object, e.g. <c>{"days":30}</c>.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// The number Maconomy files this value under, where the Maconomy sync needs one: the specification 6
    /// number on a REMS Entity Type, the specification 1 number on a REMS Industry, the template job number
    /// on a REMS Job Template. Set by hand under Option Sets; null means Maconomy gets no value for it.
    /// </summary>
    public string? MaconomyNumber { get; set; }

    /// <summary>The owning list (navigation).</summary>
    public OptionSet? OptionSet { get; set; }
}
