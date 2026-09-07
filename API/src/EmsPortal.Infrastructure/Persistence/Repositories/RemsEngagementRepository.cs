using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsEngagementRepository : IRemsEngagementRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsEngagementRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<REMSEngagement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagements
            .Include(e => e.Department)
            .Include(e => e.ServiceLine)
            .Include(e => e.Industry)
            .Include(e => e.BillingPeriod)
            .Include(e => e.MarketingMethods)
            .Include(e => e.CommissionSplits)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    // The client and its entities now hang off the REQUEST beside the engagement rather than above it, so
    // the context is reached downwards through Rems instead of upwards through Entity → Client → Rems.
    // Clients is a collection at the EF level (one active row, enforced by a filtered unique index), which
    // is why this reads as a list rather than a reference.
    public Task<REMSEngagement?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagements
            .Include(e => e.Department)
            .Include(e => e.ServiceLine)
            .Include(e => e.Industry)
            .Include(e => e.BillingPeriod)
            .Include(e => e.MarketingMethods)
            .Include(e => e.CommissionSplits)
            .Include(e => e.Rems).ThenInclude(r => r!.Clients).ThenInclude(c => c.Entities).ThenInclude(en => en.Addresses).ThenInclude(a => a.Address)
            // The client's referral source is an option item now, so the packet reads its code off the
            // navigation rather than a string column.
            .Include(e => e.Rems).ThenInclude(r => r!.Clients).ThenInclude(c => c.ReferralSource)
            // The request's own type and status: RemsSetupAccess reads the status CODE off this graph to
            // decide whether the setup is this caller's to work, and it is reached through the engagement
            // rather than loaded separately.
            .Include(e => e.Rems).ThenInclude(r => r!.Status)
            .Include(e => e.Rems).ThenInclude(r => r!.Type)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<REMSEngagement?> GetByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagements
            .Include(e => e.Department)
            .Include(e => e.ServiceLine)
            .Include(e => e.Industry)
            .Include(e => e.BillingPeriod)
            .Include(e => e.MarketingMethods)
            .Include(e => e.CommissionSplits)
            .FirstOrDefaultAsync(e => e.REMSId == remsId, cancellationToken);

    public async Task<IReadOnlyList<REMSEngagementAuditDetail>> ListAuditDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default)
        => engagementIds.Count == 0
            ? Array.Empty<REMSEngagementAuditDetail>()
            : await _dbContext.RemsEngagementAuditDetails
                // The signed client-acceptance form travels with the detail: the workspace names the
                // document on screen, and a media id on its own is not something anyone can read.
                .Include(d => d.ClientAcceptanceFormMedia)
                .Where(d => engagementIds.Contains(d.REMSEngagementId))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<REMSEngagementGovernmentDetail>> ListGovernmentDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default)
        => engagementIds.Count == 0
            ? Array.Empty<REMSEngagementGovernmentDetail>()
            : await _dbContext.RemsEngagementGovernmentDetails
                // The uploaded purchase order travels with the detail, for the same reason the CAF does:
                // the workspace names the document on screen, and a media id is not a name.
                .Include(d => d.PurchaseOrderMedia)
                .Include(d => d.PersonnelLevel)
                .Where(d => engagementIds.Contains(d.REMSEngagementId))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<REMSEngagementTaxDetail>> ListTaxDetailsAsync(IReadOnlyCollection<Guid> engagementIds, CancellationToken cancellationToken = default)
        => engagementIds.Count == 0
            ? Array.Empty<REMSEngagementTaxDetail>()
            : await _dbContext.RemsEngagementTaxDetails
                .Include(d => d.TaxForms)
                .Where(d => engagementIds.Contains(d.REMSEngagementId))
                .ToListAsync(cancellationToken);

    public async Task AddAsync(REMSEngagement engagement, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagements.AddAsync(engagement, cancellationToken);

    public void Update(REMSEngagement engagement) => _dbContext.RemsEngagements.Update(engagement);

    public void Remove(REMSEngagement engagement) => _dbContext.RemsEngagements.Remove(engagement);

    public Task<REMSEngagementAuditDetail?> GetAuditDetailAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagementAuditDetails.FirstOrDefaultAsync(d => d.REMSEngagementId == engagementId, cancellationToken);

    public Task<REMSEngagementGovernmentDetail?> GetGovernmentDetailAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagementGovernmentDetails
            .Include(d => d.PersonnelLevel)
            .FirstOrDefaultAsync(d => d.REMSEngagementId == engagementId, cancellationToken);

    public Task<REMSEngagementTaxDetail?> GetTaxDetailAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagementTaxDetails
            .Include(d => d.TaxForms)
            .FirstOrDefaultAsync(d => d.REMSEngagementId == engagementId, cancellationToken);

    public async Task AddAuditDetailAsync(REMSEngagementAuditDetail detail, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementAuditDetails.AddAsync(detail, cancellationToken);

    public async Task AddGovernmentDetailAsync(REMSEngagementGovernmentDetail detail, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementGovernmentDetails.AddAsync(detail, cancellationToken);

    public async Task AddTaxDetailAsync(REMSEngagementTaxDetail detail, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementTaxDetails.AddAsync(detail, cancellationToken);

    public async Task AddTaxFormAsync(REMSEngagementTaxForm taxForm, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementTaxForms.AddAsync(taxForm, cancellationToken);

    public void RemoveTaxForm(REMSEngagementTaxForm taxForm) => _dbContext.RemsEngagementTaxForms.Remove(taxForm);

    public async Task AddMarketingMethodAsync(REMSEngagementMarketingMethod method, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementMarketingMethods.AddAsync(method, cancellationToken);

    public void RemoveMarketingMethod(REMSEngagementMarketingMethod method) => _dbContext.RemsEngagementMarketingMethods.Remove(method);

    public async Task AddCommissionSplitAsync(REMSEngagementCommissionSplit split, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementCommissionSplits.AddAsync(split, cancellationToken);

    public void RemoveCommissionSplit(REMSEngagementCommissionSplit split) => _dbContext.RemsEngagementCommissionSplits.Remove(split);

    public Task<int> CountActiveCommissionSplitsAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEngagementCommissionSplits.CountAsync(s => s.REMSEngagementId == engagementId, cancellationToken);

    public async Task<IReadOnlyList<REMSEngagementApprover>> ListApproversAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementApprovers
            .Where(a => a.REMSEngagementId == engagementId)
            .ToListAsync(cancellationToken);

    public async Task AddApproverAsync(REMSEngagementApprover approver, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEngagementApprovers.AddAsync(approver, cancellationToken);

    public void RemoveApprover(REMSEngagementApprover approver) => _dbContext.RemsEngagementApprovers.Remove(approver);
}
