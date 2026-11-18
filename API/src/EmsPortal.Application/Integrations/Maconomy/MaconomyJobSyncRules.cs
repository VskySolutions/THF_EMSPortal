using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// What may be posted as a job, as a pure function of the request and the tenant's context — so the
/// lists, the confirmation and the sync itself give one answer. What is SENT is
/// <see cref="MaconomyJobDataMapping"/>'s business.
/// </summary>
public static class MaconomyJobSyncRules
{
    public const string NotConnectedReason = "Maconomy is not connected for this tenant.";
    public const string SettingsPlace = "Tenant Settings → Maconomy → Job";

    /// <summary>Why the request cannot be posted now, or null when it can. Switched-off is a reason too; the lists hide the action on it.</summary>
    public static string? WhyNotEligible(RemsRequest request, MaconomyJobSyncContext context, bool stale)
    {
        if (!context.Connected)
        {
            return NotConnectedReason;
        }
        if (!context.Enabled)
        {
            return $"The job sync is switched off. Start it under {SettingsPlace}.";
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Synced)
        {
            return $"This engagement is already Maconomy job {request.MaconomyJobNumber}.";
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Pending && !stale)
        {
            return "Queued to be posted. Give it a moment, then refresh.";
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Syncing && !stale)
        {
            return "An attempt is already under way. Give it a moment, then refresh.";
        }
        return WhyNotPostable(request, context);
    }

    /// <summary>What the request itself still lacks, whatever its sync state: the queued attempt's own check before it posts.</summary>
    public static string? WhyNotPostable(RemsRequest request, MaconomyJobSyncContext context)
    {
        if (!string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return "The request is not approved yet.";
        }
        if (request.Client is not { IsMaconomyClient: true })
        {
            return "The client is not in Maconomy yet. Sync the customer first.";
        }
        if (request.ServiceLineId is null)
        {
            return "The request names no Service Line, which the template job is found by.";
        }
        if (request.JobTypeId is null)
        {
            return "The request names no Job Type, which the job name needs.";
        }
        return null;
    }

    /// <summary>Why a job sync stopped for want of a template job. Sync job now reads it as "ask which one".</summary>
    public static string NoTemplateReason(RemsRequest request)
        => $"{NoTemplatePrefix} \"{request.ServiceLine?.Label?.Trim()}\". Press Sync job now and pick the template job to use.";

    public const string NoTemplatePrefix = "No Maconomy template job is named as the Service Line";

    /// <summary>The job is in Maconomy and its budget's fee line is not: a sync then sets only the line.</summary>
    public static bool IsBudgetOwed(RemsRequest request)
        => request.IsMaconomyJobBudgetSyncPending && !string.IsNullOrWhiteSpace(request.MaconomyJobNumber);

    public static string BudgetOwedReason(string jobNumber, string why)
        => $"Job {jobNumber} is in Maconomy, but the First-Year Fee Estimate could not be put on its budget: {why} Press Sync job now to try that again; no second job is created.";

    /// <summary>What a refusal is followed by when people on the engagement have no Maconomy Number.</summary>
    public static string? MissingNumbersAdvice(RemsRequest request)
        => MaconomyJobDataMapping.MissingNumbersOf(request) is { Count: > 0 } missing
            ? $" No Maconomy Number is set for: {string.Join(", ", missing)}. Add it on each user's page under Administration → Users, then sync again."
            : null;
}
