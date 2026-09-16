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
