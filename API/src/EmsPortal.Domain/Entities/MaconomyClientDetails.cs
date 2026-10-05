namespace EmsPortal.Domain.Entities;

/// <summary>
/// What Maconomy says about a customer right now: the name as Maconomy writes it, the REMS entity type
/// its specification 6 stands for, and the email and telephone on the customer card. Read by customer
/// number and held in memory only — the portal stores the number and nothing else.
/// </summary>
public sealed record MaconomyClientDetails(
    string Number, string Name, string? EntityType, string? Email, string? Phone);
