using Chat.Application.Services.Interfaces.Announcements;
using Chat.Domain.DTOs.Announcements;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Messages;
using Chat_Api.Helpers;
using Chat_Api.Hubs.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Chat_Api.Controllers.Announcements;

[Authorize]
[ApiController]
[Route("api")]
public sealed class AnnouncementsController : ControllerBase
{
    private readonly IAnnouncementService _announcementService;
    private readonly IChatRepository _chatRepository;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnnouncementsController> _logger;

    public AnnouncementsController(
        IAnnouncementService announcementService,
        IChatRepository chatRepository,
        IHubContext<ChatHub> hubContext,
        IConfiguration configuration,
        ILogger<AnnouncementsController> logger)
    {
        _announcementService = announcementService;
        _chatRepository = chatRepository;
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("announcements")]
    public async Task<IActionResult> GetAnnouncements(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] string? location = null,
        [FromQuery] bool? important = null,
        [FromQuery] bool? pinned = null,
        [FromQuery] int? statusId = null,
        [FromQuery] string? filter = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.GetAnnouncementsAsync(
                userId,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                new AnnouncementListQuery
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = search,
                    CategoryId = categoryId,
                    DepartmentId = departmentId,
                    Location = location,
                    Important = important,
                    Pinned = pinned,
                    StatusId = statusId,
                    Filter = filter,
                    SortBy = sortBy
                },
                cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("announcements/stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.GetStatsAsync(
                userId, tenant.OrgId, tenant.AppId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("announcements/unread-count")]
    public async Task<IActionResult> GetUnreadCount(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.GetUnreadCountAsync(
                userId, tenant.OrgId, tenant.AppId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("announcement-categories")]
    public async Task<IActionResult> GetCategories(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.GetCategoriesAsync(
                userId, tenant.OrgId, tenant.AppId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("announcements/{announcementId:long}")]
    public async Task<IActionResult> GetById(
        long announcementId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.GetByIdAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPost("announcements")]
    public async Task<IActionResult> Create(
        [FromBody] CreateAnnouncementRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);
            var result = await _announcementService.CreateAsync(
                userId, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            await NotifyRecipientsAsync(
                result,
                userId,
                request?.RecipientUserIds,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                isUpdate: false,
                cancellationToken);

            await BroadcastAnnouncementChangedAsync(
                "created",
                result.AnnouncementId,
                tenant.OrgId,
                tenant.AppId,
                CollectAudience(result, userId, request?.RecipientUserIds),
                cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("announcements/{announcementId:long}")]
    public async Task<IActionResult> Update(
        long announcementId,
        [FromBody] UpdateAnnouncementRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);
            var result = await _announcementService.UpdateAsync(
                announcementId, userId, request!, tenant.OrgId, tenant.AppId, cancellationToken);

            await NotifyRecipientsAsync(
                result,
                userId,
                request?.RecipientUserIds,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                isUpdate: true,
                cancellationToken);

            await BroadcastAnnouncementChangedAsync(
                "updated",
                result.AnnouncementId,
                tenant.OrgId,
                tenant.AppId,
                CollectAudience(result, userId, request?.RecipientUserIds),
                cancellationToken,
                isPinned: result.IsPinned);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpDelete("announcements/{announcementId:long}")]
    public async Task<IActionResult> Delete(
        long announcementId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);

            // Soft-delete only (delete_flag=1 + delete_at). Row stays in DB.
            AnnouncementDto? existing = null;
            try
            {
                existing = await _announcementService.GetByIdAsync(
                    announcementId, userId, tenant.OrgId, tenant.AppId, cancellationToken);
            }
            catch (ChatOperationException)
            {
                // Delete still proceeds; broadcast may only reach the actor.
            }

            var result = await _announcementService.DeleteAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, cancellationToken);

            await BroadcastAnnouncementChangedAsync(
                "deleted",
                announcementId,
                tenant.OrgId,
                tenant.AppId,
                CollectAudience(existing, userId, existing?.RecipientUserIds),
                cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPost("announcements/{announcementId:long}/read")]
    public async Task<IActionResult> MarkRead(
        long announcementId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.MarkReadAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPost("announcements/{announcementId:long}/pin")]
    public async Task<IActionResult> Pin(
        long announcementId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.PinAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, true, cancellationToken);

            await BroadcastPinAudienceAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, true, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPost("announcements/{announcementId:long}/unpin")]
    public async Task<IActionResult> Unpin(
        long announcementId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _announcementService.PinAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, false, cancellationToken);

