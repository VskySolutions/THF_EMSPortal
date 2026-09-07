using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for <see cref="EmailTemplate"/>.</summary>
public interface IEmailTemplateRepository
{
    /// <summary>All non-deleted templates in scope for a tenant: its overrides plus the global defaults.</summary>
    Task<IReadOnlyList<EmailTemplate>> ListForScopeAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The exact row for a scope + key (tenant override when tenantId set, global default when null),
    /// or null.
    /// </summary>
    Task<EmailTemplate?> GetAsync(Guid? tenantId, EmailTemplateKey key, CancellationToken cancellationToken = default);

    /// <summary>The effective template for a tenant: its override when present, otherwise the global default.</summary>
    Task<EmailTemplate?> GetEffectiveAsync(Guid? tenantId, EmailTemplateKey key, CancellationToken cancellationToken = default);

    Task AddAsync(EmailTemplate template, CancellationToken cancellationToken = default);

    void Update(EmailTemplate template);

    void Remove(EmailTemplate template);
}
