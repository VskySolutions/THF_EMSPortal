using EmsPortal.Api.Validators.Rems;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// The entity type a client's RECORD holds — what a request for a client already on file is filed under,
/// as opposed to the type any one request happens to carry.
/// </summary>
internal static class RemsClientEntityType
{
    public static bool IsIndividual(string? code)
        => string.Equals(code, RemsFormPayloadValidator.Individual, StringComparison.Ordinal);

    /// <summary>
    /// A person is always an Individual. An organisation holds the type it was last filed under; one
    /// filed as an Individual contradicts its own record, so it reads as holding none.
    /// </summary>
    public static string? Of(Person client, string? lastFiledUnder)
    {
        if (!client.IsOrganisation)
        {
            return RemsFormPayloadValidator.Individual;
        }

        return IsIndividual(lastFiledUnder) ? null : lastFiledUnder;
    }
}
