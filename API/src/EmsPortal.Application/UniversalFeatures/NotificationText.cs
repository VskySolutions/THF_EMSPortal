using System.Net;
using System.Text.RegularExpressions;

namespace EmsPortal.Application.UniversalFeatures;

/// <summary>
/// A notification is read as plain text, and what goes into one (a conversation message, a decline or
/// send-back reason) is often rich text: the markup is taken out before it is stored.
/// </summary>
public static partial class NotificationText
{
    /// <summary>The visible text of <paramref name="value"/>: tags dropped, entities decoded, whitespace collapsed.</summary>
    public static string Plain(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A tag becomes a space so "<p>a</p><p>b</p>" reads "a b", not "ab".
        var text = WebUtility.HtmlDecode(Tag().Replace(value, " "));
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>|</?[a-zA-Z][^>]*>|<!--.*?-->", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
