using EmsPortal.Domain.Entities;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// A user's name as the lists show it, in a shape SQL can order on: "First Last" off the person, else the
/// display name — the spelling <see cref="UserRepository.GetFullNamesAsync"/> resolves to, minus its last
/// resort of the email.
/// <para>
/// Every list column that holds a user — Assigned Admin, CSE, Created By, Updated By, Deleted By — is an
/// id the controller resolves to a name AFTER the query, so the name is not a column to order on.
/// Ordering by one of those columns is a correlated subquery over this projection instead, which keeps
/// the order in step with the name the cell shows.
/// </para>
/// </summary>
internal static class ActorNames
{
    public static IQueryable<ActorName> Of(EmsPortalDbContext db) => db.Users
        .Select(u => new ActorName
        {
            Id = u.Id,
            Name = u.Person != null ? u.Person.FirstName + " " + u.Person.LastName : u.DisplayName,
        });
}

/// <summary>
/// One row of <see cref="ActorNames.Of"/>. A class with settable members rather than a record: EF composes
/// a Where/Select over a member-initialised projection, and cannot over a constructor call.
/// </summary>
internal sealed class ActorName
{
    public Guid Id { get; set; }

    public string? Name { get; set; }
}
