namespace EmsPortal.Shared.Configuration;

/// <summary>
/// STATIC-APPROVAL-POLICY. The fixed REMS approval route THF asked for, bound from
/// <c>Rems:ApprovalPolicy</c>. Set <see cref="StaticRouting"/> false to fall back to the single
/// parallel round the platform routes on its own; every other switch is then ignored.
/// </summary>
public sealed class RemsApprovalPolicyOptions
{
    /// <summary>Apply THF's fixed rules for who is asked: commission recipients, CSE, department director, managing shareholder.</summary>
    public bool StaticRouting { get; set; } = true;

    /// <summary>CSEs whose TAX engagements skip the department director and always need the managing shareholder.</summary>
    public List<string> TaxExceptionCses { get; set; } = new() { "James Previte", "Stephen Hamic" };

    /// <summary>A tax engagement whose first-year fee is at or under this skips the managing shareholder.</summary>
    public decimal TaxFeeCeilingWithoutShareholder { get; set; } = 10000m;
}
