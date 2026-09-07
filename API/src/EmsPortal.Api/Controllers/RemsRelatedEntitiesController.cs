using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Related Entities: every submitted request whose client declared somebody ALONGSIDE themselves, and
/// how far each of those related clients has got.
/// </summary>
[ApiController]
[Route("api/rems/related-entities")]
[Produces("application/json")]
[Tags("REMS Related Entities")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsRelatedEntitiesController : ControllerBase
{
    /// <summary>Which table a row is in, as it travels on the wire.</summary>
    private const string KindIndividual = "individual";

    /// <inheritdoc cref="KindIndividual"/>
    private const string KindEntity = "entity";

    /// <summary>See <c>RemsRepository.JointFiling</c> — a joint filer is the parent, not a row under it.</summary>
    private const string JointFiling = "joint";

    private readonly IRemsRepository _rems;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IOptionCodeResolver _codes;

    public RemsRelatedEntitiesController(
        IRemsRepository rems,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IOptionCodeResolver codes)
    {
        _rems = rems;
        _users = users;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _codes = codes;
    }

    // -------------------- The list --------------------

    /// <summary>The paginated Related Entities list — one row per request, with its related clients nested.</summary>
    /// <param name="search">REMS number, client name, or a RELATED client's name.</param>
    /// <param name="entityType">Option-set CODE (REMS.EntityType) — what kind of entity the client is.</param>
    /// <param name="relatedStatus"> Option-set CODE (REMS.RelatedEntityStatus). Narrows to requests holding at least one related client at that status — a request is not at one status, its rows are. </param>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsRelatedEntityRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? relatedStatus = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var (items, total) = await _rems.ListRelatedEntitiesAsync(
            new RemsRelatedEntityQuery(
                search, entityType, relatedStatus, new SortRequest(sortBy, descending), page, limit),
            cancellationToken);

        var remsIds = items.Select(i => i.RemsId).ToList();
        // Two follow-up reads for the whole page, never one per row: the related clients themselves, and
        // the numbers of any requests they have already produced.
        var related = (await _rems.ListRelatedClientsAsync(remsIds, cancellationToken))
            .GroupBy(c => c.RemsId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RemsRelatedClientItem>)g.ToList());
        var createdNumbers = await _rems.GetNumbersAsync(
            related.Values.SelectMany(v => v)
                .Select(c => c.CreatedRemsId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList(),
            cancellationToken);

        var names = await _users.GetFullNamesAsync(
            items.SelectMany(i => new[] { i.AdminAssignedToId, i.CreatedById, i.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);

        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        // Whether the caller may open a request as a FORM, asked once for the page: the permission half is the
        // caller's, so only the record half varies per row.
        var me = User.GetUserId();
        var isRemsAdmin = RemsSetupAccess.IsRemsAdmin(User);
        var mayUpdate = User.HasPermission(Permissions.RemsRequestsUpdate);
        bool CanEdit(RemsRelatedEntityItem i) =>
            mayUpdate && (isRemsAdmin || (me is { } uid && (i.CreatedById == uid || i.OnBehalfOfUserId == uid)));

        var rows = items.Select(i =>
        {
            var declared = related.TryGetValue(i.RemsId, out var d)
                ? d
                : (IReadOnlyList<RemsRelatedClientItem>)Array.Empty<RemsRelatedClientItem>();
            var (parent, children) = MapRelated(
                i.RemsNumber, i.ClientName, i.ClientNameSuffix, declared, createdNumbers);
            return new RemsRelatedEntityRow(
                i.RemsId, i.RemsNumber, i.ClientName, i.ClientNameSuffix,
                i.ClientEmail, i.EntityType, i.RequestStatus,
                RemsWorkspaceMapper.UserRef(i.AdminAssignedToId, names), i.SubmittedOnUtc,
                parent, children, i.RelatedCount, CanEdit(i),
                NameOf(i.CreatedById), i.CreatedOnUtc, NameOf(i.UpdatedById), i.UpdatedOnUtc);
        });

        return Ok(ApiResponseFactory.Paginated(rows, "REMS related entities retrieved.", page, limit, total));
    }

    // -------------------- Setting a row's status --------------------

    /// <summary>Move one related client along.</summary>
    /// <param name="kind"><c>individual</c> or <c>entity</c> — which table the row is in.</param>
    [HttpPut("{kind}/{id:guid}/status")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsRelatedClientView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(
        string kind,
        Guid id,
        [FromBody] SetRemsRelatedStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var code = request?.Status?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "A status is required.",
                "Choose one of the values on the Related Entity Status list."));
        }

        // Resolved against the TENANT's own copy of the list, so a firm that has added a fifth position can set
        // it, and a code that is not on their list is refused rather than stored as a dangling reference.
        if (await _codes.RemsIdAsync(RemsOptionSetKeys.RelatedEntityStatus, code, cancellationToken)
            is not { } resolved)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Unknown status.",
                $"That is not a value on your Related Entity Status list (Administration → Option Sets)."));
        }

        Guid remsId;
        string? previous;
        if (string.Equals(kind, KindIndividual, StringComparison.OrdinalIgnoreCase))
        {
            if (await _rems.GetAdditionalIndividualAsync(id, cancellationToken) is not { } individual)
            {
                return NotFound(ApiResponseFactory.NotFound("Related client not found."));
            }

            remsId = individual.REMSId;
            previous = individual.RelatedStatus?.Value;
            individual.RelatedStatusId = resolved;
            _rems.UpdateAdditionalIndividual(individual);
        }
        else if (string.Equals(kind, KindEntity, StringComparison.OrdinalIgnoreCase))
        {
            if (await _rems.GetAdditionalEntityAsync(id, cancellationToken) is not { } entity)
            {
                return NotFound(ApiResponseFactory.NotFound("Related client not found."));
            }

            remsId = entity.REMSId;
            previous = entity.RelatedStatus?.Value;
            entity.RelatedStatusId = resolved;
            _rems.UpdateAdditionalEntity(entity);
        }
        else
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Unknown kind.",
                $"Expected '{KindIndividual}' or '{KindEntity}' in the URL."));
        }

        // On the PARENT request, because that is the only record these rows have a timeline on.
        await _activity.WriteAsync(
            new CreateActivityEventDto(
                EntityType.Rems, remsId, ActivityEventTypes.RemsRelatedEntityStatusChanged,
                previous ?? RemsRelatedEntityStatuses.NotInitiated, code, me),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Read back rather than patched together here: the row's REFERENCE turns on its position among its
        // siblings and on whether the status has left Not Initiated.
        var refreshed = await BuildRelatedClientAsync(remsId, id, cancellationToken);
        return refreshed is null
            ? NotFound(ApiResponseFactory.NotFound("Related client not found."))
            : Ok(ApiResponseFactory.Success(refreshed, "Related client status updated."));
    }

    // -------------------- Mapping --------------------

    /// <summary>
    /// Splits what the client declared into the two things the nested table shows: the PARENT header,
    /// and the rows under it.
    /// </summary>
    private static (RemsRelatedParentView Parent, List<RemsRelatedClientView> Children) MapRelated(
        string remsNumber,
        string clientName,
        string? clientSuffix,
        IReadOnlyList<RemsRelatedClientItem> declared,
        IReadOnlyDictionary<Guid, string> createdNumbers)
    {
        var jointFiler = declared.FirstOrDefault(c =>
            c.Kind == RemsRelatedClientKind.Individual && c.FilingType == JointFiling);

        var parent = new RemsRelatedParentView(
            clientName,
            clientSuffix,
            jointFiler is null
                ? null
                : new RemsRelatedJointFilerView(jointFiler.Name, jointFiler.Suffix, jointFiler.Relation));

        var children = new List<RemsRelatedClientView>();
        var ordinal = 0;
        foreach (var row in declared)
        {
            if (ReferenceEquals(row, jointFiler))
            {
                continue;
            }

            ordinal++;
            children.Add(new RemsRelatedClientView(
                row.Kind == RemsRelatedClientKind.Individual ? KindIndividual : KindEntity,
                row.Id,
                row.Name,
                row.Suffix,
                row.Relation,
                row.Email,
                row.PhoneNumber,
                row.Status,
                Reference(remsNumber, ordinal, row, createdNumbers),
                row.CreatedRemsId));
        }

        return (parent, children);
    }

    /// <summary>What this related client is referred to by.</summary>
    private static string? Reference(
        string remsNumber,
        int ordinal,
        RemsRelatedClientItem row,
        IReadOnlyDictionary<Guid, string> createdNumbers)
    {
        if (row.CreatedRemsId is { } created && createdNumbers.TryGetValue(created, out var number))
        {
            return number;
        }

        return RemsRelatedEntityStatuses.IsUnderway(row.Status) ? $"{remsNumber}-C{ordinal}" : null;
    }

    /// <summary>One row as the list would draw it, re-read after a write.</summary>
    private async Task<RemsRelatedClientView?> BuildRelatedClientAsync(
        Guid remsId, Guid rowId, CancellationToken cancellationToken)
    {
        var rems = await _rems.GetByIdAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return null;
        }

        var declared = await _rems.ListRelatedClientsAsync(new[] { remsId }, cancellationToken);
        var createdNumbers = await _rems.GetNumbersAsync(
            declared.Select(c => c.CreatedRemsId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList(),
            cancellationToken);

        // The parent header is discarded here — only one CHILD is being rebuilt, and all that needs from the
        // parent is its REMS number for the reference.
        var (_, children) = MapRelated(
            rems.REMSNumber, string.Empty, null, declared, createdNumbers);
        return children.FirstOrDefault(c => c.Id == rowId);
    }
}
