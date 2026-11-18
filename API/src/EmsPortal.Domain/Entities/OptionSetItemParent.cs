namespace EmsPortal.Domain.Entities;

/// <summary>
/// One "valid under" pairing of a dependency list: the value may be picked when this parent value is.
/// A value may sit under several parents (Consulting under four departments); one with no rows at all
/// is offered under every parent.
/// </summary>
public class OptionSetItemParent
{
    /// <summary>The dependent value.</summary>
    public Guid OptionSetItemId { get; set; }

    /// <summary>The parent-list value it is valid under.</summary>
    public Guid ParentOptionSetItemId { get; set; }
}
