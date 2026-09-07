using EmsPortal.Api.Models.Media;
using EmsPortal.Api.Security;
using EmsPortal.Api.Storage;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Storage;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainMedia = EmsPortal.Domain.Entities.Media;
using EntityTypeEnum = EmsPortal.Domain.Enums.EntityType;
using MediaTypeEnum = EmsPortal.Domain.Enums.MediaType;
using MediaCategoryEnum = EmsPortal.Domain.Enums.MediaCategory;

namespace EmsPortal.Api.Controllers;

/// <summary>Centralized media upload/serving (WO-61).</summary>
[ApiController]
[Produces("application/json")]
[Tags("Media")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class MediaController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IMediaRepository _media;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _fileStorage;
    private readonly IUploadRecordKeyResolver _recordKeys;
    private readonly IPersonRepository _persons;

    public MediaController(
        IMediaRepository media,
        IUnitOfWork unitOfWork,
        IFileStorage fileStorage,
        IUploadRecordKeyResolver recordKeys,
        IPersonRepository persons)
    {
        _media = media;
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _recordKeys = recordKeys;
        _persons = persons;
    }

    /// <summary>Uploads a file.</summary>
    [HttpPost("/api/media")]
    [Authorize]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType<ApiResponse<MediaResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? mediaCategory,
        [FromForm] EntityTypeEnum? entityType,
        [FromForm] Guid? entityId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "A non-empty file is required."));
        }
        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "File exceeds the 10 MB limit."));
        }

        var category = Enum.TryParse<MediaCategoryEnum>(mediaCategory, ignoreCase: true, out var cat) ? cat : MediaCategoryEnum.Profile;
        var purpose = StoragePaths.PurposeFor(category);
        var tenantId = User.GetActiveTenantId() ?? Guid.Empty;

        StorageLocation location;
        if (entityType is { } type && entityId is { } id && id != Guid.Empty)
        {
            if (!await CanFileUnderAsync(type, id, cancellationToken))
            {
                return Forbid();
            }

            var recordKey = await _recordKeys.ResolveAsync(type, id, cancellationToken);
            if (recordKey is null)
            {
                return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", $"No {type} record was found for '{id}'."));
            }

            location = StorageLocation.For(tenantId, type, recordKey, purpose);
        }
        else
        {
            location = StorageLocation.Unassigned(tenantId, purpose, DateTime.UtcNow);
        }

        StoredFile stored;
        await using (var stream = file.OpenReadStream())
        {
            stored = await _fileStorage.SaveAsync(location, file.FileName, stream, cancellationToken);
        }

        var extension = Path.GetExtension(file.FileName);
        var media = new DomainMedia
        {
            // The store's id becomes the row's id, which is what makes the short suffix on the stored
            // file name traceable back to this record from the server.
            Id = stored.FileId,
            MediaType = ResolveMediaType(file.ContentType),
            MediaCategory = category,
            OriginalFileName = file.FileName,
            StoredFileName = stored.StoredFileName,
            FileExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.'),
            MimeType = file.ContentType,
            FileSize = file.Length,
            StorageProvider = "Local",
            RelativePath = stored.RelativePath,
            PublicUrl = $"/api/media/{stored.FileId}/content",
            // Profile pictures must be viewable in <img> tags without an auth header.
            IsPublic = category == MediaCategoryEnum.Profile,
            IsProcessed = true,
        };
        await _media.AddAsync(media, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(
            new MediaResponse(media.Id, media.MediaType.ToString(), media.MediaCategory.ToString(),
                media.OriginalFileName, media.MimeType, media.FileSize, media.PublicUrl, media.Width, media.Height),
            "Media uploaded."));
    }

    [HttpGet("/api/media/{id:guid}/content")]
    [AllowAnonymous]
    public async Task<IActionResult> Content(Guid id, CancellationToken cancellationToken)
    {
        var media = await _media.GetByIdAsync(id, cancellationToken);
        if (media is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Media not found."));
        }

        // Non-public media requires an authenticated caller.
        if (!media.IsPublic && User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Authentication required."));
        }

        // Resolved through the store rather than by joining paths here, so rows written before the
        // structured tree (a bare "media-uploads/{guid}.png") and rows written after it read the same way.
        var stream = await _fileStorage.OpenAsync(media.RelativePath ?? string.Empty, cancellationToken);
        if (stream is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Media file not found."));
        }

        return File(stream, string.IsNullOrWhiteSpace(media.MimeType) ? "application/octet-stream" : media.MimeType);
    }

    /// <summary>Whether the caller may file a file under a record.</summary>
    private async Task<bool> CanFileUnderAsync(EntityTypeEnum entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (User.CanAccess(entityType))
        {
            return true;
        }

        return entityType is EntityTypeEnum.Person
            && User.GetUserId() is { } userId
            && (await _persons.GetByUserIdAsync(userId, cancellationToken))?.Id == entityId;
    }

    private static MediaTypeEnum ResolveMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return MediaTypeEnum.Document;
        }
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return MediaTypeEnum.Image;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MediaTypeEnum.Video;
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MediaTypeEnum.Audio;
        return MediaTypeEnum.Document;
    }
}
