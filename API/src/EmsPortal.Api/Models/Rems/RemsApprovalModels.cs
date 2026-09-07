using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

// ---------------------------------------------------------------------------------------------------
// WO-114 — REMS approval workflow (Part C): the live suggested approver list, the caller's own tasks, the
// role-scoped task view.

/// <summary>One suggested approver on the live list (AC-REMS-018): the user and the role they would act in.</summary>
// STATIC-APPROVAL-POLICY: Stage/StageName say when they are asked; 1 and null on an unstaged round.
public sealed record RemsApproverSuggestion(RemsUserRef User, string Role, int Stage = 1, string? StageName = null);

/// <summary>
/// The full approver list an engagement will route to (updates until the round is sent): the automatic
/// approvers — the firm's shareholders, the Department Director.
/// </summary>
public sealed record RemsApproverList(
    Guid EngagementId,
    string EngagementStatus,
    IReadOnlyList<RemsApproverSuggestion> Approvers,
    IReadOnlyList<Guid> SelectedApproverIds,
    // STATIC-APPROVAL-POLICY: whether the route is staged, who the seats reserve, and why it cannot go yet.
    bool StaticRouting = false,
    IReadOnlyList<Guid>? ReservedApproverIds = null,
    string? BlockedReason = null);

/// <summary>STATIC-APPROVAL-POLICY. The fixed route as it resolves in this tenant.</summary>
public sealed record RemsApprovalPolicyView(
    bool StaticRouting,
    RemsUserRef? ManagingShareholder,
    IReadOnlyList<RemsUserRef> TaxExceptionCses);

/// <summary>A user selectable as an extra approver: any active user in the tenant (there is no Approver role).</summary>
public sealed record RemsApproverOption(
    Guid UserId, string Name, string? Email, IReadOnlyList<string> Roles);

