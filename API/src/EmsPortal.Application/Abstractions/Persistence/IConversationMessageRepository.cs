using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for <see cref="ConversationMessage"/>s — a record's conversation — and their <see
/// cref="ConversationMessageMention"/>s.
/// </summary>
public interface IConversationMessageRepository
{
    Task AddAsync(ConversationMessage message, CancellationToken cancellationToken = default);

    /// <summary>Loads a message (including its mentions) by id, or null when missing/soft-deleted.</summary>
    Task<ConversationMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Update(ConversationMessage message);

    void Remove(ConversationMessage message);

    /// <summary>A record's conversation, newest-first and paginated; optional body search and author filter.</summary>
    Task<(IReadOnlyList<ConversationMessage> Items, int Total)> ListAsync(
        EntityType entityType, Guid entityId, string? search, Guid? authorId, int page, int limit, CancellationToken cancellationToken = default);

    Task AddMentionAsync(ConversationMessageMention mention, CancellationToken cancellationToken = default);

    void RemoveMention(ConversationMessageMention mention);

    /// <summary>
    /// Paginated @mentions of a user (joined to their message), newest-first; optional entity-type and
    /// read filters.
    /// </summary>
    Task<(IReadOnlyList<(ConversationMessageMention Mention, ConversationMessage Message)> Items, int Total)> ListMentionsForUserAsync(
        Guid userId, EntityType? entityType, bool? isRead, SortRequest sort, int page, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>A single mention belonging to the user, or null.</summary>
    Task<ConversationMessageMention?> GetMentionForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
