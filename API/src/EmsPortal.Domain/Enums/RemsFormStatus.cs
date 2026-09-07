namespace EmsPortal.Domain.Enums;

/// <summary>
/// Lifecycle of a customer-facing REMS onboarding form (REMS WO-110). Stored as a string in the
/// database (<c>HasConversion&lt;string&gt;</c>) so the value stays readable and stable across renames.
/// </summary>
public enum RemsFormStatus
{
    /// <summary>Created but not yet fully prepared (CSE/EntityType not set).</summary>
    Draft,

    /// <summary>Prepared and saved by staff, ready to send.</summary>
    Saved,

    /// <summary>Emailed to the customer; the invite code is locked.</summary>
    Sent,

    /// <summary>The customer has submitted their responses.</summary>
    Submitted,

    /// <summary>Cancelled before completion.</summary>
    Cancelled,
}
