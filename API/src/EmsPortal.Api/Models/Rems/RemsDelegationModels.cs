namespace EmsPortal.Api.Models.Rems;

/// <summary>Name a delegate, or change what an existing one may do.</summary>
public sealed class SaveRemsDelegationRequest
{
    /// <summary>The person who will act on the caller's behalf.</summary>
    public Guid DelegateUserId { get; set; }

    /// <summary>May create and fill requests as the caller.</summary>
    public bool CanPrepare { get; set; } = true;

    /// <summary>May email the intake link to the client.</summary>
    public bool CanSend { get; set; }

    /// <summary>First day it applies (inclusive).</summary>
    public DateOnly? StartsOn { get; set; }

    /// <summary>Last day it applies (inclusive).</summary>
    public DateOnly? EndsOn { get; set; }
}

/// <summary>A delegate the caller has named, as shown on their own delegation list.</summary>
public sealed record RemsDelegationView(
    Guid Id,
    Guid DelegateUserId,
    string DelegateName,
    bool CanPrepare,
    bool CanSend,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    /// <summary>Whether it is in force today — a dated grant can sit on the list before or after its window.</summary>
    bool IsActive);

/// <summary>Someone the caller may currently act for, and what they may do in that seat.</summary>
public sealed record RemsActingForView(
    Guid PrincipalUserId,
    string PrincipalName,
    bool CanPrepare,
    bool CanSend);
