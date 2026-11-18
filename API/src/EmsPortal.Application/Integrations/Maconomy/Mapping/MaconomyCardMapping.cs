namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// One line of a card's mapping as the settings page shows it: a field of the form, under the form's own
/// section heading, and the Maconomy field it fills. <see cref="Field"/> and <see cref="Label"/> are null
/// for a form field that is not sent. <see cref="ReadBack"/> marks a field Maconomy assigns and the portal
/// stores; <see cref="IsDocument"/> a file uploaded to the record's documents rather than a card field.
/// </summary>
public sealed record MaconomyFieldMapping(
    string Section,
    string FormField,
    string? Field = null,
    string? Label = null,
    string? Rule = null,
    bool ReadBack = false,
    bool IsDocument = false);

/// <summary>
/// A card's mapping in words: the container written to, and every field of the forms in the order the
/// forms ask them, mapped or not. A form field added later is added here by hand.
/// </summary>
public sealed record MaconomyCardMapping(string Container, IReadOnlyList<MaconomyFieldMapping> Fields);
