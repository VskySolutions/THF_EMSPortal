namespace EmsPortal.Domain.Enums;

/// <summary>
/// Where uploading one of a request's files to Maconomy has got to. Stored as a string, like
/// <see cref="RemsMaconomyJobSyncStatus"/>.
/// </summary>
public enum RemsMaconomyDocumentSyncStatus
{
    /// <summary>Not uploaded: no attempt yet, or the customer or job it belongs under is not in Maconomy yet.</summary>
    NotSynced,

    /// <summary>An upload is under way. Set BEFORE the file is sent, so a crash leaves a trace rather than a second copy.</summary>
    Syncing,

    /// <summary>Maconomy holds the document; where it sits is on the row.</summary>
    Synced,

    /// <summary>The one attempt failed, with the reason on the row, until somebody uploads the documents again.</summary>
    Failed,
}
