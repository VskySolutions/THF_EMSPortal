using EmsPortal.Api.Models.Branding;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Storage;
using EmsPortal.Application.Branding;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainMedia = EmsPortal.Domain.Entities.Media;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// A tenant's branding: the name, logos, colours, type and button styling its copy of the application
/// wears. Every signed-in user reads their tenant's; changing it needs <c>branding.manage</c>. A Super
/// Admin may target another tenant with <c>?tenantId=</c>, as on the Maconomy connection. The anonymous
/// reads serve the screens drawn before anyone has signed in.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/branding")]
[Produces("application/json")]
[Tags("Branding")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class BrandingController : ControllerBase
{
    private const long MaxImageBytes = 2 * 1024 * 1024;
    private const long MaxBackgroundBytes = 5 * 1024 * 1024;

    private readonly ITenantBrandingRepository _branding;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IMediaRepository _media;
    private readonly IRemsFormRepository _remsForms;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public BrandingController(
        ITenantBrandingRepository branding,
        ITenantRepository tenants,
        IUserRepository users,
        IMediaRepository media,
        IRemsFormRepository remsForms,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _branding = branding;
        _tenants = tenants;
        _users = users;
        _media = media;
        _remsForms = remsForms;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    // ---- Reading ----

    /// <summary>The tenant's branding. Open to every signed-in user: it is what their screen is drawn with.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<BrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var branding = await _branding.GetByTenantAsync(tenant!.Id, cancellationToken);
        return Ok(ApiResponseFactory.Success(await ToResponseAsync(tenant, branding, cancellationToken), "Branding retrieved."));
    }

    /// <summary>
    /// Branding for the sign-in screen, by tenant identifier. An identifier that matches nothing answers
    /// with the stock look rather than a 404, so the endpoint cannot be used to learn which tenants exist.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<PublicBrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublic([FromQuery] string? tenant, CancellationToken cancellationToken)
    {
        var found = string.IsNullOrWhiteSpace(tenant) || tenant.Length > 100
            ? null
            : await _tenants.GetByIdentifierAsync(tenant.Trim(), cancellationToken);

        return Ok(ApiResponseFactory.Success(await ToPublicAsync(found, cancellationToken), "Branding retrieved."));
    }

    /// <summary>Branding for the public client form: the firm that sent the link is the one the client should see.</summary>
    [HttpGet("public/forms/{inviteCode}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<PublicBrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForPublicForm(string inviteCode, CancellationToken cancellationToken)
    {
        var form = await _remsForms.GetByInviteCodeUnscopedAsync(inviteCode?.Trim() ?? string.Empty, cancellationToken);
        var found = form is null ? null : await _tenants.GetByIdAsync(form.TenantId, cancellationToken);

        return Ok(ApiResponseFactory.Success(await ToPublicAsync(found, cancellationToken), "Branding retrieved."));
    }

    // ---- Changing ----

    /// <summary>
    /// Replaces the tenant's theme with <paramref name="theme"/>. Send the whole theme: a value left out
    /// goes back to the application's own. The images are not part of this — see the asset endpoints.
    /// </summary>
    [HttpPut]
    [RequirePermission(Permissions.BrandingManage)]
    [ProducesResponseType<ApiResponse<BrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(
        [FromQuery] Guid? tenantId, [FromBody] BrandingTheme theme, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var problems = BrandingThemeRules.Normalize(theme);
        if (problems.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", string.Join(" ", problems)));
        }

        var branding = await GetOrCreateAsync(tenant!.Id, cancellationToken);
        branding.ThemeJson = BrandingThemeRules.Serialize(theme);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(await ToResponseAsync(tenant, branding, cancellationToken), "Branding saved."));
    }

    /// <summary>Puts the tenant back on the stock look: theme and images both. Answers with what is now in effect.</summary>
    [HttpDelete]
    [RequirePermission(Permissions.BrandingManage)]
    [ProducesResponseType<ApiResponse<BrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reset([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var branding = await _branding.GetByTenantAsync(tenant!.Id, cancellationToken);
        if (branding is not null)
        {
            _branding.Remove(branding);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(await ToResponseAsync(tenant, null, cancellationToken), "Branding reset."));
    }

    /// <summary>
    /// Uploads the image for one slot (<c>logo</c>, <c>logoDark</c>, <c>logoMark</c>, <c>favicon</c>,
    /// <c>loginBackground</c>), replacing whatever was there. PNG, JPEG, WebP or ICO; 2 MB, or 5 MB for
    /// the sign-in background. SVG is refused: it is a document that can carry script, and these files are
    /// served to people who have not signed in.
    /// </summary>
    [HttpPost("assets/{slot}")]
    [RequirePermission(Permissions.BrandingManage)]
    [RequestSizeLimit(MaxBackgroundBytes + 64 * 1024)]
    [ProducesResponseType<ApiResponse<BrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadAsset(
        string slot, [FromQuery] Guid? tenantId, [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (!BrandingAssetSlots.All.Contains(slot))
        {
            return NotFound(ApiResponseFactory.NotFound($"There is no branding image called '{slot}'."));
        }

        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "A non-empty file is required."));
        }
        var limit = slot == BrandingAssetSlots.LoginBackground ? MaxBackgroundBytes : MaxImageBytes;
        if (file.Length > limit)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", $"The image exceeds the {limit / (1024 * 1024)} MB limit."));
        }

        await using var stream = file.OpenReadStream();

        // The type is read off the bytes. The name and the Content-Type header are the uploader's claim,
        // and this file is handed to anonymous browsers under whatever type is recorded here.
        var kind = await SniffImageAsync(stream, cancellationToken);
        if (kind is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "The file must be a PNG, JPEG, WebP or ICO image."));
        }

        var location = StorageLocation.For(tenant!.Id, EntityType.Tenant, tenant.Id.ToString("N"), "branding");
        var stored = await _fileStorage.SaveAsync(location, $"{slot}{kind.Value.Extension}", stream, cancellationToken);

        await _media.AddAsync(new DomainMedia
        {
            Id = stored.FileId,
            MediaType = MediaType.Image,
            MediaCategory = MediaCategory.Logo,
            OriginalFileName = file.FileName,
            StoredFileName = stored.StoredFileName,
            FileExtension = kind.Value.Extension.TrimStart('.'),
            MimeType = kind.Value.MimeType,
            FileSize = file.Length,
            StorageProvider = "Local",
            RelativePath = stored.RelativePath,
            PublicUrl = MediaUrl(stored.FileId),
            // Shown on the sign-in screen and as the tab icon, so it has to load without a token.
            IsPublic = true,
            IsProcessed = true,
        }, cancellationToken);

        var branding = await GetOrCreateAsync(tenant.Id, cancellationToken);
        SetSlot(branding, slot, stored.FileId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(await ToResponseAsync(tenant, branding, cancellationToken), "Image uploaded."));
    }

    /// <summary>Empties one image slot. The link goes; the stored file is left where it is.</summary>
    [HttpDelete("assets/{slot}")]
    [RequirePermission(Permissions.BrandingManage)]
    [ProducesResponseType<ApiResponse<BrandingResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAsset(string slot, [FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!BrandingAssetSlots.All.Contains(slot))
        {
            return NotFound(ApiResponseFactory.NotFound($"There is no branding image called '{slot}'."));
        }

        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var branding = await _branding.GetByTenantAsync(tenant!.Id, cancellationToken);
        if (branding is not null && GetSlot(branding, slot) is not null)
        {
            SetSlot(branding, slot, null);
            _branding.Update(branding);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(await ToResponseAsync(tenant, branding, cancellationToken), "Image removed."));
    }

    // ---- Helpers ----

    private async Task<TenantBranding> GetOrCreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var branding = await _branding.GetByTenantAsync(tenantId, cancellationToken);
        if (branding is not null)
        {
            _branding.Update(branding);
            return branding;
        }

        branding = new TenantBranding { Id = Guid.NewGuid(), TenantId = tenantId };
        await _branding.AddAsync(branding, cancellationToken);
        return branding;
    }

    private static Guid? GetSlot(TenantBranding branding, string slot) => slot switch
    {
        BrandingAssetSlots.Logo => branding.LogoMediaId,
        BrandingAssetSlots.LogoDark => branding.LogoDarkMediaId,
        BrandingAssetSlots.LogoMark => branding.LogoMarkMediaId,
        BrandingAssetSlots.Favicon => branding.FaviconMediaId,
        _ => branding.LoginBackgroundMediaId,
    };

    private static void SetSlot(TenantBranding branding, string slot, Guid? mediaId)
    {
        switch (slot)
        {
            case BrandingAssetSlots.Logo: branding.LogoMediaId = mediaId; break;
            case BrandingAssetSlots.LogoDark: branding.LogoDarkMediaId = mediaId; break;
            case BrandingAssetSlots.LogoMark: branding.LogoMarkMediaId = mediaId; break;
            case BrandingAssetSlots.Favicon: branding.FaviconMediaId = mediaId; break;
            default: branding.LoginBackgroundMediaId = mediaId; break;
        }
    }

    private static string MediaUrl(Guid mediaId) => $"/api/media/{mediaId}/content";

    private static BrandingAssetsResponse AssetsOf(TenantBranding? b)
    {
        static string? Url(Guid? id) => id is { } mediaId ? MediaUrl(mediaId) : null;
        return new BrandingAssetsResponse(
            Url(b?.LogoMediaId), Url(b?.LogoDarkMediaId), Url(b?.LogoMarkMediaId), Url(b?.FaviconMediaId), Url(b?.LoginBackgroundMediaId));
    }

    private async Task<BrandingResponse> ToResponseAsync(Tenant tenant, TenantBranding? branding, CancellationToken cancellationToken)
    {
        string? updatedBy = null;
        if (branding?.UpdatedById is { } actor)
        {
            var names = await _users.GetFullNamesAsync(new[] { actor }, cancellationToken);
            names.TryGetValue(actor, out updatedBy);
        }

        return new BrandingResponse(
            tenant.Id,
            tenant.Name,
            tenant.Identifier,
            branding is not null,
            BrandingThemeRules.Deserialize(branding?.ThemeJson),
            AssetsOf(branding),
            updatedBy,
            branding?.UpdatedOnUtc);
    }

    /// <summary>A tenant that is missing or not active is shown the stock look, the same as one never customised.</summary>
    private async Task<PublicBrandingResponse> ToPublicAsync(Tenant? tenant, CancellationToken cancellationToken)
    {
        var branding = tenant is { Status: TenantStatus.Active }
            ? await _branding.GetByTenantAsync(tenant.Id, cancellationToken)
            : null;
        return new PublicBrandingResponse(BrandingThemeRules.Deserialize(branding?.ThemeJson), AssetsOf(branding));
    }

    /// <summary>The image type the first bytes say this is, or null. Leaves the stream at its start.</summary>
    private static async Task<(string Extension, string MimeType)?> SniffImageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;

        if (read >= 8 && head.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (".png", "image/png");
        }
        if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }
        if (read >= 12 && head.AsSpan(0, 4).SequenceEqual("RIFF"u8) && head.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return (".webp", "image/webp");
        }
        if (read >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01 && head[3] == 0x00)
        {
            return (".ico", "image/x-icon");
        }
        return null;
    }

    /// <summary>
    /// Resolves the tenant to operate on: a Super Admin may target any active tenant via the override;
    /// everyone else is pinned to their active tenant.
    /// </summary>
    private async Task<(Tenant? Tenant, IActionResult? Error)> ResolveTargetTenantAsync(Guid? requested, CancellationToken cancellationToken)
    {
        var active = User.GetActiveTenantId();
        var target = User.IsSuperAdmin() && requested is { } asked ? asked : active;
        if (target is not { } id)
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
        }

        var tenant = await _tenants.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return (null, NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", id.ToString())));
        }
        if (id != active && tenant.Status != TenantStatus.Active)
        {
            return (null, BadRequest(ApiResponseFactory.Error(ApiErrorCodes.TenantInactive, "The tenant is not active.", id.ToString())));
        }
        return (tenant, null);
    }
}
