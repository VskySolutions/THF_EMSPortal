using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Api.Storage;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Storage;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Attachments — files uploaded against any entity record via the shared <c>(EntityType,
/// EntityId)</c> key.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/uf/attachments")]
[Produces("application/json")]
[Tags("Universal Features — Attachments")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class AttachmentsController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB default

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".rtf", ".md", ".json", ".xml", ".zip",
    };

    private readonly IAttachmentRepository _attachments;
    private readonly IFileStorage _fileStorage;
    private readonly IUploadRecordKeyResolver _recordKeys;
    private readonly IUserRepository _users;
    private readonly IActivityEventWriter _activity;
    private readonly IUnitOfWork _unitOfWork;

    public AttachmentsController(
        IAttachmentRepository attachments,
        IFileStorage fileStorage,
        IUploadRecordKeyResolver recordKeys,
        IUserRepository users,
        IActivityEventWriter activity,
        IUnitOfWork unitOfWork)
    {
        _attachments = attachments;
        _fileStorage = fileStorage;
        _recordKeys = recordKeys;
        _users = users;
        _activity = activity;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<AttachmentResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] EntityType entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var attachments = await _attachments.ListAsync(entityType, entityId, cancellationToken);
        var uploaderNames = await _users.GetFullNamesAsync(
            attachments.Where(a => a.CreatedById.HasValue).Select(a => a.CreatedById!.Value), cancellationToken);
        var data = attachments.Select(a => ToResponse(a, uploaderNames));
        return Ok(ApiResponseFactory.Success(data, "Attachments retrieved."));
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType<ApiResponse<AttachmentResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile? file,
        [FromForm] EntityType entityType,
        [FromForm] Guid entityId,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "A non-empty file is required."));
        }
        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "File exceeds the 10 MB limit."));
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", $"File type '{extension}' is not allowed."));
        }

        // The same tree every other module uploads into: an attachment on REMS-42 sits beside the
        // files the REMS screens put there, rather than in a bucket of its own.
        var recordKey = await _recordKeys.ResolveAsync(entityType, entityId, cancellationToken);
        if (recordKey is null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", $"No {entityType} record was found for '{entityId}'."));
        }

        var location = StorageLocation.For(
            User.GetActiveTenantId() ?? Guid.Empty, entityType, recordKey, StoragePaths.Purposes.Attachments);

        StoredFile stored;
        await using (var stream = file.OpenReadStream())
        {
            stored = await _fileStorage.SaveAsync(location, file.FileName, stream, cancellationToken);
        }

        var attachment = new Attachment
        {
            Id = stored.FileId,
            EntityType = entityType,
            EntityId = entityId,
            FileName = file.FileName,
            StoredPath = stored.RelativePath,
            MimeType = file.ContentType,
            FileSize = file.Length,
            FileExtension = extension.ToLowerInvariant(),
        };
        await _attachments.AddAsync(attachment, cancellationToken);
        await _activity.WriteAsync(new CreateActivityEventDto(entityType, entityId, ActivityEventTypes.AttachmentUploaded, NewValue: file.FileName), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var names = await ResolveUploaderAsync(attachment.CreatedById, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(attachment, names), "Attachment uploaded."));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await _attachments.GetByIdAsync(id, cancellationToken);
        if (attachment is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Attachment not found."));
        }
        if (!User.CanAccess(attachment.EntityType))
        {
            return Forbid();
        }

        var stream = await _fileStorage.OpenAsync(attachment.StoredPath, cancellationToken);
        if (stream is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Attachment file not found."));
        }

        return File(stream, string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType, attachment.FileName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await _attachments.GetByIdAsync(id, cancellationToken);
        if (attachment is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Attachment not found."));
        }

        var isAdmin = User.IsSuperAdmin() || User.HasPermission(Permissions.SettingsManage);
        if (attachment.CreatedById != User.GetUserId() && !isAdmin)
        {
            return Forbid();
        }

        // Record the activity event (committed) before permanently removing the row + file.
        await _activity.WriteAsync(new CreateActivityEventDto(attachment.EntityType, attachment.EntityId, ActivityEventTypes.AttachmentDeleted, OldValue: attachment.FileName), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _attachments.HardDeleteAsync(id, cancellationToken);
        await _fileStorage.DeleteAsync(attachment.StoredPath, cancellationToken);

        return Ok(ApiResponseFactory.Success(new { attachmentId = id }, "Attachment deleted."));
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveUploaderAsync(Guid? uploaderId, CancellationToken cancellationToken)
        => uploaderId is { } id
            ? await _users.GetFullNamesAsync(new[] { id }, cancellationToken)
            : new Dictionary<Guid, string>();

    private static AttachmentResponse ToResponse(Attachment attachment, IReadOnlyDictionary<Guid, string> uploaderNames)
        => new(
            attachment.Id,
            attachment.EntityType,
            attachment.EntityId,
            attachment.FileName,
            attachment.MimeType,
            attachment.FileSize,
            attachment.FileExtension,
            attachment.CreatedById,
            attachment.CreatedById is { } id && uploaderNames.TryGetValue(id, out var name) ? name : null,
            attachment.CreatedOnUtc);
}
