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
    /// <summary>What the job card's specification 2 is given for the Job Type; null while the request names none.</summary>
    string? JobTypeValue,
    string? TemplateJobNumber,
    string? ProjectManagerNumber,
    string? JobName,
    string? Department,
    /// <summary>The Department's Maconomy location number; null while it carries none, so the template job's location stands.</summary>
    string? LocationNumber,
    string? PurchaseOrderNumber,
    /// <summary>The purchase order's dates as the card takes them (yyyy-MM-dd); null while the contract names none.</summary>
    string? PurchaseOrderStartDate = null,
    string? PurchaseOrderEndDate = null,
    /// <summary>The chosen marketing methods' labels, joined; null while none is chosen.</summary>
    string? MarketingMethods = null,
    string? FiscalYear = null,
    string? FiscalMonth = null,
    int JobDocuments = 0,
    string? AccountManagerNumber = null,
    string? BillingManagerNumber = null,
    /// <summary>The commission recipients as sent: "number — share%", in order.</summary>
    IReadOnlyList<string>? Commission = null,
    /// <summary>People on the engagement with no Maconomy Number, who are therefore left out.</summary>
    IReadOnlyList<string>? MissingNumbers = null,
    /// <summary>No template job could be resolved: the press has to name one.</summary>
    bool NeedsTemplate = false,
    /// <summary>The First-Year Fee Estimate, put on the job's budget (task FEE) after the create; null when blank.</summary>
    decimal? FeeEstimate = null,
    /// <summary>The job is already in Maconomy: a press only puts the fee estimate on its budget.</summary>
    bool BudgetOnly = false);

/// <summary>Sync job now's optional body: the template job picked when none could be resolved.</summary>
public sealed class RemsMaconomyJobSyncRequest
{
    public string? TemplateJobNumber { get; set; }
}

/// <summary>One of Maconomy's template jobs, for the pick.</summary>
public sealed record MaconomyJobTemplateOption(string Number, string Name);

/// <summary>The settings page's Job tab: the switch, the settings, the counts, the service line and department maps.</summary>
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
    IReadOnlyList<MaconomyServiceLineRow> ServiceLines,
    IReadOnlyList<MaconomyDepartmentRow> Departments);

/// <summary>
/// One value of the tenant's REMS Service Line list and the Maconomy template job number it carries
/// (<c>OptionSetItem.MaconomyNumber</c>, edited under Option Sets), blank while unset.
/// </summary>
public sealed record MaconomyServiceLineRow(Guid ServiceLineId, string Code, string Label, string? TemplateJobNumber);

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
    RemsMaconomyJobSyncView Sync,
    /// <summary>How the request's files stand with Maconomy: attachments under the customer, the CAF and purchase order under the job.</summary>
    RemsMaconomyDocumentsView? Documents = null);

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
        RemsRequest request, MaconomyJobSyncContext context, bool stale, bool canManage, string? syncedByName,
        MaconomyJobLookups? lookups = null)
    {
        // The mapped write itself, so the confirmation shows exactly what a press sends.
        var write = MaconomyJobDataMapping.Map(request, context, lookups);
        var template = write.Values.GetValueOrDefault(MaconomyJobDataMapping.TemplateField) ?? write.InitValues?.GetValueOrDefault(MaconomyJobDataMapping.TemplateField);
        // The share as a person reads it; Maconomy is sent it in hundredths.
        var recipients = MaconomyJobDataMapping.CommissionRecipientsOf(request);
        var commission = Enumerable.Range(0, recipients.Count)
            .Where(i => write.Values.ContainsKey(MaconomyJobDataMapping.CommissionEmployeeFields[i]))
            .Select(i => $"{write.Values[MaconomyJobDataMapping.CommissionEmployeeFields[i]]} — {recipients[i].CommissionPercentage:0.##}%")
            .ToList();
        return new RemsMaconomyJobSyncPreview(
            For(request, context, stale, canManage, syncedByName),
            request.RemsNumber,
            request.Client is { } client ? MaconomyJobDataMapping.ClientNameOf(client) : string.Empty,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.CustomerField),
            request.ServiceLine?.Label,
            request.JobType?.Label,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.JobTypeField),
            template,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.ProjectManagerField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.NameField),
            request.Department?.Label,
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.LocationField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.PurchaseOrderField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.PurchaseOrderStartField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.PurchaseOrderEndField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.MarketingMethodsField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.FiscalYearField),
            MaconomyJobDataMapping.FiscalYearEndOf(request) is { } end && write.Values.ContainsKey(MaconomyJobDataMapping.FiscalMonthField)
                ? MaconomyJobDataMapping.MonthNameOf(end)
                : null,
            request.Files.Count(f => !f.Deleted && MaconomyDocumentMapping.TargetOf(f.Kind) == MaconomyDocumentTarget.Job),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.AccountManagerField),
            write.Values.GetValueOrDefault(MaconomyJobDataMapping.BillingManagerField),
            commission,
            MaconomyJobDataMapping.MissingNumbersOf(request),
            NeedsTemplate: string.IsNullOrWhiteSpace(template) && !MaconomyJobSyncRules.IsBudgetOwed(request),
            FeeEstimate: request.FirstYearFeeEstimate,
            BudgetOnly: MaconomyJobSyncRules.IsBudgetOwed(request));
    }

    public static string MessageFor(RemsMaconomyJobSyncView view) => view.Status switch
    {
        RemsMaconomySyncStatuses.Synced => $"Maconomy job {view.JobNumber} is in place.",
        RemsMaconomySyncStatuses.Failed => $"The job sync failed: {view.FailureReason}",
        _ => "Sync recorded.",
    };
}
