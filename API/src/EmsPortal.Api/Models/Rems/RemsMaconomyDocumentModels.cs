using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// Where uploading one of a request's files to Maconomy has got to, carried on the file wherever a
/// request hands it out. The status words are the syncs' (<see cref="RemsMaconomySyncStatuses"/>).
/// </summary>
public sealed record RemsMaconomyDocumentView(
    string Status,
    /// <summary>Maconomy's Document Archive No. the file sits in: "Customer 900004", "Job 10234".</summary>
    string? ArchiveNumber,
    /// <summary>The name the document carries in that archive.</summary>
    string? DocumentName,
    /// <summary>Maconomy's own key for the document, where it gave one.</summary>
    string? DocumentKey,
    DateTime? SyncedOnUtc,
    string? FailureReason);

/// <summary>A request's files in one line, for the Job tab's table: how many are in Maconomy, and whether more can go now.</summary>
public sealed record RemsMaconomyDocumentsView(
    int Total,
    int Uploaded,
    int Failed,
    int Waiting,
    bool CanUploadNow,
    /// <summary>What holds the outstanding files back when none can go; null when one can, or none is outstanding.</summary>
    string? BlockedReason);

public static class RemsMaconomyDocumentViews
{
    public const string StaleReason = "The portal lost track of this upload. Upload the documents again.";

    // A static mapper cannot read the configured minutes; ten is that setting's own default.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The view for a file, or null while there is nothing to say: a file never tried, on a request that
    /// is not approved, cannot be in Maconomy and gets no tag.
    /// </summary>
    public static RemsMaconomyDocumentView? Of(RemsFile? file, RemsRequest request)
    {
        if (file is null)
        {
            return null;
        }
        if (file.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.NotSynced
            && !string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return null;
        }
        var stale = file.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Syncing
            && file.UpdatedOnUtc < DateTime.UtcNow - StaleAfter;
        var status = stale
            ? RemsMaconomySyncStatuses.Failed
            : file.MaconomyDocumentSyncStatus switch
            {
                RemsMaconomyDocumentSyncStatus.Syncing => RemsMaconomySyncStatuses.Syncing,
                RemsMaconomyDocumentSyncStatus.Synced => RemsMaconomySyncStatuses.Synced,
                RemsMaconomyDocumentSyncStatus.Failed => RemsMaconomySyncStatuses.Failed,
                _ => RemsMaconomySyncStatuses.NotSynced,
            };
        return new RemsMaconomyDocumentView(
            status, file.MaconomyDocumentArchiveNumber, file.MaconomyDocumentName, file.MaconomyDocumentKey,
            file.MaconomyDocumentSyncedOnUtc, stale ? StaleReason : file.MaconomyDocumentSyncError);
    }

    public static RemsMaconomyDocumentsView Of(MaconomyDocumentSummary summary)
        => new(summary.Total, summary.Uploaded, summary.Failed, summary.Waiting, summary.CanUploadNow, summary.BlockedReason);

    public static string MessageFor(RemsMaconomyDocumentsView view)
        => view.Failed > 0
            ? $"{view.Uploaded} of {view.Total} document(s) are in Maconomy; {view.Failed} failed."
            : $"{view.Uploaded} of {view.Total} document(s) are in Maconomy.";
}
