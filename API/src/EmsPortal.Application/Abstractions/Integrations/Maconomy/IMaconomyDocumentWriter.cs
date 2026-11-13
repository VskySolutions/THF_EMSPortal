using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Files a document under a Maconomy record and says where it landed. The mapping
/// (<c>MaconomyDocumentMapping</c>) names the containers, fields and actions; this carries them out
/// through the session manager, so the token takes care of itself.
/// </summary>
public interface IMaconomyDocumentWriter
{
    /// <summary>Under a customer: the file is dropped, then the customer card's Attach Document action takes it.</summary>
    Task<MaconomyStoredDocument> AttachToCustomerAsync(
        MaconomyConnection connection, string customerNumber, MaconomyFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Under a job: the job's archive is made when it has none and the job pointed at it, then the dropped
    /// file is added to the archive as a document.
    /// </summary>
    Task<MaconomyStoredDocument> AttachToJobAsync(
        MaconomyConnection connection, string jobNumber, MaconomyFile file, CancellationToken cancellationToken = default);
}
