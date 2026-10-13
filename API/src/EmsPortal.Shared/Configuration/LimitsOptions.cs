namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Every ceiling the API runs under, bound from <c>Limits</c> in appsettings.json so each can be changed on the
/// server after a publish (a restart applies it). The defaults are what the platform ships with.
/// </summary>
public sealed class LimitsOptions
{
    public RateLimitingOptions RateLimiting { get; set; } = new();

    public PagingLimits Paging { get; set; } = new();

    public UploadLimits Uploads { get; set; } = new();

    public TimeoutLimits Timeouts { get; set; } = new();
}

/// <summary>
/// How often one caller may hit the API. A caller is the signed-in user or API key; anybody else is counted by
/// address. A policy with <c>PermitLimit</c> 0 is switched off on its own.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Off, every policy below admits everything.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Every request, whatever the endpoint.</summary>
    public RateLimitWindow General { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    /// <summary>Password sign-in and token refresh, by address.</summary>
    public RateLimitWindow Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Forgot-password and reset-password, by address.</summary>
    public RateLimitWindow PasswordReset { get; set; } = new() { PermitLimit = 5, WindowSeconds = 900 };

    /// <summary>Reading or drafting the public REMS form, by address.</summary>
    public RateLimitWindow PublicFormRead { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>Reviewing, submitting or cancelling the public REMS form, by address.</summary>
    public RateLimitWindow PublicFormWrite { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Actions that send email: the form link, a reminder, an approval round, a send-back, an SMTP test.</summary>
    public RateLimitWindow Email { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Maconomy customer lookups.</summary>
    public RateLimitWindow MaconomySearch { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>The email-events webhook and public media and branding reads, by address.</summary>
    public RateLimitWindow PublicContent { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    /// <summary>Uploads one caller may have in flight at once. 0 switches the cap off.</summary>
    public int UploadConcurrency { get; set; } = 2;
}

/// <summary>So many requests per so many seconds, counted over a sliding window.</summary>
public sealed class RateLimitWindow
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; } = 60;

    /// <summary>Requests held back and let through as the window frees up, instead of refused outright.</summary>
    public int QueueLimit { get; set; }
}

/// <summary>Row ceilings.</summary>
public sealed class PagingLimits
{
    /// <summary>The most rows any one request returns, whether a page of a list or a whole list.</summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>The most ids one bulk call may name.</summary>
    public int MaxBulkIds { get; set; } = 1000;
}

/// <summary>Size ceilings, in MB so they read naturally when edited by hand.</summary>
public sealed class UploadLimits
{
    private const long Mb = 1024 * 1024;

    /// <summary>Attachments and media files.</summary>
    public int MaxFileMb { get; set; } = 10;

    /// <summary>Branding logos and icons.</summary>
    public int BrandingImageMb { get; set; } = 2;

    /// <summary>The branding login background.</summary>
    public int BrandingBackgroundMb { get; set; } = 5;

    /// <summary>Any request body at all. IIS has its own ceiling in web.config, which must sit above this one.</summary>
    public int MaxRequestBodyMb { get; set; } = 30;

    public long MaxFileBytes => MaxFileMb * Mb;

    public long BrandingImageBytes => BrandingImageMb * Mb;

    public long BrandingBackgroundBytes => BrandingBackgroundMb * Mb;

    public long MaxRequestBodyBytes => MaxRequestBodyMb * Mb;
}

/// <summary>How long a request may run before the server stops waiting for it.</summary>
public sealed class TimeoutLimits
{
    /// <summary>Seconds for an ordinary request.</summary>
    public int DefaultSeconds { get; set; } = 30;

    /// <summary>Seconds for uploads and exports.</summary>
    public int LongRunningSeconds { get; set; } = 120;
}