            await BroadcastPinAudienceAsync(
                announcementId, userId, tenant.OrgId, tenant.AppId, false, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Notify selected recipients (bell + SignalR). Never fails the create/update API.
    /// </summary>
    private async Task NotifyRecipientsAsync(
        AnnouncementDto announcement,
        long senderUserId,
        IReadOnlyList<long>? recipientUserIds,
        int orgId,
        int appId,
        int? fiscalYearId,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        if (announcement.AnnouncementId <= 0 || senderUserId <= 0)
            return;

        // Prefer request ids; fall back to ids returned from create/update (get_by_id).
        var recipients = (recipientUserIds ?? Array.Empty<long>())
            .Concat(announcement.RecipientUserIds ?? Array.Empty<long>())
            .Where(id => id > 0 && id != senderUserId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Announcement {AnnouncementId}: no recipients to notify (request empty).",
                announcement.AnnouncementId);
            return;
        }

        // SoftOnCloud uses calendar year as fiscal_year_id (e.g. 2026) when claim is missing.
        var resolvedFy = fiscalYearId is > 0
            ? fiscalYearId.Value
            : DateTime.UtcNow.Year;

        var title = isUpdate ? "Announcement updated" : "New announcement";
        var preview = string.IsNullOrWhiteSpace(announcement.Title)
            ? announcement.Description ?? "New announcement"
            : announcement.Title.Trim();
        if (preview.Length > 200)
            preview = preview[..200];

        foreach (var receiverId in recipients)
        {
            try
            {
                var notification = await _chatRepository.CreateNotificationAsync(
                    receiverId,
                    senderUserId,
                    title,
                    preview,
                    announcement.AnnouncementId,
                    orgId,
                    appId,
                    resolvedFy,
                    cancellationToken,
                    referenceType: "ANNOUNCEMENT",
                    notificationType: "ANNOUNCEMENT",
                    referenceEntity: "tab_announcements");

                var payload = new
                {
                    notificationId = notification.NotificationId,
                    notificationType = notification.NotificationType ?? "ANNOUNCEMENT",
                    title = notification.Title,
                    message = notification.Message,
                    referenceId = notification.ReferenceId,
                    referenceType = notification.ReferenceType ?? "ANNOUNCEMENT",
                    senderUserId = notification.SenderUserId,
                    createdDate = notification.CreatedDate,
                    isRead = notification.IsRead
                };

                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(receiverId))
                    .SendAsync("ReceiveNotification", payload, cancellationToken);
            }
            catch (Exception ex)
            {
                // Announcement already saved — do not fail API if notification fails.
                _logger.LogError(
                    ex,
                    "Failed to notify user {ReceiverId} for announcement {AnnouncementId}",
                    receiverId,
                    announcement.AnnouncementId);
            }
        }
    }

    private static HashSet<long> CollectAudience(
        AnnouncementDto? announcement,
        long actorUserId,
        IReadOnlyList<long>? extraIds)
    {
        var set = new HashSet<long> { actorUserId };
        if (announcement is not null)
        {
            if (announcement.PostedBy > 0) set.Add(announcement.PostedBy);
            foreach (var id in announcement.RecipientUserIds ?? Array.Empty<long>())
            {
                if (id > 0) set.Add(id);
            }
        }
        foreach (var id in extraIds ?? Array.Empty<long>())
        {
            if (id > 0) set.Add(id);
        }
        return set;
    }

    private async Task BroadcastPinAudienceAsync(
        long announcementId,
        long userId,
        int orgId,
        int appId,
        bool isPinned,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _announcementService.GetByIdAsync(
                announcementId, userId, orgId, appId, cancellationToken);
            await BroadcastAnnouncementChangedAsync(
                "pinned",
                announcementId,
                orgId,
                appId,
                CollectAudience(detail, userId, detail.RecipientUserIds),
                cancellationToken,
                isPinned);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pin broadcast failed for announcement {AnnouncementId}", announcementId);
        }
    }

    /// <summary>
    /// Live list sync for other logged-in users (no page refresh).
    /// </summary>
    private async Task BroadcastAnnouncementChangedAsync(
        string action,
        long announcementId,
        int orgId,
        int appId,
        IEnumerable<long> audienceUserIds,
        CancellationToken cancellationToken,
        bool? isPinned = null)
    {
        var payload = new
        {
            action,
            announcementId,
            orgId,
            appId,
            isPinned
        };

        foreach (var uid in audienceUserIds.Where(id => id > 0).Distinct())
        {
            try
            {
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(uid))
                    .SendAsync("AnnouncementChanged", payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "AnnouncementChanged hub send failed for user {UserId}",
                    uid);
            }
        }
    }

    private (long UserId, (int OrgId, int AppId, int? FiscalYearId) Tenant) RequireUserAndTenant(
        int? orgId,
        int? appId,
        int? fiscalYearId)
    {
        var userId = CurrentUserHelper.GetUserId(User);
        if (userId is null)
            throw new ChatOperationException("Authenticated user id claim is missing.", 401);

        return (userId.Value, ResolveTenant(orgId, appId, fiscalYearId));
    }

    private (int OrgId, int AppId, int? FiscalYearId) ResolveTenant(
        int? orgId,
        int? appId,
        int? fiscalYearId)
    {
        var resolvedOrg = orgId
            ?? CurrentUserHelper.GetIntClaim(User, "orgId", "org_id", "organizationId", "OrganisationId")
            ?? 0;

        var resolvedApp = appId
            ?? CurrentUserHelper.GetIntClaim(User, "appId", "app_id", "productId", "product_id")
            ?? _configuration.GetValue<int?>("SoftOnCloud:ProductId")
            ?? 0;

        var resolvedFy = fiscalYearId
            ?? CurrentUserHelper.GetIntClaim(User, "fiscalYearId", "fiscal_year_id", "FiscalYearId");

        return (resolvedOrg, resolvedApp, resolvedFy);
    }
}
