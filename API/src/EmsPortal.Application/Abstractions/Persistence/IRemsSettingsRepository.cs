using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for the per-tenant <see cref="RemsSettings"/> (WO-114): the managing shareholder and the
/// department-to-director mapping. Tenant isolation is applied by the ambient query filter.
/// </summary>
public interface IRemsSettingsRepository
{
    /// <summary>The tenant's settings row with its department-director mappings loaded, or null when unset.</summary>
    Task<RemsSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(RemsSettings settings, CancellationToken cancellationToken = default);

    void Update(RemsSettings settings);

    Task AddDepartmentDirectorAsync(RemsDepartmentDirector director, CancellationToken cancellationToken = default);

    void UpdateDepartmentDirector(RemsDepartmentDirector director);

    void RemoveDepartmentDirector(RemsDepartmentDirector director);
    Task AddChecklistTemplateAsync(RemsChecklistTemplate template, CancellationToken cancellationToken = default);
    void RemoveChecklistTemplate(RemsChecklistTemplate template);
}
