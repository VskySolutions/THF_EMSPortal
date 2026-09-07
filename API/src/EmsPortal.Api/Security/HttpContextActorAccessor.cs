using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Shared.Security;

namespace EmsPortal.Api.Security;

/// <summary>
/// Resolves the audit actor from the authenticated user on the current HTTP request, falling back to
/// the system identity when no user is present (REQ-INF-009 AC-2).
/// </summary>
public sealed class HttpContextActorAccessor : IActorAccessor
{
    private const string SystemIdentity = "system";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentActor()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return SystemIdentity;
        }

        return user.FindFirst(ClaimTypeNames.Subject)?.Value
            ?? user.Identity.Name
            ?? SystemIdentity;
    }
}