/// <summary>Replaces the engagement's ADDED approvers with exactly these users (AC-REMS-018).</summary>
public sealed class SetRemsApproversRequest
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>
/// One REQUEST in the caller's approvals inbox, carried by their task on its latest round — so <see
/// cref="Role"/>, <see cref="Status"/> and <see cref="RoundNumber"/> say what they are to it now.
/// </summary>
public sealed record RemsApprovalTaskRow(
    Guid TaskId,
    Guid RoundId,
    int RoundNumber,
    string Role,
    string Status,
    DateTime SentOnUtc,
    DateTime? DecidedOnUtc,
    string RoundStatus,
    Guid EngagementId,
    Guid RemsId,
    string RemsNumber,
    /// <summary>The client's name as it reads — the suffix after the name they gave on intake.</summary>
    string ClientName,
    /// <summary>The generational particle, so the cell can draw it in bold at the end of that name.</summary>
    string? ClientNameSuffix,
    /// <summary>The request's Client Service Executive.</summary>
    RemsUserRef? Cse,
    // No EntityName: an approval is about a request and its single engagement now, so the entity's name
    // only ever repeated the client's.
    int ApprovedCount,
    int RejectedCount,
    int ApproverCount,
    // The TASK's own audit trail — the row is keyed on it — offered as hidden-by-default columns.
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

/// <summary>A checklist line on an approval task.</summary>
public sealed record RemsChecklistItemView(Guid Id, int DisplayOrder, string Label, bool IsCompleted, DateTime? CompletedOnUtc);

/// <summary>
/// The approval-task review packet: the task, its checklist, and the complete case an approver decides
/// on — the originating REMS request, the client, the entity under review.
/// </summary>
public sealed record RemsApprovalTaskView(
    Guid TaskId,
    Guid RoundId,
    int RoundNumber,
    string Role,
    string Status,
    DateTime? DecidedOnUtc,
    string? RejectionReason,
    bool CanDecide,
    IReadOnlyList<RemsChecklistItemView> Checklist,
    RemsApprovalRequestView Request,
    RemsApprovalEngagementView Engagement,
    RemsApprovalRoundView Round,
    /// <summary>The TASK's own provenance — the packet is keyed on it — as every detail page ends with.</summary>
    RecordAudit Audit);

/// <summary>
/// The originating REMS request as an approver sees it: the intake fields the partner raised, who is
/// on it, and the attachments that came with it.
/// </summary>
public sealed record RemsApprovalRequestView(
    Guid RemsId,
    string RemsNumber,
    string? Description,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an
    /// organisation.
    /// </summary>
    string ClientName,
    /// <summary>The particle, so the packet can draw it in bold at the end of that name.</summary>
    string? ClientNameSuffix,
    string Type,
    string Status,
    string? CustomerEmail,
    string? CustomerMobileNumber,
    string? EntityType,
    string EmsFormState,
    string? ClientSubmissionState,
    RemsUserRef? AssignedAdmin,
    RemsUserRef? Cse,
    string? RequestedBy,
    DateTime CreatedOnUtc,
    IReadOnlyList<RemsFileRef> Files);

/// <summary>The engagement under review, mirroring the workspace's Setup / Marketing / Commission tabs.</summary>
public sealed record RemsApprovalEngagementView(
    Guid EngagementId,
    string Status,
    string? Department,
    string? ServiceLine,
    string? Industry,
    RemsApprovalClientView Client,
    RemsApprovalEntityView Entity,
    RemsUserRef? DepartmentDirector,
    RemsUserRef? EngagementExecutive,
    RemsUserRef? BillingManager,
    decimal? FirstYearFeeEstimate,
    /// <summary>Assurance prices the engagement rather than its first year; only one of the two is ever set.</summary>
    decimal? EngagementFee,
    decimal? RealizationPercentage,
    bool FinancialsRestricted,
    /// <summary>
    /// How often the client is billed (option-set <c>REMS.BillingPeriod</c> code), and how that
    /// billing actually runs.
    /// </summary>
    string? BillingPeriod,
    string? BillingProcessDescription,
    RemsApprovalAuditDetailView? Audit,
    RemsGovernmentDetailView? Government,
    RemsApprovalTaxDetailView? Tax,
    IReadOnlyList<RemsApprovalOptionRef> MarketingMethods,
    IReadOnlyList<RemsCommissionSplitView> CommissionSplits);

/// <summary>One approval round as history.</summary>
public sealed record RemsApprovalTaskRef(Guid TaskId);

public sealed record RemsApprovalRoundHistory(
    Guid RoundId,
    int RoundNumber,
    string Status,
    DateTime SentOnUtc,
    string? SentBy,
    DateTime? CompletedOnUtc,
    // What it would have taken to close this round, and how close it got.
    int DeclineThreshold,
    int DeclineCount,
    IReadOnlyList<RemsApprovalRoundDecision> Decisions);

/// <summary>One approver's decision within a round, with the checklist they worked through.</summary>
public sealed record RemsApprovalRoundDecision(
    Guid TaskId,
    string Approver,
    string Role,
    string Status,
    DateTime? DecidedOnUtc,
    /// <summary>Their own reason for declining.</summary>
    string? Reason,
    int ChecklistCompleted,
    int ChecklistTotal,
    // STATIC-APPROVAL-POLICY
    int Stage = 1,
    string? StageName = null);

/// <summary>The client on the engagement, including the billing block the workspace's client card carries.</summary>
public sealed record RemsApprovalClientView(
    Guid Id,
    string Name,
    string Email,
    string? MobileNumber,
    string? ReferralSource,
    string? BillingContactName,
    string? BillingEmail,
    // The billing ADDRESS moved onto the entity, so it arrives with that entity's addresses rather than
    // separately here.
    IReadOnlyList<RemsApprovalEntitySummary> Entities);

/// <summary>A sibling entity of the same client, listed for context.</summary>
public sealed record RemsApprovalEntitySummary(Guid Id, string Name, string? Ein, bool IsMainEntity);

/// <summary>The entity this engagement belongs to, with the addresses and contacts from the submitted form.</summary>
public sealed record RemsApprovalEntityView(
    Guid Id,
    string Name,
    string? Ein,
    bool IsMainEntity,
    IReadOnlyList<RemsEntityAddressView> Addresses,
    IReadOnlyList<RemsEntityContactView> Contacts);

/// <summary>An option-set item resolved to its LABEL (and group, for marketing).</summary>
public sealed record RemsApprovalOptionRef(Guid Id, string Label, string? Group);

/// <summary>Audit detail with the signed client-acceptance form resolved to something openable.</summary>
public sealed record RemsApprovalAuditDetailView(
    Guid Id,
    Guid? ClientAcceptanceFormMediaId,
    string? FileName,
    string? Url,
    DateOnly? ClientFiscalYearEnd,
    bool? AdminFeesApply,
    decimal? AdminFeesAmount);

/// <summary>Tax detail with the due-date schedule deserialized and the form checklist resolved to labels.</summary>
public sealed record RemsApprovalTaxDetailView(
    Guid Id,
    DateOnly? FiscalYearEnd,
    /// <summary>Derived from the fiscal year end, then whatever was typed over it.</summary>
    RemsTaxDueDateSet? DueDates,
    IReadOnlyList<RemsApprovalOptionRef> TaxForms);

/// <summary>
/// The approval round this task belongs to, with every approver's decision — the approver-side
/// equivalent of the workspace's Approval tab.
/// </summary>
public sealed record RemsApprovalRoundView(
    Guid Id,
    int RoundNumber,
    string Status,
    DateTime SentOnUtc,
    RemsUserRef? SentBy,
    DateTime? CompletedOnUtc,
    string? RejectionReason,
    IReadOnlyList<RemsApprovalDecisionView> Decisions);

/// <summary>One approver's standing on the round.</summary>
public sealed record RemsApprovalDecisionView(
    Guid TaskId,
    RemsUserRef Approver,
    string Role,
    string Status,
    DateTime? DecidedOnUtc,
    string? RejectionReason,
    bool IsYou,
    // STATIC-APPROVAL-POLICY
    int Stage = 1,
    string? StageName = null);

/// <summary>Check / uncheck a checklist item on the caller's own task.</summary>
public sealed class SetChecklistItemRequest
{
    public bool IsCompleted { get; set; }
}

/// <summary>Reject the caller's own task with a required reason (AC-REMS-020.1).</summary>
public sealed class RejectApprovalTaskRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The per-role approval checklist labels (AC-REMS-019.4/5/6): CSE = 2, DepartmentDirector = 3,
/// CommissionRecipient = 2.
/// </summary>
public static class RemsApprovalChecklistCatalog
{
    private static readonly IReadOnlyList<string> Cse = new[]
    {
        "Client information reviewed and accurate",
        "Engagement scope and service line confirmed",
    };

    private static readonly IReadOnlyList<string> DepartmentDirector = new[]
    {
        "First-year fee estimate reviewed",
        "Realization percentage acceptable",
        "Engagement team and department placement appropriate",
    };

    private static readonly IReadOnlyList<string> CommissionRecipient = new[]
    {
        "Commission split reviewed",
        "Commission allocation accepted",
    };

    // An approver with no other standing on the engagement: a general review, since nothing narrower can be
    // assumed about why they are looking at it.
    private static readonly IReadOnlyList<string> Approver = new[]
    {
        "Engagement details reviewed",
        "Engagement accepted",
    };

    /// <summary>The ordered checklist labels for an approver role.</summary>
    public static IReadOnlyList<string> For(RemsApproverRole role) => role switch
    {
        RemsApproverRole.CSE => Cse,
        RemsApproverRole.DepartmentDirector => DepartmentDirector,
        RemsApproverRole.CommissionRecipient => CommissionRecipient,
        RemsApproverRole.Shareholder or RemsApproverRole.Approver => Approver,
        _ => Array.Empty<string>(),
    };
}
