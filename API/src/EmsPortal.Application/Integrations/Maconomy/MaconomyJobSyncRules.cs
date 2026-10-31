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
    public const string SettingsPlace = "Tenant Settings → Maconomy → Job";

    /// <summary>Why the request cannot be posted now, or null when it can. Switched-off is a reason too; the lists hide the action on it.</summary>
    public static string? WhyNotEligible(RemsRequest request, MaconomyJobSyncContext context, bool stale)
    {
        if (!context.Connected)
        {
            return "Maconomy is not connected for this tenant.";
        }
        if (!context.Enabled)
        {
            return $"The job sync is switched off. Start it under {SettingsPlace}.";
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Synced)
        {
            return $"This engagement is already Maconomy job {request.MaconomyJobNumber}.";
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Syncing && !stale)
        {
            return "An attempt is already under way. Give it a moment, then refresh.";
        }
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
            return "The request names no Service Line, which the job name needs.";
        }
        if (MaconomyJobDataMapping.ResolveTemplate(request, context) is null)
        {
            return request.JobTemplateId is null
                ? $"The request names no Job Template and no default template job is set under {SettingsPlace}."
                : $"The request's Job Template is not mapped to a Maconomy template job, and no default template job is set under {SettingsPlace}.";
        }
        if (string.IsNullOrWhiteSpace(context.ProjectManagerNumber))
        {
            return $"No project manager employee number is set under {SettingsPlace}.";
        }
        return null;
    }
}
