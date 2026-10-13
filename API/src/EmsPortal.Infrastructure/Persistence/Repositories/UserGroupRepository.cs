using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class UserGroupRepository : IUserGroupRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public UserGroupRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserGroup>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.UserGroups.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(g => g.Name.Contains(term) || (g.Description != null && g.Description.Contains(term)));
        }

        // Most-recently-touched first, with name as the tie-break so equal timestamps stay predictable.
        return await query.OrderByDescending(g => g.UpdatedOnUtc).ThenBy(g => g.Name).Take(_dbContext.MaxRows).ToListAsync(cancellationToken);
    }

    public Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.UserGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<UserGroup?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return _dbContext.UserGroups.FirstOrDefaultAsync(g => g.Name.ToLower() == trimmed.ToLower(), cancellationToken);
    }

    public async Task AddAsync(UserGroup group, CancellationToken cancellationToken = default)
        => await _dbContext.UserGroups.AddAsync(group, cancellationToken);

    public void Remove(UserGroup group) => _dbContext.UserGroups.Remove(group);

    public async Task<IReadOnlyList<UserGroupMember>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.UserGroupMembers.Where(m => m.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserGroupMember>> GetMembersByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        => await _dbContext.UserGroupMembers.Where(m => m.UserGroupId == groupId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserGroupMember>> GetMembersWithUsersByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        => await _dbContext.UserGroupMembers
            .Where(m => m.UserGroupId == groupId)
            .Include(m => m.User)
            .ToListAsync(cancellationToken);

    public async Task AddMemberAsync(UserGroupMember member, CancellationToken cancellationToken = default)
        => await _dbContext.UserGroupMembers.AddAsync(member, cancellationToken);

    public void RemoveMember(UserGroupMember member) => _dbContext.UserGroupMembers.Remove(member);

    public async Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _dbContext.UserGroupMembers
            .GroupBy(m => m.UserGroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => c.GroupId, c => c.Count);
    }
}
