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
}
