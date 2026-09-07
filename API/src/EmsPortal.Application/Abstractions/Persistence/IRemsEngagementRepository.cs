using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for the REMS engagement aggregate (WO-110): the engagement, its audit/government/tax
/// detail records, tax forms, marketing methods and commission splits.
/// </summary>
public interface IRemsEngagementRepository
{
    /// <summary>The engagement with its marketing methods and commission splits loaded.</summary>
    Task<REMSEngagement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The engagement with its full staff/approval context (WO-114): marketing methods, commission
    /// splits.
    /// </summary>
    Task<REMSEngagement?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A request's engagement, with marketing and commission loaded.</summary>
    Task<REMSEngagement?> GetByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>Audit details for a set of engagements (WO-114 workspace).</summary>
    Task<IReadOnlyList<REMSEngagementAuditDetail>> ListAuditDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default);

    /// <summary>Government details for a set of engagements (WO-114 workspace).</summary>
    Task<IReadOnlyList<REMSEngagementGovernmentDetail>> ListGovernmentDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default);

    /// <summary>Tax details (with their tax forms) for a set of engagements (WO-114 workspace).</summary>
    Task<IReadOnlyList<REMSEngagementTaxDetail>> ListTaxDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default);

    Task AddAsync(REMSEngagement engagement, CancellationToken cancellationToken = default);

    void Update(REMSEngagement engagement);

    void Remove(REMSEngagement engagement);

    Task<REMSEngagementAuditDetail?> GetAuditDetailAsync(Guid engagementId, CancellationToken cancellationToken = default);

    Task<REMSEngagementGovernmentDetail?> GetGovernmentDetailAsync(Guid engagementId, CancellationToken cancellationToken = default);

    /// <summary>The tax detail for an engagement with its tax forms loaded.</summary>
    Task<REMSEngagementTaxDetail?> GetTaxDetailAsync(Guid engagementId, CancellationToken cancellationToken = default);

    Task AddAuditDetailAsync(REMSEngagementAuditDetail detail, CancellationToken cancellationToken = default);

    Task AddGovernmentDetailAsync(REMSEngagementGovernmentDetail detail, CancellationToken cancellationToken = default);

    Task AddTaxDetailAsync(REMSEngagementTaxDetail detail, CancellationToken cancellationToken = default);

    Task AddTaxFormAsync(REMSEngagementTaxForm taxForm, CancellationToken cancellationToken = default);

    void RemoveTaxForm(REMSEngagementTaxForm taxForm);

    Task AddMarketingMethodAsync(REMSEngagementMarketingMethod method, CancellationToken cancellationToken = default);

    void RemoveMarketingMethod(REMSEngagementMarketingMethod method);

    Task AddCommissionSplitAsync(REMSEngagementCommissionSplit split, CancellationToken cancellationToken = default);

    void RemoveCommissionSplit(REMSEngagementCommissionSplit split);

    /// <summary>Active commission-split count for an engagement (backs the max-10-recipients rule).</summary>
    Task<int> CountActiveCommissionSplitsAsync(Guid engagementId, CancellationToken cancellationToken = default);

    /// <summary>The approvers picked for an engagement, or an empty list when none has been saved yet.</summary>
    Task<IReadOnlyList<REMSEngagementApprover>> ListApproversAsync(Guid engagementId, CancellationToken cancellationToken = default);

    Task AddApproverAsync(REMSEngagementApprover approver, CancellationToken cancellationToken = default);

    void RemoveApprover(REMSEngagementApprover approver);
}
