using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using Microsoft.Extensions.Logging;

namespace EmsPortal.Infrastructure.Integrations.Maconomy;

/// <summary>
/// Talks to a Maconomy REST instance over the named <see cref="HttpClientName"/> client. Every URL is
/// built absolute per call, because the base address differs per tenant. Logs the status of what
/// Maconomy answered, never a credential, a token or the Basic header.
/// </summary>
internal sealed class MaconomyGateway : IMaconomyGateway
{
    /// <summary>Named HttpClient (registered in DependencyInjection, with the configured timeout).</summary>
    public const string HttpClientName = "Maconomy";
    /// <summary>The same client with the longer timeout a file upload needs.</summary>
    public const string UploadHttpClientName = "MaconomyUpload";
    private const string ReconnectHeader = "Maconomy-Reconnect";
    private const string ConcurrencyHeader = "Maconomy-Concurrency-Control";
    private const string FileCallbackHeader = "Maconomy-File-Callback";
    private const string FileDropAccept = "application/vnd.deltek.maconomy.filedrop+json; charset=utf-8; version=3.0";
    private const string AuthenticationAccept = "application/vnd.deltek.maconomy.authentication+json; charset=utf-8; version=3.0";
    private const string ContainersAccept = "application/vnd.deltek.maconomy.containers+json; charset=utf-8; version=9.0";
    private const string ContainersContentType = "application/vnd.deltek.maconomy.containers+json; charset=UTF-8; version=9.0";

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MaconomyGateway> _logger;

    public MaconomyGateway(IHttpClientFactory httpClientFactory, ILogger<MaconomyGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> LoginAsync(MaconomyEndpoint endpoint, string userName, string password, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root(endpoint)}/auth/{Segment(endpoint.InstanceCode)}/login");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
        request.Headers.TryAddWithoutValidation("Accept", AuthenticationAccept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");

        // Asks for a reconnect token in the reply rather than a session cookie.
        request.Headers.TryAddWithoutValidation("Maconomy-Authentication", "X-Reconnect");

        using var response = await SendAsync(request, "login", cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Maconomy login at {BaseUrl} refused user {UserName} with {StatusCode}.", endpoint.BaseUrl, userName, (int)response.StatusCode);
            throw new MaconomyException(MaconomyFailure.AuthFailed, $"Maconomy refused the login for '{userName}' ({(int)response.StatusCode}). Check the user name, the password and the instance code.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await UnexpectedAsync(response, "login", cancellationToken);
        }
        if (!response.Headers.TryGetValues(ReconnectHeader, out var values) || values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) is not { } token)
        {
            throw new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy accepted the login but sent no {ReconnectHeader} header. Is X-Reconnect authentication enabled on the instance?");
        }
        return token.Trim();
    }

    public async Task<MaconomyFilterResult> FilterAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        string container,
        MaconomyFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root(endpoint)}/containers/{Segment(endpoint.InstanceCode)}/{Segment(container)}/filter");
        request.Headers.Authorization = new AuthenticationHeaderValue("X-Reconnect", reconnectToken);
        request.Headers.TryAddWithoutValidation("Accept", ContainersAccept);

