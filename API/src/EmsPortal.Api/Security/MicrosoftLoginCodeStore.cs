using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EmsPortal.Api.Security;

/// <summary>
/// The one-time codes that carry a verified Microsoft sign-in from the API's callback to the web app. The
/// browser is redirected to the SPA with the code in the URL, and the SPA posts it back within
/// <see cref="Lifetime"/> to receive the token pair. A code leaves the store the moment it is redeemed, so a URL
/// that leaks (history, logs) is worthless after its first use.
/// In-process by design: the API is one host. A second instance would need this moved to the database.
/// </summary>
public sealed class MicrosoftLoginCodeStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, (Guid UserId, DateTime ExpiresAt)> _codes = new(StringComparer.Ordinal);

    /// <summary>A new code bound to <paramref name="userId"/>.</summary>
    public string Issue(Guid userId)
    {
        Prune();
        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _codes[code] = (userId, DateTime.UtcNow.Add(Lifetime));
        return code;
    }

    /// <summary>Takes the code out of the store; true only on the first, in-time redemption.</summary>
    public bool TryRedeem(string code, out Guid userId)
    {
        userId = Guid.Empty;
        if (string.IsNullOrEmpty(code) || !_codes.TryRemove(code, out var entry) || entry.ExpiresAt <= DateTime.UtcNow)
        {
            return false;
        }

        userId = entry.UserId;
        return true;
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var (code, entry) in _codes)
        {
            if (entry.ExpiresAt <= now)
            {
                _codes.TryRemove(code, out _);
            }
        }
    }
}
