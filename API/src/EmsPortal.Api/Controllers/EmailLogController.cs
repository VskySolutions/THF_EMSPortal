using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Email;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// The Email Log: every email the tenant's portal tried to send, read-only. The tenant is the caller's
/// active one, so a Super Admin viewing another tenant reads that tenant's log.
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Email Log")]
public sealed class EmailLogController : ControllerBase
{
    // A transparent 1x1 GIF.
    private static readonly byte[] Pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    private readonly IEmailLogRepository _logs;

    public EmailLogController(IEmailLogRepository logs)
    {
        _logs = logs;
    }

    [HttpGet("/api/admin/email-logs")]
    [Authorize]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<EmailLogRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? templateKey,
        [FromQuery] bool? opened,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var filter = new EmailLogFilter(
            search,
            Enum.TryParse<EmailLogStatus>(status, ignoreCase: true, out var parsedStatus) ? parsedStatus : null,
            Enum.TryParse<EmailTemplateKey>(templateKey, ignoreCase: true, out var parsedKey) ? parsedKey : null,
            opened,
            from,
            // "to" is a day picked on a calendar: the whole of it is meant.
            to?.Date.AddDays(1));
        var (rows, total) = await _logs.ListAsync(tenantId, filter, page, limit, cancellationToken);
        return Ok(ApiResponseFactory.Paginated(rows.Select(ToRow), "Email log retrieved.", page, limit, total));
    }

    [HttpGet("/api/admin/email-logs/{id:guid}")]
    [Authorize]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<EmailLogDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var log = User.GetActiveTenantId() is { } tenantId ? await _logs.GetAsync(tenantId, id, cancellationToken) : null;
        if (log is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Email not found."));
        }
        return Ok(ApiResponseFactory.Success(new EmailLogDetail(ToRow(log), log.Body), "Email retrieved."));
    }

    /// <summary>
    /// The tracking image inside a sent email. Anonymous by nature, and it always answers with the image:
    /// whether the id was real is nobody's business but the log's.
    /// </summary>
    [HttpGet("/api/email-logs/{id:guid}/open.gif")]
    [AllowAnonymous]
    public async Task<IActionResult> Open(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _logs.MarkOpenedAsync(id, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception)
        {
            // The reader's mail client still gets its image.
        }
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return File(Pixel, "image/gif");
    }

    private static EmailLogRow ToRow(EmailLog log) => new(
        log.Id,
        log.TemplateKey.ToString(),
        DefaultEmailTemplates.For(log.TemplateKey).DisplayName,
        log.RecipientEmail,
        log.Subject,
        log.Status.ToString(),
        log.FailureReason,
        log.CreatedOnUtc,
        log.FirstOpenedOnUtc,
        log.LastOpenedOnUtc,
        log.OpenCount);
}

public sealed record EmailLogRow(
    Guid Id,
    string TemplateKey,
    string TemplateName,
    string RecipientEmail,
    string Subject,
    string Status,
    string? FailureReason,
    DateTime SentOnUtc,
    DateTime? FirstOpenedOnUtc,
    DateTime? LastOpenedOnUtc,
    int OpenCount);

public sealed record EmailLogDetail(EmailLogRow Email, string? Body);
