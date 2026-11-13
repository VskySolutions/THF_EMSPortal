using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of the First-Year Fee Estimate to the job's budget (user, 13 Nov 2026): one outlay line
/// on task FEE, quantity 1, the estimate as its billing price. A job made from a template already
/// carries that line (thmasd01, 13 Nov 2026), so the line is updated where it stands. Where the
/// template gave none the sync stops and says so: no line is added. The line's Activity Type
/// ("Amount") is Maconomy's own, read from the task's activity: it cannot be written.
/// </summary>
public static class MaconomyJobBudgetDataMapping
{
    public const string Container = "jobbudgets";
    public const string JobNumberField = "jobnumber";

    public const string LineTypeField = "linetype";
    public const string TaskField = "taskname";
    public const string QuantityField = "numberof";
    public const string PriceField = "billingpricecurrency";

    public const string LineType = "outlay";
    public const string Task = "FEE";

    public static readonly string[] CardFields = { JobNumberField };
    public static readonly string[] RowFields = { LineTypeField, TaskField, QuantityField, PriceField };
    public static readonly string[] NumberFields = { QuantityField, PriceField };

    /// <summary>The budget line for the job, or null when the request names no fee estimate: the template's line then stands.</summary>
    public static MaconomyTableRowSet? Map(RemsRequest request, string jobNumber)
        => request.FirstYearFeeEstimate is not { } fee
            ? null
            : new MaconomyTableRowSet(
                Container, JobNumberField, jobNumber.Trim(), CardFields, RowFields,
                Match: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [LineTypeField] = LineType, [TaskField] = Task },
                Values: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [QuantityField] = "1",
                    [PriceField] = MaconomyJobDataMapping.AmountOf(fee),
                },
                NumberFields);
}
