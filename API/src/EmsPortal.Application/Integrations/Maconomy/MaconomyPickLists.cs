using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyPickLists"/>: one filter of the popup's container through the session
/// manager, kept per tenant and container for <see cref="MaconomyOptions.PickListCacheSeconds"/>.
/// </summary>
public sealed class MaconomyPickLists : IMaconomyPickLists
{
    // A popup container's own fields: the literal, the title a person reads, and whether it is offered.
    private const string ValueField = "value";
    private const string TitleField = "title";
    private const string HiddenField = "hidden";
    // The blank entry every popup carries.
    private const string Nil = "nil";
    private const int RowsMax = 2000;
    private const string TemplateContainer = "jobs";
    private const string TemplateNumberField = "jobnumber";
    private const string TemplateNameField = "jobname";

    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;
    private readonly IMemoryCache _cache;
    private readonly MaconomyOptions _options;

    public MaconomyPickLists(
        IMaconomySessionManager session, IMaconomyGateway gateway, IMemoryCache cache, IOptions<MaconomyOptions> options)
    {
        _session = session;
        _gateway = gateway;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<MaconomyPickList> GetAsync(MaconomyConnection connection, string container, CancellationToken cancellationToken = default)
    {
        var key = $"maconomy:picklist:{connection.TenantId:N}:{container}";
        if (_cache.TryGetValue(key, out MaconomyPickList? cached) && cached is not null)
        {
            return cached;
        }
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, container,
                new MaconomyFilterRequest(string.Empty, new[] { ValueField, TitleField, HiddenField }, RowsMax), ct),
            cancellationToken);
        var list = new MaconomyPickList(result.Records
            .Select(r => (Value: Text(r, ValueField), Title: Text(r, TitleField), Hidden: Text(r, HiddenField)))
            .Where(i => i.Value.Length > 0 && !string.Equals(i.Value, Nil, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(i.Hidden, "true", StringComparison.OrdinalIgnoreCase))
            .Select(i => new MaconomyPickListItem(i.Value, i.Title.Length > 0 ? i.Title : i.Value))
            .ToList());
        if (_options.PickListCacheSeconds > 0)
        {
            _cache.Set(key, list, TimeSpan.FromSeconds(_options.PickListCacheSeconds));
        }
        return list;
    }

    public async Task<MaconomyPickList> GetJobTemplatesAsync(MaconomyConnection connection, CancellationToken cancellationToken = default)
    {
        var key = $"maconomy:jobtemplates:{connection.TenantId:N}";
        if (_cache.TryGetValue(key, out MaconomyPickList? cached) && cached is not null)
        {
            return cached;
        }
        // Template jobs are the jobs marked as one; `template = true` is the instance's own spelling (11 Nov 2026).
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, TemplateContainer,
                new MaconomyFilterRequest("template = true", new[] { TemplateNumberField, TemplateNameField }, RowsMax), ct),
            cancellationToken);
        var list = new MaconomyPickList(result.Records
            .Select(r => (Number: Text(r, TemplateNumberField), Name: Text(r, TemplateNameField)))
            .Where(t => t.Number.Length > 0)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new MaconomyPickListItem(t.Number, t.Name.Length > 0 ? t.Name : t.Number))
            .ToList());
        if (_options.PickListCacheSeconds > 0)
        {
            _cache.Set(key, list, TimeSpan.FromSeconds(_options.PickListCacheSeconds));
        }
        return list;
    }

    private static string Text(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
}
