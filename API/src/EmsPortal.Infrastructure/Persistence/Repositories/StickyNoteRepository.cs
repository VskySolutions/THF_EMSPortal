using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class StickyNoteRepository : IStickyNoteRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public StickyNoteRepository(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public Task AddAsync(StickyNote note, CancellationToken cancellationToken = default)
        => _dbContext.StickyNotes.AddAsync(note, cancellationToken).AsTask();

    public Task<StickyNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.StickyNotes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public void Update(StickyNote note) => _dbContext.StickyNotes.Update(note);

    public void Remove(StickyNote note) => _dbContext.StickyNotes.Remove(note);

    public async Task<IReadOnlyList<StickyNote>> ListActiveForUserAsync(Guid userId, string? scope, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StickyNotes.Where(s =>
            (s.IsPersonal && s.CreatedByUserId == userId)
            || (!s.IsPersonal && !_dbContext.StickyNoteDismissals.Any(d => d.StickyNoteId == s.Id && d.UserId == userId)));

        // Scope filter: global notes always show; route-scoped notes only on a matching route.
        if (!string.IsNullOrWhiteSpace(scope))
        {
            query = query.Where(s => s.Scope == "global" || s.Scope == scope);
        }
        else
        {
            query = query.Where(s => s.Scope == "global");
        }

        return await query.OrderByDescending(s => s.UpdatedOnUtc).ThenByDescending(s => s.CreatedOnUtc).Take(_dbContext.MaxRows).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(StickyNote Note, int DismissalCount)>> ListTenantNotesWithCountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.StickyNotes
            .Where(s => !s.IsPersonal)
            .Select(s => new { Note = s, Count = _dbContext.StickyNoteDismissals.Count(d => d.StickyNoteId == s.Id) })
            .OrderByDescending(x => x.Note.UpdatedOnUtc)
            .ThenByDescending(x => x.Note.CreatedOnUtc)
            .Take(_dbContext.MaxRows)
            .ToListAsync(cancellationToken);
        return rows.Select(x => (x.Note, x.Count)).ToList();
    }

    public async Task<IReadOnlyList<StickyNote>> GetExpiredTenantNotesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _dbContext.StickyNotes
            .Where(s => !s.IsPersonal && s.ExpiresAtUtc != null && s.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);

    public Task<StickyNoteDismissal?> GetDismissalAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.StickyNoteDismissals.FirstOrDefaultAsync(d => d.StickyNoteId == noteId && d.UserId == userId, cancellationToken);

    public Task AddDismissalAsync(StickyNoteDismissal dismissal, CancellationToken cancellationToken = default)
        => _dbContext.StickyNoteDismissals.AddAsync(dismissal, cancellationToken).AsTask();

    public async Task<IReadOnlyList<StickyNoteDismissal>> GetDismissalsByNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
        => await _dbContext.StickyNoteDismissals.Where(d => d.StickyNoteId == noteId).ToListAsync(cancellationToken);

    public void RemoveDismissal(StickyNoteDismissal dismissal) => _dbContext.StickyNoteDismissals.Remove(dismissal);

    public Task<UserStickyNoteState?> GetStateAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.UserStickyNoteStates.FirstOrDefaultAsync(s => s.StickyNoteId == noteId && s.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, UserStickyNoteState>> GetStatesAsync(Guid userId, IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken = default)
    {
        if (noteIds.Count == 0)
        {
            return new Dictionary<Guid, UserStickyNoteState>();
        }

        return await _dbContext.UserStickyNoteStates
            .Where(s => s.UserId == userId && noteIds.Contains(s.StickyNoteId))
            .ToDictionaryAsync(s => s.StickyNoteId, s => s, cancellationToken);
    }

    public Task AddStateAsync(UserStickyNoteState state, CancellationToken cancellationToken = default)
        => _dbContext.UserStickyNoteStates.AddAsync(state, cancellationToken).AsTask();

    public void UpdateState(UserStickyNoteState state) => _dbContext.UserStickyNoteStates.Update(state);
}
