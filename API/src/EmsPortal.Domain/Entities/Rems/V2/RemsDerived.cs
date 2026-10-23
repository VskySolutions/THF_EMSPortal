using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// The states the old model stored and the new one derives. <see cref="RemsRequest.StatusId"/> is the only
/// workflow status; the form's state and the engagement's state are read off it and the invite columns so the
/// API keeps handing out the same words.
/// </summary>
public static class RemsDerived
{
    /// <summary>
    /// The intake form's state as the UI knows it: null before a form is built, Saved once it has an invite
    /// code but has not gone out, Sent while the client is filling it in, Submitted once they have.
    /// </summary>
    public static RemsFormStatus? FormStatusOf(RemsRequest request)
    {
        if (request.FormSubmittedOnUtc is not null)
        {
            return RemsFormStatus.Submitted;
        }
        if (request.InviteSentOnUtc is not null)
        {
            return RemsFormStatus.Sent;
        }
        return request.InviteCode is not null ? RemsFormStatus.Saved : null;
    }

    public static bool HasInvite(RemsRequest request) => request.InviteCode is not null;

    public static bool IsFormSubmitted(RemsRequest request) => request.FormSubmittedOnUtc is not null;

    /// <summary>The engagement's state as the UI reads it, taken from the request status code.</summary>
    public static RemsEngagementStatus EngagementStatusOf(string? requestStatusCode) => requestStatusCode switch
    {
        RemsRequestStatuses.PendingApproval => RemsEngagementStatus.PendingApproval,
        RemsRequestStatuses.ChangesRequested => RemsEngagementStatus.Rejected,
        RemsRequestStatuses.Approved => RemsEngagementStatus.Approved,
        _ => RemsEngagementStatus.Draft,
    };

    public static RemsEngagementStatus EngagementStatusOf(RemsRequest request) => EngagementStatusOf(request.Status?.Value);
}
