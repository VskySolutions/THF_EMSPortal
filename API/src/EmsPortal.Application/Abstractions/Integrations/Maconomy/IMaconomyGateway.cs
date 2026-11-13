namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The calls the portal makes to a Maconomy instance, as typed methods. Knows nothing about tenants or
/// where credentials live: it is handed an endpoint and whatever the call needs.
/// </summary>
public interface IMaconomyGateway
{
    /// <summary>
    /// <c>GET /auth/{instance}/login</c> with Basic auth, asking for a reconnect token. Returns the
    /// <c>Maconomy-Reconnect</c> response header. Throws <see cref="MaconomyException"/> with
    /// <see cref="MaconomyFailure.AuthFailed"/> when the credentials are refused.
    /// </summary>
    Task<string> LoginAsync(MaconomyEndpoint endpoint, string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /containers/{instance}/{container}/filter</c> with a reconnect token. Throws
    /// <see cref="MaconomyException"/> with <see cref="MaconomyFailure.TokenRejected"/> when the token is
    /// refused, so the caller can log in and try once more.
    /// </summary>
    Task<MaconomyFilterResult> FilterAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        string container,
        MaconomyFilterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a record: opens a container instance, follows its init-create link for a template, posts
    /// the values to the template's create link, each step carrying <c>Maconomy-Concurrency-Control</c>.
    /// Throws <see cref="MaconomyException"/> with <see cref="MaconomyFailure.Rejected"/> when Maconomy
    /// refuses the record, with its own message.
    /// </summary>
    Task<MaconomyCreatedRecord> CreateAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyCardWrite create,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a file's bytes to the instance's file drop (<c>POST /filedrop/{instance}/create</c>) and
    /// returns the drop's URI, which the action that consumes the file is then pointed at.
    /// </summary>
    Task<string> DropFileAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyFile file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an action on an existing record: opens a container instance, loads the record by its key and
    /// posts the action, naming the dropped file in <c>Maconomy-File-Callback</c>. Returns the record as
    /// it stands afterwards.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> RunCardActionAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyCardAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Writes values onto an existing record's card and returns the record as it stands afterwards.</summary>
    Task<IReadOnlyDictionary<string, string?>> UpdateCardAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyCardUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a dropped file to a document archive as a new line, creating the archive when there is none.
    /// Returns every line of the archive afterwards, for the caller to find its own by name.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> AddArchiveDocumentAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyArchiveDocumentAdd add,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Adds rows to the table under an existing card, skipping any that already stand there. Returns how
    /// many were added. Throws <see cref="MaconomyFailure.Rejected"/> when Maconomy holds no such card.
    /// </summary>
    Task<int> AddTableRowsAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyTableRowsAdd add,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets values on the matching row of the table under an existing card. Returns the row as Maconomy
    /// holds it afterwards, or null, with nothing written, when no row matches. Throws
    /// <see cref="MaconomyFailure.Rejected"/> when there is no such card.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>?> SetTableRowAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyTableRowSet set,
        CancellationToken cancellationToken = default);
}
