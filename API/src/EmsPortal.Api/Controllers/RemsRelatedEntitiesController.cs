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
/// Related Entities: every submitted request whose client declared somebody ALONGSIDE themselves, and how
/// far each of those related clients has got.
///
/// <para>
/// The two sources are the two cards the intake form asks that on, one per kind of client. An Individual
/// is asked "Spouse &amp; More Individuals" — the other people on their return
/// (<see cref="REMSAdditionalIndividual"/>). Every other entity type is asked "Other Entities" — the
/// client's other businesses (<see cref="REMSAdditionalEntity"/>). A request can carry both, because the
/// second question used to be asked of individuals too.
/// </para>
/// <para>
/// THE STATUS ON EACH ROW IS SET BY HAND and by nothing else. Neither raising the follow-up request nor
/// approving it moves it, and neither does the parent request's own status: this is the firm's own note
/// about work that largely happens off this portal, and its value is that whoever is doing that work says
/// where it stands. See <see cref="RemsRelatedEntityStatuses"/>.
/// </para>
/// <para>
/// OPEN TO EVERY SIGNED-IN USER, read and write, and NOT narrowed to the caller's own requests — unlike
/// every other REMS list, which returns what the caller raised or is named on. This one is a shared
/// tracking board: the point of it is that anybody chasing a client group can see the whole picture and
/// say where a piece of it has got to. Every change is attributed all the same — the row's own audit
/// columns, plus a timeline entry on the parent request — which is what makes an open board answerable.
/// </para>
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
    /// <summary>
    /// Which table a row is in, as it travels on the wire. The row carries it and the write route takes
    /// it, so a screen builds the URL straight out of the row it is acting on — singular, because it
    /// names ONE row's kind rather than a collection.
    /// </summary>
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

    /// <summary>
    /// The paginated Related Entities list — one row per request, with its related clients nested.
    /// </summary>
    /// <param name="search">REMS number, client name, or a RELATED client's name.</param>
    /// <param name="entityType">Option-set CODE (REMS.IndustryGroup) — what kind of entity the client is.</param>
    /// <param name="relatedStatus">
    /// Option-set CODE (REMS.RelatedEntityStatus). Narrows to requests holding at least one related client
    /// at that status — a request is not at one status, its rows are.
    /// </param>
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

        // Whether the caller may open a request as a FORM, asked once for the page: the permission half
        // is the caller's, so only the record half varies per row. The rule is
        // RemsRequestsController.ActionsFor's — a REMS admin, or a request the caller raised (or had
        // raised for them) — and it is answered here rather than in the browser because this list is open
        // to everyone, so most callers may edit none of what they can see.
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

    /// <summary>
    /// Move one related client along. The only write on this list, and the only thing that ever changes
    /// the status — see the class remarks.
    /// </summary>
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

        // Resolved against the TENANT's own copy of the list, so a firm that has added a fifth position can
        // set it, and a code that is not on their list is refused rather than stored as a dangling
        // reference.
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

        // On the PARENT request, because that is the only record these rows have a timeline on. Old and
        // new together: the status moves only by hand, so who moved it and from what is the whole audit of
        // it. A row nobody had answered for reads as its default rather than as a blank.
        await _activity.WriteAsync(
            new CreateActivityEventDto(
                EntityType.Rems, remsId, ActivityEventTypes.RemsRelatedEntityStatusChanged,
                previous ?? RemsRelatedEntityStatuses.NotInitiated, code, me),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Read back rather than patched together here: the row's REFERENCE turns on its position among its
        // siblings and on whether the status has left Not Initiated, so the answer to "what does this row
        // look like now" is the same mapping the list itself does.
        var refreshed = await BuildRelatedClientAsync(remsId, id, cancellationToken);
        return refreshed is null
            ? NotFound(ApiResponseFactory.NotFound("Related client not found."))
            : Ok(ApiResponseFactory.Success(refreshed, "Related client status updated."));
    }

    // -------------------- Mapping --------------------

    /// <summary>
    /// Splits what the client declared into the two things the nested table shows: the PARENT header, and
    /// the rows under it.
    /// <para>
    /// A person filing JOINTLY with the client goes into the header rather than becoming a row of their
    /// own. One return means one client and one invoice, so a row for them — with a status, and a
    /// reference inviting somebody to raise a request — would be a second request for a person who is
    /// already on this one. The header names them instead, with the reason beside them.
    /// </para>
    /// <para>
    /// The rest are numbered in the order the client declared them, ACROSS both kinds, which is what makes
    /// <c>REMS-1042-C1</c> a thing a reader can count to. The number is only printed once the row has left
    /// Not Initiated: before that there is nothing for it to point at.
    /// </para>
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

    /// <summary>
    /// What this related client is referred to by. A row that has actually produced a request is named by
    /// THAT request — a real number beats a derived one, and it links somewhere. Otherwise the derived
    /// reference, and only once somebody has moved the row along.
    /// </summary>
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

    /// <summary>One row as the list would draw it, re-read after a write. Null when it has gone.</summary>
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

        // The parent header is discarded here — only one CHILD is being rebuilt, and all that needs from
        // the parent is its REMS number for the reference. So the client's name is not read at all, which
        // is just as well: GetByIdAsync does not load ClientPerson.
        var (_, children) = MapRelated(
            rems.REMSNumber, string.Empty, null, declared, createdNumbers);
        return children.FirstOrDefault(c => c.Id == rowId);
    }
}
