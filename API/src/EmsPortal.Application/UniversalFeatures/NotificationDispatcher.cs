using EmsPortal.Application.Abstractions.Branding;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.UniversalFeatures;

/// <summary>
/// Default <see cref="INotificationDispatcher"/>. Stages an in-app <see cref="Notification"/> (honouring
/// the user's per-type in-app preference) and dedupes duplicates within a 60-second window.
/// <para>
/// In-app only since WO-124: internal workflow notifications (mentions, reminders, …) never send email
/// (REQ-UNI-002.2 / 010.2). External email is limited to the account-security and REMS templates on the
/// email allowlist and is dispatched through <c>IEmailDispatcher</c> / <c>IRemsEmailNotifier</c>, not here.
/// </para>
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    /// <summary>Duplicate-suppression window — repeated notifications inside it are grouped.</summary>
    private static readonly TimeSpan GroupingWindow = TimeSpan.FromSeconds(60);

    private readonly INotificationRepository _notifications;
    private readonly ISiteNameProvider _siteName;
    private readonly ITenantContext _tenant;

    public NotificationDispatcher(INotificationRepository notifications, ISiteNameProvider siteName, ITenantContext tenant)
    {
        _notifications = notifications;
        _siteName = siteName;
        _tenant = tenant;
    }

    public async Task DispatchAsync(CreateNotificationDto notification, CancellationToken cancellationToken = default)
    {
        // The in-app channel is default-on (AC-UNI-013.4); a user may still opt out of it per type.
        var preference = await _notifications.GetPreferenceAsync(notification.UserId, notification.Type, cancellationToken);
        if (!(preference?.InApp ?? true))
        {
            return;
        }

        var isGrouped = await _notifications.HasRecentDuplicateAsync(
            notification.UserId, notification.Type, notification.EntityType, notification.EntityId,
            DateTime.UtcNow - GroupingWindow, cancellationToken);

        // "REMS" in the wording is said as the site's name; with no tenant resolved the caller words it.
        var tenantId = _tenant.IsResolved ? _tenant.TenantId : (Guid?)null;
        await _notifications.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = notification.UserId,
            Type = notification.Type,
            Title = await _siteName.ApplyAsync(tenantId, notification.Title, cancellationToken),
            Body = await _siteName.ApplyAsync(tenantId, notification.Body, cancellationToken),
            EntityType = notification.EntityType,
            EntityId = notification.EntityId,
            IsRead = false,
            IsGrouped = isGrouped,
        }, cancellationToken);
    }
}
