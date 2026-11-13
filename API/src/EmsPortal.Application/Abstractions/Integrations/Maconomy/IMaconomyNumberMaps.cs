namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The Maconomy numbers a tenant's option items carry (<c>OptionSetItem.MaconomyNumber</c>), read once
/// per call and kept briefly: the specification 6 number on each REMS Entity Type, the specification 1
/// number on each REMS Industry, the template job number on each REMS Service Line, the specification 2
/// value on each REMS Job Type (its label where none is set), the location number on each REMS Department.
/// Every translation between a portal code and a Maconomy number goes through
/// the map; nothing holds a table of its own.
/// </summary>
public interface IMaconomyNumberMaps
{
    Task<MaconomyNumberMap> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Drops the tenant's cached map, so a number just saved under Option Sets is read on the next call.</summary>
    void Forget(Guid tenantId);
}

/// <summary>
/// One option item as the map sees it: the item, its code (the option value), its label, its Maconomy
/// number (null while unset) and whether the value is still offered.
/// </summary>
public sealed record MaconomyNumbered(Guid ItemId, string Code, string Label, string? Number, bool IsActive = true);

public sealed class MaconomyNumberMap
{
    public static readonly MaconomyNumberMap Empty = new(
        Array.Empty<MaconomyNumbered>(), Array.Empty<MaconomyNumbered>(), Array.Empty<MaconomyNumbered>(),
        Array.Empty<MaconomyNumbered>(), Array.Empty<MaconomyNumbered>());

    public MaconomyNumberMap(
        IReadOnlyList<MaconomyNumbered> entityTypes,
        IReadOnlyList<MaconomyNumbered> industries,
        IReadOnlyList<MaconomyNumbered> serviceLines,
        IReadOnlyList<MaconomyNumbered> jobTypes,
        IReadOnlyList<MaconomyNumbered> departments)
    {
        EntityTypes = entityTypes;
        Industries = industries;
        ServiceLines = serviceLines;
        JobTypes = jobTypes;
        Departments = departments;
        TemplateMap = ByItem(serviceLines);
        LocationMap = ByItem(departments);
        JobTypeValueMap = jobTypes.ToDictionary(
            i => i.ItemId,
            i => string.IsNullOrWhiteSpace(i.Number) ? i.Label.Trim() : i.Number!.Trim());
    }

    public IReadOnlyList<MaconomyNumbered> EntityTypes { get; }
    public IReadOnlyList<MaconomyNumbered> Industries { get; }
    public IReadOnlyList<MaconomyNumbered> ServiceLines { get; }
    public IReadOnlyList<MaconomyNumbered> JobTypes { get; }
    public IReadOnlyList<MaconomyNumbered> Departments { get; }

    /// <summary>REMS Service Line item id → template job number, for the lines that carry one.</summary>
    public IReadOnlyDictionary<Guid, string> TemplateMap { get; }

    /// <summary>
    /// REMS Job Type item id → what the job card's specification 2 is given: the item's Maconomy number
    /// where one is set, its label otherwise (the list was supplied as Maconomy names it).
    /// </summary>
    public IReadOnlyDictionary<Guid, string> JobTypeValueMap { get; }

    /// <summary>REMS Department item id → Maconomy location number, for the items that carry one.</summary>
    public IReadOnlyDictionary<Guid, string> LocationMap { get; }

    private static Dictionary<Guid, string> ByItem(IReadOnlyList<MaconomyNumbered> items) => items
        .Where(i => !string.IsNullOrWhiteSpace(i.Number))
        .GroupBy(i => i.ItemId)
        .ToDictionary(g => g.Key, g => g.First().Number!.Trim());

    /// <summary>The specification 6 number a REMS entity type code is filed under, or null for a code without one.</summary>
    public string? EntityTypeNumberOf(string? code) => NumberOf(EntityTypes, code);

    /// <summary>The specification 1 number a REMS industry code is filed under, or null for a code without one.</summary>
    public string? IndustryNumberOf(string? code) => NumberOf(Industries, code);

    /// <summary>The REMS entity type a specification 6 value stands for, by its number, or null when it stands for none.</summary>
    public MaconomyNumbered? EntityTypeOf(string? specification6)
    {
        var number = (specification6 ?? string.Empty).Trim();
        return number.Length == 0 ? null : EntityTypes.FirstOrDefault(t => string.Equals(t.Number?.Trim(), number, StringComparison.Ordinal));
    }

    /// <summary>
    /// The REMS industry a specification 1 value stands for: by the number it starts with ("17",
    /// "17-Manufacturing"), by the label otherwise, or null when it stands for none.
    /// </summary>
    public MaconomyNumbered? IndustryOf(string? specification1)
    {
        var text = (specification1 ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }
        var number = new string(text.TakeWhile(char.IsDigit).ToArray());
        if (number.Length > 0)
        {
            return Industries.FirstOrDefault(i => string.Equals(i.Number?.Trim(), number, StringComparison.Ordinal));
        }
        var key = Letters(text);
        return Industries.FirstOrDefault(i => Letters(i.Label) == key);
    }

    private static string? NumberOf(IReadOnlyList<MaconomyNumbered> items, string? code)
    {
        var wanted = (code ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return null;
        }
        var number = items.FirstOrDefault(i => string.Equals(i.Code, wanted, StringComparison.OrdinalIgnoreCase))?.Number?.Trim();
        return string.IsNullOrEmpty(number) ? null : number;
    }

    // Letters and digits alone, lower-cased: "Insurance - Health" and "insurance health" are one value.
    private static string Letters(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
