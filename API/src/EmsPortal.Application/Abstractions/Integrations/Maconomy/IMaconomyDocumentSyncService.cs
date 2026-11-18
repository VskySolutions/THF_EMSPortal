using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Uploads an approved request's files to Maconomy: its attachments under the client's customer, its
/// signed client acceptance form and purchase order under the engagement's job. It follows the two
/// syncs — queued once the customer is there and again once the job is — and can be pressed by hand for
/// what failed. Each file carries its own state (<see cref="RemsFile.MaconomyDocumentSyncStatus"/> and
/// where Maconomy filed it), and a failed upload never touches the customer or the job, which exist by then.
/// </summary>
public interface IMaconomyDocumentSyncService
{
    /// <summary>What every file needs to answer "may this go now": the connection and the two switches.</summary>
    Task<MaconomyDocumentSyncContext> GetContextAsync(Guid tenantId, CancellationToken cancellationToken = default);

    void Enqueue(Guid remsId, Guid tenantId);

    /// <summary>The background attempt: every file not yet in Maconomy whose customer or job is. A failed upload is recorded on the file.</summary>
    Task<RemsRequest?> RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The same, pressed by the person named. Throws <see cref="MaconomyDocumentSyncException"/> when there is nothing it could upload.</summary>
    Task<RemsRequest> UploadNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>Whether a file has sat as uploading longer than an upload can take.</summary>
    bool IsStale(RemsFile file);
}

/// <summary>The background attempt's queue: what the customer sync, the job sync and the approval enqueue.</summary>
public interface IMaconomyDocumentSyncQueue
{
    void Enqueue(Guid remsId, Guid tenantId);
}

/// <summary>
/// The tenant's document upload as the rules read it. Customer documents follow the customer sync's
/// switch, job documents the job sync's.
/// </summary>
public sealed record MaconomyDocumentSyncContext(bool Connected, bool CustomerEnabled, bool JobEnabled)
{
    public static readonly MaconomyDocumentSyncContext None = new(false, false, false);
}

/// <summary>How a request's files stand with Maconomy, and whether anything can be uploaded now.</summary>
public sealed record MaconomyDocumentSummary(int Total, int Uploaded, int Failed, int Waiting, bool CanUploadNow, string? BlockedReason);

/// <summary>A rule of the document upload broken by the caller. The refusal kinds are the customer sync's.</summary>
public sealed class MaconomyDocumentSyncException : Exception
{
    public MaconomyDocumentSyncException(MaconomyCustomerSyncRefusal refusal, string message) : base(message)
        => Refusal = refusal;

    public MaconomyCustomerSyncRefusal Refusal { get; }
}
