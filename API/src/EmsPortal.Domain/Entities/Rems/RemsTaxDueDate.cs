using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// One due date of a tax engagement. Every computed date is a row, so an approved request keeps its dates
/// if the calculation changes later; an edited date is the same row with <see cref="IsOverridden"/> set.
/// </summary>
public class RemsTaxDueDate : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public RemsTaxDueDateKind Kind { get; set; }
    public DateOnly DueOn { get; set; }
    public bool IsOverridden { get; set; }
    public Guid? OverriddenByUserId { get; set; }

    public RemsRequest? Request { get; set; }
}
