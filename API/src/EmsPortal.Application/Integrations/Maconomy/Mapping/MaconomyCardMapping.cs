namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// One card field as the settings page explains it: the Maconomy field, what the portal fills it from and
/// the rule that decides the value. <see cref="ReadBack"/> marks a field that is never sent: Maconomy
/// assigns it and the portal stores the reply.
/// </summary>
public sealed record MaconomyFieldMapping(string Field, string Label, string Source, string? Rule, bool ReadBack);

/// <summary>A card's mapping in words: the container written to and its fields in the order the write names them.</summary>
public sealed record MaconomyCardMapping(string Container, IReadOnlyList<MaconomyFieldMapping> Fields);
