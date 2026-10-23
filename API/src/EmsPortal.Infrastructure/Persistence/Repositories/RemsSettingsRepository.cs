using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsSettingsRepository : IRemsSettingsRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsSettingsRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RemsSettings?> GetAsync(CancellationToken cancellationToken = default)
        => _dbContext.RemsSettings
            .Include(s => s.DepartmentDirectors).ThenInclude(d => d.Department)
            .Include(s => s.ChecklistTemplates.Where(t => !t.Deleted))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(RemsSettings settings, CancellationToken cancellationToken = default)
        => await _dbContext.RemsSettings.AddAsync(settings, cancellationToken);

    public void Update(RemsSettings settings) => _dbContext.RemsSettings.Update(settings);

    public async Task AddDepartmentDirectorAsync(RemsDepartmentDirector director, CancellationToken cancellationToken = default)
        => await _dbContext.RemsDepartmentDirectors.AddAsync(director, cancellationToken);

    public void UpdateDepartmentDirector(RemsDepartmentDirector director) => _dbContext.RemsDepartmentDirectors.Update(director);

    public void RemoveDepartmentDirector(RemsDepartmentDirector director) => _dbContext.RemsDepartmentDirectors.Remove(director);
    public async Task AddChecklistTemplateAsync(RemsChecklistTemplate template, CancellationToken cancellationToken = default)
        => await _dbContext.RemsChecklistTemplates.AddAsync(template, cancellationToken);
    public void RemoveChecklistTemplate(RemsChecklistTemplate template) => _dbContext.RemsChecklistTemplates.Remove(template);
}
