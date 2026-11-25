namespace EmsPortal.Domain.Enums;

/// <summary>
/// The role an approver plays on a REMS approval task (REMS WO-110). Stored as a string in the
/// database (<c>HasConversion&lt;string&gt;</c>), so renaming a member needs a data migration.
/// </summary>
public enum RemsApproverRole
{
    /// <summary>Client Service Executive.</summary>
    CSE,

    /// <summary>Director of the owning department.</summary>
    DepartmentDirector,

    /// <summary>A recipient of a commission split who must approve their share.</summary>
    CommissionRecipient,

    /// <summary>
    /// A holder of the <c>Managing Shareholder</c> role: on every engagement's approver list by standing
    /// and not removable from it. Reviews on the same terms as a plain <see cref="Approver"/>; the fee
    /// estimate and realization stay with the Department Director.
    /// </summary>
    ManagingShareholder,

    /// <summary>
    /// A hand-picked approver with no standing of their own on the engagement — someone added on the
    /// Approval tab who is not the managing shareholder, the CSE, the department director or a commission
    /// recipient. Reviews the engagement without seeing the fee estimate or realization, which stay
    /// reserved to the director.
    /// </summary>
    Approver,
}
