namespace EmsPortal.Domain.Entities;

/// <summary>
/// What Maconomy says about a customer right now: the name as Maconomy writes it, the REMS entity type
/// its specification 6 stands for, and the email and telephone on the customer card. Read by customer
/// number and held in memory only — the portal stores the number and nothing else. The industry is the
/// REMS.Industry code its specification 1 stands for, null when it stands for none.
/// </summary>
public sealed record MaconomyClientDetails(
    string Number, string Name, string? EntityType, string? Email, string? Phone, string? Industry = null);
