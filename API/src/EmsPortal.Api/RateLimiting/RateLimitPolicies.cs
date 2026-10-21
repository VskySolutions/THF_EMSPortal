namespace EmsPortal.Api.RateLimiting;

/// <summary>The names endpoints attach a rate limit by; each is bound from Limits:RateLimiting in appsettings.json.</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string PasswordReset = "password-reset";
    public const string PublicFormRead = "public-form-read";
    public const string PublicFormWrite = "public-form-write";
    public const string Email = "email";
    public const string MaconomySearch = "maconomy-search";
    public const string PublicContent = "public-content";
    public const string Upload = "upload";
}

/// <summary>The names endpoints attach a request timeout by; the default policy covers everything else.</summary>
public static class RequestTimeoutPolicies
{
    public const string LongRunning = "long-running";
}
