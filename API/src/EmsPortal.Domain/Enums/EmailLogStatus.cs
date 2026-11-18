namespace EmsPortal.Domain.Enums;

/// <summary>How a send ended. Stored by name.</summary>
public enum EmailLogStatus
{
    /// <summary>The mail server accepted it. Not proof it reached the inbox: plain SMTP reports no more.</summary>
    Sent,

    /// <summary>The mail server refused it, or the portal could not write or send it.</summary>
    Failed,

    /// <summary>Held back on purpose: notifications are turned off, or the tenant has no active account.</summary>
    Skipped,
}
