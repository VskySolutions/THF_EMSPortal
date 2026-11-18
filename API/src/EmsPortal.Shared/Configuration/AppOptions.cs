namespace EmsPortal.Shared.Configuration;

/// <summary>
/// General application settings. <see cref="BaseUrl"/> is the public SPA root used to build links in
/// outgoing emails (e.g. the login link in invitation/reset templates).
/// </summary>
public sealed class AppOptions
{
    /// <summary>Public base URL of the web application (no trailing slash), e.g. <c>https://app.example.com</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where this API is reached from outside, for the tracking image inside a sent email. Empty: no
    /// image is added, and the Email Log shows no opens.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
}
