using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.UniversalFeatures;

/// <summary>A notification as returned to the client.</summary>
public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    EntityType? EntityType,
    Guid? EntityId,
    bool IsRead,
    bool IsGrouped,
    DateTime CreatedOnUtc);

/// <summary>A single notification-type channel preference.</summary>
public sealed record NotificationPreferenceResponse(NotificationType NotificationType, bool InApp);

/// <summary>Request to update one notification preference row (in-app channel only).</summary>
public sealed class NotificationPreferenceItem
{
    public NotificationType NotificationType { get; set; }
    public bool InApp { get; set; }
}

/// <summary>Request to update the user's notification preference matrix.</summary>
public sealed class UpdateNotificationPreferencesRequest
{
    public List<NotificationPreferenceItem> Preferences { get; set; } = new();
}
