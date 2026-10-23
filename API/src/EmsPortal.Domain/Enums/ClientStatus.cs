namespace EmsPortal.Domain.Enums;

/// <summary>
/// What a <see cref="Entities.Client"/> is to the firm. Set by the REMS workflow, not edited as a step:
/// Prospect from creation, Active once a request for the client is approved, Inactive by hand.
/// </summary>
public enum ClientStatus
{
    Prospect,
    Active,
    Inactive,
}
