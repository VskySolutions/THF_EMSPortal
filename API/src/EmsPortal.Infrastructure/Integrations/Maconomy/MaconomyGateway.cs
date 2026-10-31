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
    private const string ReconnectHeader = "Maconomy-Reconnect";
    private const string ConcurrencyHeader = "Maconomy-Concurrency-Control";
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
                reconnectToken, paneUrl, new { data = create.Values }, templateConcurrency, what, cancellationToken);
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

    /// <summary>One POST in the containers dialect: the reply parsed, with the concurrency value to send next.</summary>
    private async Task<(JsonDocument Reply, string? Concurrency)> PostContainersAsync(
        string reconnectToken, string url, object? body, string? concurrency, string what, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("X-Reconnect", reconnectToken);
        request.Headers.TryAddWithoutValidation("Accept", ContainersAccept);
        if (!string.IsNullOrEmpty(concurrency))
        {
            request.Headers.TryAddWithoutValidation(ConcurrencyHeader, concurrency);
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

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
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
