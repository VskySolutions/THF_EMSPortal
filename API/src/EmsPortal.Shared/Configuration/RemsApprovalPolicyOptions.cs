namespace EmsPortal.Shared.Configuration;

/// <summary>
/// STATIC-APPROVAL-POLICY. The fixed REMS approval route THF asked for, bound from
/// <c>Rems:ApprovalPolicy</c>. Set <see cref="StaticRouting"/> false to fall back to the single
/// parallel round the platform routes on its own; every other switch is then ignored.
/// </summary>
public sealed class RemsApprovalPolicyOptions
{
    /// <summary>Stage the approvals — commission, then CSE, then department director, then the Shareholder role.</summary>
    public bool StaticRouting { get; set; } = true;

    /// <summary>
    /// Who signs as the shareholders, as "First Last", matched against the user's person record. When
    /// none of the names resolves to an active user, everyone holding the Shareholder role is asked instead.
    /// </summary>
    public List<string> MandatoryShareholders { get; set; } = new() { "Jeff Barbacci" };

    /// <summary>CSEs whose TAX engagements skip the department director and always need the shareholders.</summary>
    public List<string> TaxExceptionCses { get; set; } = new() { "James Previte", "Stephen Hamic" };

    /// <summary>A tax engagement whose first-year fee is at or under this skips the shareholders.</summary>
    public decimal TaxFeeCeilingWithoutShareholder { get; set; } = 10000m;
}
