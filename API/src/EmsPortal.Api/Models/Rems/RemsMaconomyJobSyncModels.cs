using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// Where posting the request's engagement to Maconomy as a job has got to. On every request the lists
/// and the page hand out; the status words are the customer sync's (<see cref="RemsMaconomySyncStatuses"/>),
/// minus linked.
/// </summary>
public sealed record RemsMaconomyJobSyncView(
    string Status,
    string? JobNumber,
    /// <summary>Who pressed the sync that created the job; null when it ran by itself.</summary>
    string? SyncedByName,
    DateTime? SyncedOnUtc,
    string? FailureReason,
    /// <summary>Whether the tenant's job sync is switched on. Off hides the action everywhere.</summary>
    bool SyncEnabled,
    /// <summary>Whether THIS caller may press Sync job now on it.</summary>
    bool CanSyncNow,
    /// <summary>
    /// Why the caller cannot press it although the row offers the action: an approved request not yet
    /// eligible (client not in Maconomy, no template, …). Null when they can, and null when the action is
    /// not offered at all.
    /// </summary>
    string? BlockedReason);

/// <summary>What a press would send, shown in the confirmation before it.</summary>
public sealed record RemsMaconomyJobSyncPreview(
    RemsMaconomyJobSyncView Sync,
    string RemsNumber,
    string ClientName,
    string? CustomerNumber,
    string? ServiceLine,
    string? JobType,
    string? TemplateJobNumber,
    string? ProjectManagerNumber,
    string? JobName,
    string? Department,
    /// <summary>The Department's Maconomy location number; null while it carries none, so the template job's location stands.</summary>
    string? LocationNumber,
    string? PurchaseOrderNumber);

/// <summary>The settings page's Job tab: the switch, the settings, the counts, the job type and department maps.</summary>
public sealed record MaconomyJobSyncOverviewResponse(
    bool Connected,
    bool Enabled,
    DateTime? ChangedOnUtc,
    string? ChangedByName,
    string? ProjectManagerNumber,
    string? DefaultTemplateNumber,
    int Synced,
    int Failed,
    int Syncing,
    int Waiting,
    IReadOnlyList<MaconomyJobTypeRow> JobTypes,
    IReadOnlyList<MaconomyDepartmentRow> Departments);

/// <summary>
/// One value of the tenant's REMS Job Type list and the Maconomy template job number it carries
/// (<c>OptionSetItem.MaconomyNumber</c>, edited under Option Sets), blank while unset.
/// </summary>
public sealed record MaconomyJobTypeRow(Guid JobTypeId, string Code, string Label, string? TemplateJobNumber);

/// <summary>One offered value of the tenant's REMS Department list and the Maconomy location number it carries, blank while unset.</summary>
public sealed record MaconomyDepartmentRow(Guid DepartmentId, string Code, string Label, string? LocationNumber);

/// <summary>One row of the Job tab's table: an approved request and where its job stands.</summary>
public sealed record MaconomyJobSyncRow(
    Guid RemsId,
    string RemsNumber,
    string ClientName,
    string? CustomerNumber,
    string? ServiceLine,
    string? JobType,
    string? Department,
    DateTime UpdatedOnUtc,
    RemsMaconomyJobSyncView Sync);

public static class RemsMaconomyJobSyncViews
{
    public const string StaleReason = "The portal lost track of this attempt. Sync job now to try again.";

    public static string StatusOf(RemsRequest request, bool stale) => stale
        ? RemsMaconomySyncStatuses.Failed
        : request.MaconomyJobSyncStatus switch
        {
            RemsMaconomyJobSyncStatus.Pending => RemsMaconomySyncStatuses.Pending,
            RemsMaconomyJobSyncStatus.Syncing => RemsMaconomySyncStatuses.Syncing,
            RemsMaconomyJobSyncStatus.Synced => RemsMaconomySyncStatuses.Synced,
            RemsMaconomyJobSyncStatus.Failed => RemsMaconomySyncStatuses.Failed,
            _ => RemsMaconomySyncStatuses.NotSynced,
        };

    /// <summary>
    /// The view for a request. The action is offered — pressable or blocked with its reason — only to a
    /// caller who may manage it, only while the sync is on (off hides it; user, 31 Oct 2026), and only on
    /// an approved request without a job yet.
    /// </summary>
    public static RemsMaconomyJobSyncView For(
        RemsRequest request, MaconomyJobSyncContext context, bool stale, bool canManage, string? syncedByName)
    {
        var status = StatusOf(request, stale);
        var blocked = MaconomyJobSyncRules.WhyNotEligible(request, context, stale);
        var offered = canManage && context.Enabled
            && string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal)
            && status is RemsMaconomySyncStatuses.NotSynced or RemsMaconomySyncStatuses.Failed;
        return new RemsMaconomyJobSyncView(
            status,
            request.MaconomyJobNumber,
            syncedByName,
            request.MaconomyJobSyncedOnUtc,
            stale ? StaleReason : request.MaconomyJobSyncError,
            context.Enabled,
            CanSyncNow: offered && blocked is null,
            BlockedReason: offered ? blocked : null);
    }

    /// <summary>
    /// The view for a list row or the request page: null while there is nothing to say — a request not yet
    /// approved with no attempt recorded — so the chip stays off every row that cannot have a job.
    /// </summary>
    public static RemsMaconomyJobSyncView? ForListed(
        RemsRequest request, MaconomyJobSyncContext context, bool stale, bool canManage, string? syncedByName)
        => request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.NotSynced
            && !string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal)
            ? null
            : For(request, context, stale, canManage, syncedByName);

    public static RemsMaconomyJobSyncPreview PreviewFor(
        RemsRequest request, MaconomyJobSyncContext context, bool stale, bool canManage, string? syncedByName)
    {
        // The mapped write itself, so the confirmation shows exactly what a press sends.
        var write = MaconomyJobDataMapping.Map(request, context);
        return new RemsMaconomyJobSyncPreview(
            For(request, context, stale, canManage, syncedByName),
            request.RemsNumber,
            request.Client is { } client ? MaconomyJobDataMapping.ClientNameOf(client) : string.Empty,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.CustomerField),
            request.ServiceLine?.Label,
            request.JobType?.Label,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.TemplateField) ?? write.InitValues?.GetValueOrDefault(MaconomyJobDataMapping.TemplateField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.ProjectManagerField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.NameField),
            request.Department?.Label,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.LocationField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.PurchaseOrderField));
    }

    public static string MessageFor(RemsMaconomyJobSyncView view) => view.Status switch
    {
        RemsMaconomySyncStatuses.Synced => $"Maconomy job {view.JobNumber} created.",
        RemsMaconomySyncStatuses.Failed => $"The job sync failed: {view.FailureReason}",
        _ => "Sync recorded.",
    };
}
