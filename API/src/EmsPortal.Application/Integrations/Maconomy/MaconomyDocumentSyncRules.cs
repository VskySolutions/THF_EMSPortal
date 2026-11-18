using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Which of a request's files may go to Maconomy, as a pure function of the request and the tenant's
/// context — so the lists, the request page and the upload itself give one answer. WHERE a file goes is
/// <see cref="MaconomyDocumentMapping"/>'s business.
/// </summary>
public static class MaconomyDocumentSyncRules
{
    /// <summary>Why the file cannot be uploaded now, or null when it can. Its own sync state is not looked at.</summary>
    public static string? WhyNotUploadable(RemsRequest request, RemsFile file, MaconomyDocumentSyncContext context)
    {
        if (!context.Connected)
        {
            return MaconomyJobSyncRules.NotConnectedReason;
        }
        if (!string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return "The request is not approved yet.";
        }
        if (MaconomyDocumentMapping.TargetOf(file.Kind) == MaconomyDocumentTarget.Customer)
        {
            if (!context.CustomerEnabled)
            {
                return "The customer sync is switched off, and it governs the customer's documents.";
            }
            return request.Client is { IsMaconomyClient: true } ? null : "The client is not in Maconomy yet.";
        }
        if (!context.JobEnabled)
        {
            return "The job sync is switched off, and it governs the job's documents.";
        }
        return string.IsNullOrWhiteSpace(request.MaconomyJobNumber) ? "The job has not been created in Maconomy yet." : null;
    }

    /// <summary>Whether the file still has to go: not in Maconomy, and not on its way there right now.</summary>
    public static bool IsOutstanding(RemsFile file, bool stale)
        => file.MaconomyDocumentSyncStatus switch
        {
            RemsMaconomyDocumentSyncStatus.Synced => false,
            RemsMaconomyDocumentSyncStatus.Syncing => stale,
            _ => true,
        };

    /// <summary>The request's files in one line: how many are in Maconomy, failed or waiting, and what stands in the way.</summary>
    public static MaconomyDocumentSummary Summarise(RemsRequest request, MaconomyDocumentSyncContext context, Func<RemsFile, bool> isStale)
    {
        var files = request.Files.Where(f => !f.Deleted).ToList();
        var uploaded = files.Count(f => f.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Synced);
        var failed = files.Count(f => f.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Failed
            || (f.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Syncing && isStale(f)));
        var outstanding = files.Where(f => IsOutstanding(f, isStale(f))).ToList();
        var reasons = outstanding.Select(f => WhyNotUploadable(request, f, context)).ToList();
        var canUpload = reasons.Any(r => r is null);
        return new MaconomyDocumentSummary(
            files.Count, uploaded, failed, files.Count - uploaded - failed,
            canUpload,
            canUpload ? null : reasons.FirstOrDefault(r => r is not null));
    }
}
