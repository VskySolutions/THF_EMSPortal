using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class TagRepository : ITagRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public TagRepository(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
        => _dbContext.Tags.AddAsync(tag, cancellationToken).AsTask();

    public Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        => _dbContext.Tags.FirstOrDefaultAsync(t => t.Name == name, cancellationToken);

    public void Update(Tag tag) => _dbContext.Tags.Update(tag);

    public void Remove(Tag tag) => _dbContext.Tags.Remove(tag);

    public async Task<IReadOnlyList<Tag>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Tags.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => EF.Functions.Like(t.Name, $"%{search}%"));
        }

        // Most-recently-touched first, with name as the tie-break so equal timestamps stay predictable.
        return await query.OrderByDescending(t => t.UpdatedOnUtc).ThenBy(t => t.Name).Take(_dbContext.MaxRows).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetUsageCountsAsync(CancellationToken cancellationToken = default)
        => await _dbContext.EntityTags
            .GroupBy(e => e.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, cancellationToken);

    public async Task<IReadOnlyList<EntityTag>> GetEntityTagsAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
        => await _dbContext.EntityTags
            .Include(e => e.Tag)
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToListAsync(cancellationToken);

    public Task<EntityTag?> GetEntityTagAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.EntityTags.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<EntityTag?> GetEntityTagAsync(EntityType entityType, Guid entityId, Guid tagId, CancellationToken cancellationToken = default)
        => _dbContext.EntityTags.FirstOrDefaultAsync(
            e => e.EntityType == entityType && e.EntityId == entityId && e.TagId == tagId, cancellationToken);

    public async Task<IReadOnlyList<EntityTag>> GetApplicationsByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
        => await _dbContext.EntityTags.Where(e => e.TagId == tagId).ToListAsync(cancellationToken);

    public Task AddEntityTagAsync(EntityTag entityTag, CancellationToken cancellationToken = default)
        => _dbContext.EntityTags.AddAsync(entityTag, cancellationToken).AsTask();

    public void RemoveEntityTag(EntityTag entityTag) => _dbContext.EntityTags.Remove(entityTag);
}