        var body = JsonSerializer.Serialize(new { restriction = filter.Restriction, fields = filter.Fields, limit = filter.Limit }, BodyOptions);
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContainersContentType);

        using var response = await SendAsync(request, $"{container} filter", cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MaconomyException(MaconomyFailure.TokenRejected, $"Maconomy refused the reconnect token ({(int)response.StatusCode}).");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await UnexpectedAsync(response, $"{container} filter", cancellationToken);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return ParseFilterResult(text);
        }
        catch (JsonException ex)
        {
            throw new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy's reply to the {container} filter was not JSON.", ex);
        }
    }

    /// <summary>
    /// Reads <c>panes.filter.records[].data</c> as one dictionary per record, every value as text, plus
    /// the pane's <c>rowCount</c>. Anything missing reads as no records rather than as an error.
    /// </summary>
    private static MaconomyFilterResult ParseFilterResult(string text)
    {
        using var json = JsonDocument.Parse(text);
        var records = new List<IReadOnlyDictionary<string, string?>>();
        var rowCount = 0;
        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("panes", out var panes) && panes.TryGetProperty("filter", out var pane))
        {
            if (pane.TryGetProperty("meta", out var meta) && meta.TryGetProperty("rowCount", out var count) && count.TryGetInt32(out var n))
            {
                rowCount = n;
            }
            if (pane.TryGetProperty("records", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in list.EnumerateArray())
                {
                    if (record.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        records.Add(data.EnumerateObject().ToDictionary(p => p.Name, p => ValueText(p.Value), StringComparer.OrdinalIgnoreCase));
                    }
                }
            }
        }
        return new MaconomyFilterResult(records, rowCount > 0 ? rowCount : records.Count);
    }

    private static string? ValueText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    public async Task<MaconomyCreatedRecord> CreateAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyCardWrite create,
        CancellationToken cancellationToken = default)
    {
        // The sequence THF's Maconomy team gave (22 Oct 2026), spelt out rather than discovered from links:
        //   1. POST .../{container}/instances, naming the card fields          → containerInstanceId
        //   2. POST .../instances/{id}/data/panes/card/init with {}            → a blank template
        //   3. POST .../instances/{id}/data/panes/card with {"data": {...}}    → the created record
        // Each reply's Maconomy-Concurrency-Control value goes on the next request. "init-create" is a
        // different action — it validated and refused an empty record — and is not used.
        var what = $"{create.Container} create";
        var instancesUrl = $"{Root(endpoint)}/containers/{Segment(endpoint.InstanceCode)}/{ContainerSegment(create.Container)}/instances";
        string? instanceId = null;
        try
        {
            var (instance, concurrency) = await PostContainersAsync(
                reconnectToken, instancesUrl, new { panes = new { card = new { fields = create.Fields } } }, null,
                $"{create.Container} instance", cancellationToken);
            using (instance)
            {
                instanceId = Text(instance.RootElement, "meta", "containerInstanceId");
            }
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new MaconomyException(
                    MaconomyFailure.Unavailable, $"Maconomy opened a {create.Container} instance but its reply named no containerInstanceId.");
            }

            var paneUrl = $"{instancesUrl}/{Segment(instanceId)}/data/panes/card";
            // A template reference goes on the init where the caller says so, so the record Maconomy hands
            // back already carries the template's defaults; otherwise init is bare.
            object initBody = create.InitValues is { Count: > 0 } ? new { data = create.InitValues } : new { };
            var (template, templateConcurrency) = await PostContainersAsync(
                reconnectToken, $"{paneUrl}/init", initBody, concurrency, $"{create.Container} init", cancellationToken);
            template.Dispose();

            var (created, _) = await PostContainersAsync(
                reconnectToken, paneUrl, new { data = TypedValues(create) }, templateConcurrency, what, cancellationToken);
            using (created)
            {
                var data = FindRecordData(created.RootElement, create.Fields)
                    ?? throw new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy accepted the {what} but its reply held no record.");
                return new MaconomyCreatedRecord(data);
            }
        }
        finally
        {
            if (instanceId is { Length: > 0 })
            {
                await DeleteInstanceQuietlyAsync(reconnectToken, $"{instancesUrl}/{Segment(instanceId)}", cancellationToken);
            }
        }
    }

    public async Task<string> DropFileAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyFile file, CancellationToken cancellationToken = default)
    {
        // File Drop 3.0: the instance's /filedrop root advertises this as its filedrop:create link (12 Nov 2026).
        const string what = "file drop";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root(endpoint)}/filedrop/{Segment(endpoint.InstanceCode)}/create");
        request.Headers.Authorization = new AuthenticationHeaderValue("X-Reconnect", reconnectToken);
        request.Headers.TryAddWithoutValidation("Accept", FileDropAccept);
        request.Content = new ByteArrayContent(file.Content);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type)
            ? type
            : new MediaTypeHeaderValue("application/octet-stream");
        // The name Maconomy files the document under. How the drop takes it is not yet proven against the instance.
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"\"{HeaderSafe(file.FileName)}\"" };

        using var response = await SendAsync(request, what, cancellationToken, UploadHttpClientName);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MaconomyException(MaconomyFailure.TokenRejected, $"Maconomy refused the reconnect token ({(int)response.StatusCode}).");
        }
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            var said = ErrorMessageOf(text);
            _logger.LogWarning("Maconomy refused the {What} with {StatusCode}: {Message}", what, (int)response.StatusCode, said ?? "(no message)");
            throw new MaconomyException(
                MaconomyFailure.Rejected, $"Maconomy refused the file{(said is null ? $" ({(int)response.StatusCode})." : $": {said}")}");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await UnexpectedAsync(response, what, cancellationToken);
        }
        var location = response.Headers.Location?.ToString() ?? FileDropLinkOf(text);
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new MaconomyException(MaconomyFailure.Unavailable, "Maconomy took the file but its reply named no file drop to attach from.");
        }
        return Uri.TryCreate(new Uri(Root(endpoint) + "/"), location, out var absolute) ? absolute.ToString() : location;
    }

    public async Task<IReadOnlyDictionary<string, string?>> RunCardActionAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyCardAction action, CancellationToken cancellationToken = default)
    {
        // The route the instance's own links spell out (12 Nov 2026): open an instance, load the record
        // by key, then POST .../data/panes/card/0/action;name={action}.
        var what = $"{action.Container} {action.Action}";
        var instancesUrl = InstancesUrl(endpoint, action.Container);
        string? instanceId = null;
        try
        {
            string? concurrency;
            (instanceId, concurrency) = await OpenInstanceAsync(
                reconnectToken, instancesUrl, new { card = new { fields = action.Fields } }, action.Container, cancellationToken);
            var instanceUrl = $"{instancesUrl}/{Segment(instanceId)}";
            var (record, loadedConcurrency) = await LoadCardAsync(
                reconnectToken, instanceUrl, action.Container, action.KeyField, action.KeyValue, concurrency, cancellationToken);
            if (record is null)
            {
                throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no {action.Container} record with {action.KeyField} {action.KeyValue}.");
            }
            var (done, _) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data/panes/card/0/action;name={Segment(action.Action)}", null, loadedConcurrency, what,
                cancellationToken, action.FileDropUri);
            using (done)
            {
                // An action that answers with no record has still run; what was loaded is the best there is.
                return FindRecordData(done.RootElement, action.Fields) ?? record;
            }
        }
        finally
        {
            await ReleaseAsync(reconnectToken, instancesUrl, instanceId, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, string?>> UpdateCardAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyCardUpdate update, CancellationToken cancellationToken = default)
    {
        var what = $"{update.Container} update";
        var instancesUrl = InstancesUrl(endpoint, update.Container);
        string? instanceId = null;
        try
        {
            string? concurrency;
            (instanceId, concurrency) = await OpenInstanceAsync(
                reconnectToken, instancesUrl, new { card = new { fields = update.Fields } }, update.Container, cancellationToken);
            var instanceUrl = $"{instancesUrl}/{Segment(instanceId)}";
            var (record, loadedConcurrency) = await LoadCardAsync(
                reconnectToken, instanceUrl, update.Container, update.KeyField, update.KeyValue, concurrency, cancellationToken);
            if (record is null)
            {
                throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no {update.Container} record with {update.KeyField} {update.KeyValue}.");
            }
            var (saved, _) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data/panes/card/0", new { data = Typed(update.Values, update.NumberFields) }, loadedConcurrency, what, cancellationToken);
            using (saved)
            {
                return FindRecordData(saved.RootElement, update.Fields) ?? record;
            }
        }
        finally
        {
            await ReleaseAsync(reconnectToken, instancesUrl, instanceId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> AddArchiveDocumentAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyArchiveDocumentAdd add, CancellationToken cancellationToken = default)
    {
        // The archive's card and its table of documents in one instance: the archive is loaded, or made
        // when there is none, then "Add Document" on the table takes the dropped file. Built from the
        // instance's links; not yet run against it (12 Nov 2026).
        var instancesUrl = InstancesUrl(endpoint, add.Container);
        string? instanceId = null;
        try
        {
            string? concurrency;
            (instanceId, concurrency) = await OpenInstanceAsync(
                reconnectToken, instancesUrl,
                new { card = new { fields = add.ArchiveFields }, table = new { fields = add.LineFields } },
                add.Container, cancellationToken);
            var instanceUrl = $"{instancesUrl}/{Segment(instanceId)}";
            var (loaded, loadedConcurrency) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data;{Segment(add.ArchiveNumberField)}={Segment(add.ArchiveNumber)}", null, concurrency,
                $"{add.Container} read", cancellationToken);
            concurrency = loadedConcurrency;
            IReadOnlyList<IReadOnlyDictionary<string, string?>> lines;
            bool exists;
            using (loaded)
            {
                exists = PaneRecords(loaded.RootElement, "card").Count > 0;
                lines = PaneRecords(loaded.RootElement, "table");
            }
            if (!exists)
            {
                var (blank, blankConcurrency) = await PostContainersAsync(
                    reconnectToken, $"{instanceUrl}/data/panes/card/init", new { }, concurrency, $"{add.Container} init", cancellationToken);
                blank.Dispose();
                var (made, madeConcurrency) = await PostContainersAsync(
                    reconnectToken, $"{instanceUrl}/data/panes/card", new { data = add.NewArchiveValues }, blankConcurrency,
                    $"{add.Container} create", cancellationToken);
                made.Dispose();
                concurrency = madeConcurrency;
            }
            if (string.IsNullOrEmpty(add.FileDropUri))
            {
                return lines;
            }
            var (line, lineConcurrency) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data/panes/table/init", new { }, concurrency, $"{add.Container} document init", cancellationToken);
            line.Dispose();
            var (added, _) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data/panes/table", new { data = new { } }, lineConcurrency, $"{add.Container} add document",
                cancellationToken, add.FileDropUri);
            using (added)
            {
                return PaneRecords(added.RootElement, "table");
            }
        }
        finally
        {
            await ReleaseAsync(reconnectToken, instancesUrl, instanceId, cancellationToken);
        }
    }

    public async Task<int> AddTableRowsAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyTableRowsAdd add, CancellationToken cancellationToken = default)
    {
        // The card and its table in one instance, as the instance's own links spell it (13 Nov 2026):
        // load the card by key, then per row POST .../panes/table/init and POST .../panes/table.
        var instancesUrl = InstancesUrl(endpoint, add.Container);
        string? instanceId = null;
        try
        {
            string? concurrency;
            (instanceId, concurrency) = await OpenInstanceAsync(
                reconnectToken, instancesUrl,
                new { card = new { fields = add.CardFields }, table = new { fields = add.RowFields } },
                add.Container, cancellationToken);
            var instanceUrl = $"{instancesUrl}/{Segment(instanceId)}";
            var (loaded, loadedConcurrency) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data;{Segment(add.KeyField)}={Segment(add.KeyValue)}", null, concurrency,
                $"{add.Container} read", cancellationToken);
            concurrency = loadedConcurrency;
            IReadOnlyList<IReadOnlyDictionary<string, string?>> existing;
            using (loaded)
            {
                if (PaneRecords(loaded.RootElement, "card").Count == 0)
                {
                    throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no {add.Container} record with {add.KeyField} {add.KeyValue}.");
                }
                existing = PaneRecords(loaded.RootElement, "table");
            }
            var added = 0;
            foreach (var row in add.Rows)
            {
                if (existing.Any(e => add.MatchFields.All(f => SameText(e.GetValueOrDefault(f), row.GetValueOrDefault(f)))))
                {
                    continue;
                }
                var (blank, blankConcurrency) = await PostContainersAsync(
                    reconnectToken, $"{instanceUrl}/data/panes/table/init", new { }, concurrency, $"{add.Container} row init", cancellationToken);
                blank.Dispose();
                var (made, madeConcurrency) = await PostContainersAsync(
                    reconnectToken, $"{instanceUrl}/data/panes/table", new { data = row }, blankConcurrency, $"{add.Container} row", cancellationToken);
                made.Dispose();
                concurrency = madeConcurrency;
                added++;
            }
            return added;
        }
        finally
        {
            await ReleaseAsync(reconnectToken, instancesUrl, instanceId, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, string?>?> SetTableRowAsync(
        MaconomyEndpoint endpoint, string reconnectToken, MaconomyTableRowSet set, CancellationToken cancellationToken = default)
    {
        // The card and its table in one instance: load by key, then save the matching row in place
        // (.../panes/table/{row}). Null, nothing written, when no row matches: adding a row to a
        // line-numbered table wants a row parameter, and thmasd01 took no form of it (13 Nov 2026).
        var instancesUrl = InstancesUrl(endpoint, set.Container);
        string? instanceId = null;
        try
        {
            string? concurrency;
            (instanceId, concurrency) = await OpenInstanceAsync(
                reconnectToken, instancesUrl,
                new { card = new { fields = set.CardFields }, table = new { fields = set.RowFields } },
                set.Container, cancellationToken);
            var instanceUrl = $"{instancesUrl}/{Segment(instanceId)}";
            var (loaded, loadedConcurrency) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data;{Segment(set.KeyField)}={Segment(set.KeyValue)}", null, concurrency,
                $"{set.Container} read", cancellationToken);
            concurrency = loadedConcurrency;
            IReadOnlyList<IReadOnlyDictionary<string, string?>> existing;
            using (loaded)
            {
                if (PaneRecords(loaded.RootElement, "card").Count == 0)
                {
                    throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no {set.Container} record with {set.KeyField} {set.KeyValue}.");
                }
                existing = PaneRecords(loaded.RootElement, "table");
            }
            var index = -1;
            for (var i = 0; i < existing.Count && index < 0; i++)
            {
                if (set.Match.All(m => SameText(existing[i].GetValueOrDefault(m.Key), m.Value)))
                {
                    index = i;
                }
            }
            if (index < 0)
            {
                return null;
            }
            var (saved, _) = await PostContainersAsync(
                reconnectToken, $"{instanceUrl}/data/panes/table/{index}", new { data = Typed(set.Values, set.NumberFields) }, concurrency,
                $"{set.Container} row update", cancellationToken);
            using (saved)
            {
                var rows = PaneRecords(saved.RootElement, "table");
                return rows.FirstOrDefault(r => set.Match.All(m => SameText(r.GetValueOrDefault(m.Key), m.Value))) ?? existing[index];
            }
        }
        finally
        {
            await ReleaseAsync(reconnectToken, instancesUrl, instanceId, cancellationToken);
        }
    }

    // A number field goes out as a JSON number: Maconomy refuses "2026" where it holds a whole number.
    private static Dictionary<string, object?> TypedValues(MaconomyCardWrite write) => Typed(write.Values, write.NumberFields);

    private static Dictionary<string, object?> Typed(IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? numberFields)
    {
        var typed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, value) in values)
        {
            typed[field] = numberFields is { } numbers
                && numbers.Contains(field, StringComparer.OrdinalIgnoreCase)
                && decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : value;
        }
        return typed;
    }

    private static bool SameText(string? a, string? b)
        => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static string InstancesUrl(MaconomyEndpoint endpoint, string container)
        => $"{Root(endpoint)}/containers/{Segment(endpoint.InstanceCode)}/{ContainerSegment(container)}/instances";

    /// <summary>Opens a container instance on the panes given; the id every later call of the sequence is made against.</summary>
    private async Task<(string InstanceId, string? Concurrency)> OpenInstanceAsync(
        string reconnectToken, string instancesUrl, object panes, string container, CancellationToken cancellationToken)
    {
        var (instance, concurrency) = await PostContainersAsync(
            reconnectToken, instancesUrl, new { panes }, null, $"{container} instance", cancellationToken);
        using (instance)
        {
            var id = Text(instance.RootElement, "meta", "containerInstanceId");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new MaconomyException(
                    MaconomyFailure.Unavailable, $"Maconomy opened a {container} instance but its reply named no containerInstanceId.");
            }
            return (id, concurrency);
        }
    }

    /// <summary>Loads one record into the instance by its key. Null when Maconomy holds none: it answers 204, with no body.</summary>
    private async Task<(IReadOnlyDictionary<string, string?>? Record, string? Concurrency)> LoadCardAsync(
        string reconnectToken, string instanceUrl, string container, string keyField, string keyValue,
        string? concurrency, CancellationToken cancellationToken)
    {
        var (loaded, next) = await PostContainersAsync(
            reconnectToken, $"{instanceUrl}/data;{Segment(keyField)}={Segment(keyValue)}", null, concurrency, $"{container} read", cancellationToken);
        using (loaded)
        {
            return (PaneRecords(loaded.RootElement, "card").FirstOrDefault(), next);
        }
    }

    private async Task ReleaseAsync(string reconnectToken, string instancesUrl, string? instanceId, CancellationToken cancellationToken)
    {
        if (instanceId is { Length: > 0 })
        {
            await DeleteInstanceQuietlyAsync(reconnectToken, $"{instancesUrl}/{Segment(instanceId)}", cancellationToken);
        }
    }

    /// <summary>One POST in the containers dialect: the reply parsed, with the concurrency value to send next.</summary>
    private async Task<(JsonDocument Reply, string? Concurrency)> PostContainersAsync(
        string reconnectToken, string url, object? body, string? concurrency, string what, CancellationToken cancellationToken,
        string? fileDropUri = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("X-Reconnect", reconnectToken);
        request.Headers.TryAddWithoutValidation("Accept", ContainersAccept);
        if (!string.IsNullOrEmpty(concurrency))
        {
            request.Headers.TryAddWithoutValidation(ConcurrencyHeader, concurrency);
        }
        if (!string.IsNullOrEmpty(fileDropUri))
        {
            // The file an action consumes is named by its drop, in angle brackets.
            request.Headers.TryAddWithoutValidation(FileCallbackHeader, $"<{fileDropUri}>");
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, BodyOptions), Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContainersContentType);
        }

        using var response = await SendAsync(request, what, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MaconomyException(MaconomyFailure.TokenRejected, $"Maconomy refused the reconnect token ({(int)response.StatusCode}).");
        }
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            // Maconomy understood and said no: its message is the one to show.
            var said = ErrorMessageOf(await response.Content.ReadAsStringAsync(cancellationToken));
            _logger.LogWarning("Maconomy refused the {What} with {StatusCode}: {Message}", what, (int)response.StatusCode, said ?? "(no message)");
            throw new MaconomyException(
                MaconomyFailure.Rejected,
                $"Maconomy refused the {what}{(said is null ? $" ({(int)response.StatusCode})." : $": {said}")}");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await UnexpectedAsync(response, what, cancellationToken);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument reply;
        try
        {
            reply = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException ex)
        {
            throw new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy's reply to the {what} was not JSON.", ex);
        }
        var next = response.Headers.TryGetValues(ConcurrencyHeader, out var values)
            ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? concurrency
            : concurrency;
        return (reply, next);
    }

    // A container instance left open holds a slot on Maconomy until it expires; nothing rides on the delete.
    private async Task DeleteInstanceQuietlyAsync(string reconnectToken, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("X-Reconnect", reconnectToken);
            request.Headers.TryAddWithoutValidation("Accept", ContainersAccept);
            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Maconomy container instance at {Url} was not released.", url);
        }
    }

    // A container name such as "dpl:customercard" keeps its colon: that is how Maconomy's own examples
    // spell the path, and an escaped one may not be read the same way.
    private static string ContainerSegment(string container) => Segment(container).Replace("%3A", ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The created record: <c>panes.card.records[0].data</c> where it is, else the first <c>data</c> object naming a field asked for.</summary>
    private static IReadOnlyDictionary<string, string?>? FindRecordData(JsonElement root, IReadOnlyList<string> fields)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("panes", out var panes) && panes.ValueKind == JsonValueKind.Object
            && panes.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object
            && card.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                if (record.ValueKind == JsonValueKind.Object
                    && record.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    return ToRecord(data);
                }
            }
        }
        return FindData(root, fields);
    }

    private static IReadOnlyDictionary<string, string?>? FindData(JsonElement element, IReadOnlyList<string> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    && fields.Any(f => data.TryGetProperty(f, out _)))
                {
                    return ToRecord(data);
                }
                foreach (var property in element.EnumerateObject())
                {
                    if (FindData(property.Value, fields) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FindData(item, fields) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static IReadOnlyDictionary<string, string?> ToRecord(JsonElement data)
        => data.EnumerateObject().ToDictionary(p => p.Name, p => ValueText(p.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Every record of one pane of a data reply (<c>panes.{pane}.records[].data</c>); empty when the reply has none.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> PaneRecords(JsonElement root, string pane)
    {
        var found = new List<IReadOnlyDictionary<string, string?>>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("panes", out var panes) && panes.ValueKind == JsonValueKind.Object
            && panes.TryGetProperty(pane, out var wanted) && wanted.ValueKind == JsonValueKind.Object
            && wanted.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                if (record.ValueKind == JsonValueKind.Object
                    && record.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    found.Add(ToRecord(data));
                }
            }
        }
        return found;
    }

    // A header value: ASCII, no quotes or control characters. Anything else in a file name becomes "_".
    private static string HeaderSafe(string fileName)
    {
        var name = new string(fileName.Trim().Select(c => c is < ' ' or > '~' or '"' or '\\' ? '_' : c).ToArray());
        return name.Length == 0 ? "document" : name;
    }

    /// <summary>The file drop a reply names in its body, for a drop that sends no Location header.</summary>
    private static string? FileDropLinkOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(body);
            return FirstFileDropHref(json.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstFileDropHref(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && (property.NameEquals("href") || property.NameEquals("location"))
                        && property.Value.GetString() is { } href
                        && href.Contains("/filedrop/", StringComparison.OrdinalIgnoreCase)
                        && !href.EndsWith("/create", StringComparison.OrdinalIgnoreCase))
                    {
                        return href;
                    }
                    if (FirstFileDropHref(property.Value) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FirstFileDropHref(item) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static string? Text(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, string what, CancellationToken cancellationToken, string clientName = HttpClientName)
    {
        var http = _httpClientFactory.CreateClient(clientName);
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maconomy could not be reached for the {What} at {Url}.", what, request.RequestUri);
            throw new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy could not be reached for the {what}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Maconomy did not answer the {What} at {Url} within {Seconds}s.", what, request.RequestUri, http.Timeout.TotalSeconds);
            throw new MaconomyException(
                MaconomyFailure.Unavailable, $"Maconomy did not answer the {what} within {http.Timeout.TotalSeconds:0} seconds.", ex);
        }
    }

    /// <summary>A status the call has no use for, with whatever Maconomy said about it.</summary>
    private async Task<MaconomyException> UnexpectedAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var said = ErrorMessageOf(body);
        _logger.LogWarning("Maconomy answered {StatusCode} to the {What}: {Message}", (int)response.StatusCode, what, said ?? "(no message)");
        return new MaconomyException(MaconomyFailure.Unavailable, $"Maconomy answered {(int)response.StatusCode} to the {what}{(said is null ? "." : $": {said}")}");
    }

    // Maconomy's error replies carry an errorMessage; anything else is noise for a log, not for a person.
    private static string? ErrorMessageOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("errorMessage", out var message)
                && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Root(MaconomyEndpoint endpoint) => endpoint.BaseUrl.TrimEnd('/');

    private static string Segment(string value) => Uri.EscapeDataString(value.Trim());
}
