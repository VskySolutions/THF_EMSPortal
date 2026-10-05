using System.Collections.Concurrent;
using System.Collections.Immutable;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyClientDirectory"/>. Per tenant it holds one immutable snapshot, swapped
/// whole, so a reader never sees half of a refresh.
/// <para>
/// Every read of Maconomy runs in a scope of its own. A login saves the new token through that scope's
/// unit of work, and done in the caller's it would flush whatever the caller had only half staged.
/// </para>
/// </summary>
public sealed class MaconomyClientDirectory : IMaconomyClientDirectory
{
    private const int SearchMinLength = 2;
    private const int MinimumRetrySeconds = 5;
    private const int FallbackSearchLimit = 25;

    private readonly ConcurrentDictionary<Guid, TenantState> _tenants = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomyClientDirectory> _logger;

    public MaconomyClientDirectory(
        IServiceScopeFactory scopes, IOptions<MaconomyOptions> options, ILogger<MaconomyClientDirectory> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    private TimeSpan Lifetime => TimeSpan.FromSeconds(Math.Max(1, _options.CustomerCacheSeconds));

    public async Task EnsureLoadedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        var state = _tenants.GetOrAdd(tenantId, _ => new TenantState());
        if (IsReady(state, out var stale) && !stale)
        {
            return;
        }
        // Maconomy failed a moment ago: what is held stays in use until the pause is over.
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref state.RetryAfterTicks))
        {
            return;
        }
        if (IsReady(state, out _))
        {
            // Old, but whole: answer from it now and refresh behind the request.
            RefreshInBackground(tenantId, state);
            return;
        }

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have done the work while this one waited.
            if (IsReady(state, out _) || DateTime.UtcNow.Ticks < Interlocked.Read(ref state.RetryAfterTicks))
            {
                return;
            }
            await LoadAsync(tenantId, state, onlyUnknown: state.Current.Loaded, cancellationToken);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task HydrateAsync(Person? client, CancellationToken cancellationToken = default)
    {
        if (client is not { IsMaconomyClient: true, TenantId: { } tenantId })
        {
            return;
        }

        await EnsureLoadedAsync(tenantId, cancellationToken);
        var found = Find(tenantId, client.MaconomyCustomerNumber);
        if (found is null && _tenants.TryGetValue(tenantId, out var state) && !state.Unknown.IsEmpty)
        {
            // Linked since the last load: the miss was recorded just now, so one more pass fetches it.
            await EnsureLoadedAsync(tenantId, cancellationToken);
            found = Find(tenantId, client.MaconomyCustomerNumber);
        }
        client.Maconomy = found;
    }

    public MaconomyClientDetails? Find(Guid? tenantId, string? number)
    {
        if (tenantId is not { } tenant || string.IsNullOrWhiteSpace(number)
            || !_tenants.TryGetValue(tenant, out var state))
        {
            return null;
        }

        var key = number.Trim();
        var snapshot = state.Current;
        if (snapshot.Customers.TryGetValue(key, out var customer))
        {
            return customer;
        }
        // Never asked for: remembered, so the next EnsureLoadedAsync fetches it.
        if (snapshot.Loaded && !snapshot.Asked.Contains(key))
        {
            state.Unknown.TryAdd(key, 0);
        }
        return null;
    }

    public async Task<MaconomyClientDetails?> ReadAsync(
        Guid tenantId, string number, CancellationToken cancellationToken = default)
    {
        var key = number.Trim();
        IReadOnlyDictionary<string, MaconomyClientDetails> found;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, string.Empty);
            found = await scope.ServiceProvider.GetRequiredService<IMaconomyCustomerService>()
                .GetByNumbersAsync(tenantId, new[] { key }, cancellationToken);
        }

        var state = _tenants.GetOrAdd(tenantId, _ => new TenantState());
        found.TryGetValue(key, out var customer);
        lock (state)
        {
            var current = state.Current;
            state.Current = customer is null
                ? current with { Customers = current.Customers.Remove(key), Asked = current.Asked.Add(key) }
                : current with
                {
                    Customers = current.Customers.SetItem(customer.Number, customer),
                    Asked = current.Asked.Add(customer.Number),
                };
        }
        state.Unknown.TryRemove(key, out _);
        return customer;
    }

    public bool IsFresh(Guid tenantId)
        => _tenants.TryGetValue(tenantId, out var state)
            && state.Current.Loaded
            && DateTime.UtcNow - state.Current.LoadedOnUtc < Lifetime;

    public IReadOnlyList<string> MatchByName(Guid tenantId, string term)
        => Match(tenantId, term, (c, t) =>
            MaconomyCustomerService.NameMatches(c.Name, t) || MaconomyCustomerService.Contains(c.Number, t));

    public IReadOnlyList<string> MatchByContact(Guid tenantId, string term)
        => Match(tenantId, term, (c, t) =>
            MaconomyCustomerService.Contains(c.Email, t) || MaconomyCustomerService.Contains(c.Phone, t));

    public async Task<MaconomyClientSearch> SearchAsync(
        Guid tenantId, string? term, CancellationToken cancellationToken = default)
    {
        var text = MaconomyCustomerService.Sanitize(term);
        if (text.Length < SearchMinLength)
        {
            return new MaconomyClientSearch(Array.Empty<MaconomyClientDetails>(), More: false);
        }

        if (_options.PreloadAllCustomers)
        {
            await EnsureLoadedAsync(tenantId, cancellationToken);
            if (_tenants.TryGetValue(tenantId, out var state) && state.Current is { Complete: true } snapshot)
            {
                var matches = snapshot.Customers.Values
                    .Where(c => MaconomyCustomerService.Matches(c, text))
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(snapshot.SearchLimit + 1)
                    .ToList();
                return new MaconomyClientSearch(
                    matches.Take(snapshot.SearchLimit).ToList(), More: matches.Count > snapshot.SearchLimit);
            }
        }

        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, string.Empty);
        var live = await scope.ServiceProvider.GetRequiredService<IMaconomyCustomerService>()
            .SearchAsync(tenantId, text, limit: null, cancellationToken);
        return new MaconomyClientSearch(
            live.Customers
                .Select(o => new MaconomyClientDetails(o.Value, o.Name, o.EntityType, o.EmailAddress, o.PhoneNumber))
                .ToList(),
            More: !live.Complete);
    }

    public void Forget(Guid tenantId) => _tenants.TryRemove(tenantId, out _);

    private IReadOnlyList<string> Match(Guid tenantId, string term, Func<MaconomyClientDetails, string, bool> matches)
    {
        var text = term.Trim();
        if (text.Length == 0 || !_tenants.TryGetValue(tenantId, out var state))
        {
            return Array.Empty<string>();
        }
        return state.Current.Customers.Values.Where(c => matches(c, text)).Select(c => c.Number).ToList();
    }

    /// <summary>Whether there is a loaded snapshot with nothing left to fetch, and whether it has outlived the cache lifetime.</summary>
    private bool IsReady(TenantState state, out bool stale)
    {
        var snapshot = state.Current;
        stale = DateTime.UtcNow - snapshot.LoadedOnUtc >= Lifetime;
        return snapshot.Loaded && state.Unknown.IsEmpty;
    }

    private void RefreshInBackground(Guid tenantId, TenantState state)
    {
        if (Interlocked.CompareExchange(ref state.Refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await state.Gate.WaitAsync();
                try
                {
                    await LoadAsync(tenantId, state, onlyUnknown: false, CancellationToken.None);
                }
                finally
                {
                    state.Gate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refreshing tenant {TenantId}'s Maconomy clients failed.", tenantId);
            }
            finally
            {
                Interlocked.Exchange(ref state.Refreshing, 0);
            }
        });
    }

    /// <summary>
    /// Reads the tenant's customers from Maconomy and swaps the snapshot: all of them, or just the ones
    /// met since the last load. The caller holds the tenant's gate.
    /// </summary>
    private async Task LoadAsync(Guid tenantId, TenantState state, bool onlyUnknown, CancellationToken cancellationToken)
    {
        var unknown = state.Unknown.Keys.ToList();
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ITenantContext>().Set(tenantId, string.Empty);

            if (onlyUnknown)
            {
                var met = await services.GetRequiredService<IMaconomyCustomerService>()
                    .GetByNumbersAsync(tenantId, unknown, cancellationToken);
                lock (state)
                {
                    state.Current = state.Current with
                    {
                        Customers = state.Current.Customers.SetItems(met),
                        Asked = state.Current.Asked.Union(unknown),
                    };
                }
            }
            else
            {
                var snapshot = await ReadSnapshotAsync(services, tenantId, unknown, cancellationToken);
                lock (state)
                {
                    state.Current = snapshot;
                }
            }
            Forget(state, unknown);
        }
        catch (MaconomyException ex) when (ex.Failure == MaconomyFailure.NotConfigured)
        {
            // Nothing to ask. Counted as loaded so the question is not repeated on every request.
            lock (state)
            {
                state.Current = state.Current with
                {
                    Asked = state.Current.Asked.Union(unknown), LoadedOnUtc = DateTime.UtcNow,
                };
            }
            Forget(state, unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // What was held stays in use, and Maconomy is not asked again until the pause is over.
            var pause = Math.Max(MinimumRetrySeconds, _options.FailureCooldownSeconds);
            Interlocked.Exchange(ref state.RetryAfterTicks, DateTime.UtcNow.AddSeconds(pause).Ticks);
            _logger.LogWarning(
                "Tenant {TenantId}'s Maconomy clients could not be read ({Message}); retrying in {Seconds}s.",
                tenantId, ex.Message, pause);
        }
    }

    private async Task<Snapshot> ReadSnapshotAsync(
        IServiceProvider services, Guid tenantId, IReadOnlyCollection<string> unknown, CancellationToken cancellationToken)
    {
        var customerService = services.GetRequiredService<IMaconomyCustomerService>();
        var linked = await services.GetRequiredService<IPersonRepository>()
            .ListMaconomyCustomerNumbersAsync(tenantId, cancellationToken);
        var wanted = linked.Concat(unknown).ToList();

        var customers = ImmutableDictionary.CreateBuilder<string, MaconomyClientDetails>(StringComparer.OrdinalIgnoreCase);
        var complete = false;
        var searchLimit = FallbackSearchLimit;
        if (_options.PreloadAllCustomers)
        {
            var (all, whole) = await customerService.ListAllAsync(tenantId, cancellationToken);
            foreach (var customer in all)
            {
                customers[customer.Number] = customer;
            }
            complete = whole;
            if (!whole)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} has more Maconomy customers than Maconomy:PreloadMaxRows; the client search asks Maconomy instead.",
                    tenantId);
            }
            var connection = await services.GetRequiredService<IMaconomyConnectionRepository>()
                .GetByTenantAsync(tenantId, cancellationToken);
            searchLimit = Math.Clamp(connection?.DefaultLimit ?? FallbackSearchLimit, 1, Math.Max(1, _options.MaxLimit));
        }

        // The portal's own clients are read by number unless the one read above already brought them.
        var missing = wanted.Where(number => !customers.ContainsKey(number)).ToList();
        if (missing.Count > 0 && !complete)
        {
            foreach (var (number, customer) in await customerService.GetByNumbersAsync(tenantId, missing, cancellationToken))
            {
                customers[number] = customer;
            }
        }

        return new Snapshot(
            customers.ToImmutable(),
            wanted.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            complete,
            searchLimit,
            DateTime.UtcNow);
    }

    private static void Forget(TenantState state, IEnumerable<string> numbers)
    {
        foreach (var number in numbers)
        {
            state.Unknown.TryRemove(number, out _);
        }
    }

    private sealed class TenantState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly ConcurrentDictionary<string, byte> Unknown = new(StringComparer.OrdinalIgnoreCase);
        public volatile Snapshot Current = Snapshot.Empty;
        public long RetryAfterTicks;
        public int Refreshing;
    }

    /// <param name="Customers">By customer number.</param>
    /// <param name="Asked">Every number Maconomy was asked for, found or not — one outside it has never been looked up.</param>
    /// <param name="Complete">Holds every customer in Maconomy, not just the tenant's clients.</param>
    /// <param name="SearchLimit">Rows a search answered from memory returns.</param>
    /// <param name="LoadedOnUtc">When the tenant's clients were last read in full; <see cref="DateTime.MinValue"/> until they have been.</param>
    private sealed record Snapshot(
        ImmutableDictionary<string, MaconomyClientDetails> Customers,
        ImmutableHashSet<string> Asked,
        bool Complete,
        int SearchLimit,
        DateTime LoadedOnUtc)
    {
        public static readonly Snapshot Empty = new(
            ImmutableDictionary.Create<string, MaconomyClientDetails>(StringComparer.OrdinalIgnoreCase),
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            Complete: false,
            FallbackSearchLimit,
            DateTime.MinValue);

        public bool Loaded => LoadedOnUtc != DateTime.MinValue;
    }
}
