using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// A routing of a request to its approvers, read off the tasks that share a <see cref="RemsApprovalTask.RoundNumber"/>.
/// Rounds are not stored: who sent it and when are the tasks' audit columns, the outcome follows from the
/// decisions, and the rejection reason sits on the task that rejected.
/// </summary>
public sealed record RemsApprovalRoundSummary(
    int RoundNumber,
    RemsApprovalRoundStatus Status,
    DateTime SentOnUtc,
    Guid? SentByUserId,
    DateTime? CompletedOnUtc,
    string? RejectionReason,
    IReadOnlyList<RemsApprovalTask> Tasks)
{
    public int DeclineCount => Tasks.Count(t => t.Status == RemsApprovalTaskStatus.Rejected);

    /// <summary>A stable id for callers that still speak in round ids: the round's first task, by id.</summary>
    public Guid Id => Tasks.OrderBy(t => t.CreatedOnUtc).ThenBy(t => t.Id).First().Id;
}

public static class RemsApprovalRounds
{
    /// <summary>Every round of a request, latest first.</summary>
    public static IReadOnlyList<RemsApprovalRoundSummary> Summarize(IEnumerable<RemsApprovalTask> tasks, int declineThreshold = 1)
        => tasks
            .Where(t => !t.Deleted)
            .GroupBy(t => t.RoundNumber)
            .OrderByDescending(g => g.Key)
            .Select(g => Summarize(g.Key, g.ToList(), declineThreshold))
            .ToList();

    /// <summary>The round the request is currently on, or null when it has never been routed.</summary>
    public static RemsApprovalRoundSummary? Current(IEnumerable<RemsApprovalTask> tasks, int declineThreshold = 1)
        => Summarize(tasks, declineThreshold).FirstOrDefault();

    public static RemsApprovalRoundStatus StatusOf(IReadOnlyCollection<RemsApprovalTask> round, int declineThreshold = 1)
    {
        if (round.Count == 0)
        {
            return RemsApprovalRoundStatus.Pending;
        }
        var declines = round.Count(t => t.Status == RemsApprovalTaskStatus.Rejected);
        if (declines >= Math.Max(1, declineThreshold))
        {
            return RemsApprovalRoundStatus.Rejected;
        }
        return round.All(t => t.Status == RemsApprovalTaskStatus.Approved)
            ? RemsApprovalRoundStatus.Approved
            : RemsApprovalRoundStatus.Pending;
    }

    private static RemsApprovalRoundSummary Summarize(int roundNumber, List<RemsApprovalTask> round, int declineThreshold)
    {
        var status = StatusOf(round, declineThreshold);
        var first = round.OrderBy(t => t.CreatedOnUtc).ThenBy(t => t.Id).First();
        var rejecting = round.Where(t => t.Status == RemsApprovalTaskStatus.Rejected).OrderBy(t => t.DecidedOnUtc).FirstOrDefault();
        DateTime? completed = status switch
        {
            RemsApprovalRoundStatus.Rejected => rejecting?.DecidedOnUtc,
            RemsApprovalRoundStatus.Approved => round.Max(t => t.DecidedOnUtc),
            _ => null,
        };
        return new RemsApprovalRoundSummary(
            roundNumber, status, first.CreatedOnUtc, first.CreatedById, completed, rejecting?.RejectionReason,
            round.OrderBy(t => t.CreatedOnUtc).ThenBy(t => t.Id).ToList());
    }
}
