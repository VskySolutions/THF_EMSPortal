using System.Globalization;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Security;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Rems;

/// <summary>
/// The intake form coming in: the client's own submission through their link, or a user completing it for
/// them from the request page. Both write the answers onto the client, stamp the request, record the
/// client's intake as on file (once, ever) and move the request to Admin Review.
/// </summary>
public sealed class RemsIntakeSubmission
{
    private readonly IRemsRequestRepository _requests;
    private readonly IClientRepository _clients;
    private readonly RemsClientIntake _intake;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IRemsEmailNotifier _emailNotifier;
    private readonly IOptionCodeResolver _codes;
    private readonly ILogger<RemsIntakeSubmission> _logger;
    private readonly string _baseUrl;

    public RemsIntakeSubmission(
        IRemsRequestRepository requests,
        IClientRepository clients,
        RemsClientIntake intake,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IRemsEmailNotifier emailNotifier,
        IOptionCodeResolver codes,
        IOptions<AppOptions> appOptions,
        ILogger<RemsIntakeSubmission> logger)
    {
        _requests = requests;
        _clients = clients;
        _intake = intake;
        _users = users;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _notifications = notifications;
        _emailNotifier = emailNotifier;
        _codes = codes;
        _logger = logger;
        _baseUrl = appOptions.Value.BaseUrl;
    }

    /// <summary>
    /// Writes the answers and moves the request to Admin Review, in one transaction.
    /// <paramref name="byUserId"/> is null when the client submitted it themselves.
    /// </summary>
    public async Task SubmitAsync(
        RemsRequest request, RemsFormPayloadV1 payload, string entityType, Guid? byUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var submittedStatusId = await _codes.RequireRemsIdAsync(
            RemsOptionSetKeys.Status, RemsRequestStatuses.AdminReview, cancellationToken);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // The answers go onto the client master; the request records when they arrived and who typed them.
            await _intake.ApplyAsync(request, payload, entityType, ct);
            request.FormSubmittedOnUtc = now;
            request.FormLastSavedOnUtc = now;
            request.IntakeCompletedByUserId = byUserId;
            MarkIntakeOnFile(request.Client!, request, now, byUserId);

            var previousStatusId = request.StatusId;
            request.StatusId = submittedStatusId;
            _requests.Update(request);
            await _requests.AddStatusHistoryAsync(new RemsStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId, // EXPLICIT — no tenant stamping in the public context.
                REMSId = request.Id,
                FromStatusId = previousStatusId,
                ToStatusId = submittedStatusId,
                ChangedByUserId = byUserId,
                ChangedOnUtc = now,
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>The client's intake is on file from this request. Recorded once: a later correction, or a second form, never moves it.</summary>
    private void MarkIntakeOnFile(Client client, RemsRequest request, DateTime now, Guid? byUserId)
    {
        if (client.IntakeSubmittedOnUtc is not null)
        {
            return;
        }
        client.IntakeSubmittedOnUtc = now;
        client.IntakeSubmittedRemsId = request.Id;
        client.IntakeSubmittedByUserId = byUserId;
        _clients.Update(client);
    }

    /// <summary>
    /// After the commit: the people on the request are told in-app and by email, the admin pool too when
    /// nobody holds it, and the timeline gets its row. Best-effort — the submission is already durable.
    /// </summary>
    public async Task NotifyAsync(RemsRequest request, string? clientName, Guid? byUserId, CancellationToken cancellationToken)
    {
        var name = clientName ?? request.Client?.ClientName ?? string.Empty;
        // Assigned admin, CSE and the requester — less whoever did it themselves.
        var recipients = new[] { request.AdminAssignedToId, request.CSEId, request.CreatedById }
            .Where(id => id.HasValue && id != byUserId)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        try
        {
            foreach (var userId in recipients)
            {
                await _notifications.DispatchAsync(new CreateNotificationDto(
                    userId,
                    NotificationType.RemsFormSubmitted,
                    byUserId is null ? "A REMS onboarding form was submitted" : "A REMS onboarding form was completed for the client",
                    $"{request.RemsNumber} — {name}",
                    EntityType.Rems,
                    request.Id), cancellationToken);
            }

            // Nobody has claimed this request, so the answers that just landed are on no admin's desk in particular.
            if (request.AdminAssignedToId is null)
            {
                var admins = await _users.ListByTenantRolesAsync(
                    request.TenantId, new[] { Roles.Admin, Roles.SuperAdmin }, cancellationToken);
                foreach (var admin in admins.Where(a => !recipients.Contains(a.Id) && a.Id != byUserId))
                {
                    await _notifications.DispatchAsync(new CreateNotificationDto(
                        admin.Id,
                        NotificationType.RemsRequestAssigned,
                        "A REMS request is waiting for pickup",
                        $"{request.RemsNumber} — {name}",
                        EntityType.Rems,
                        request.Id), cancellationToken);
                }
            }

            await _activity.WriteAsync(
                new CreateActivityEventDto(
                    EntityType.Rems, request.Id,
                    byUserId is null ? ActivityEventTypes.RemsFormSubmitted : ActivityEventTypes.RemsFormCompletedByUser),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The staff email, enqueued on a worker; never throws or blocks.
            var model = new RemsFormSubmittedEmail(
                name,
                request.RemsNumber,
                $"{_baseUrl.TrimEnd('/')}/rems/requests/{request.Id}",
                (request.FormSubmittedOnUtc ?? DateTime.UtcNow).ToString("f", CultureInfo.InvariantCulture) + " UTC");
            foreach (var userId in recipients)
            {
                var user = await _users.GetByIdAsync(userId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    _emailNotifier.SendFormSubmitted(request.TenantId, user!.Email, model);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "The form on REMS request {RemsId} came in, but post-commit notification/email dispatch failed.", request.Id);
        }
    }
}
