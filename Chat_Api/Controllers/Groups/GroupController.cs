using Chat.Application.Services.Interfaces.Groups;
using Chat.Domain.DTOs.Groups;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Messages;
using Chat_Api.Helpers;
using Chat_Api.Hubs.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Chat_Api.Controllers.Groups;

[Authorize]
[ApiController]
[Route("api/chat")]
public sealed class GroupController : ControllerBase
{
    private readonly IGroupService _groupService;
    private readonly IChatRepository _chatRepository;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GroupController> _logger;

    public GroupController(
        IGroupService groupService,
        IChatRepository chatRepository,
        IHubContext<ChatHub> hubContext,
        IConfiguration configuration,
        ILogger<GroupController> logger)
    {
        _groupService = groupService;
        _chatRepository = chatRepository;
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Create a group. Creator (JWT) becomes admin; memberUserIds are SoftOnCloud user ids.
    /// Notifies each added member via SignalR ReceiveNotification (live).
    /// </summary>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(
        [FromBody] CreateGroupRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var group = await _groupService.CreateGroupAsync(
                userId.Value, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            var memberIds = (request?.MemberUserIds ?? Array.Empty<long>())
                .Where(id => id > 0 && id != userId.Value)
                .Distinct()
                .ToList();

            await NotifyMembersAddedToGroupAsync(
                group,
                userId.Value,
                memberIds,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId.Value,
                isNewGroup: true,
                cancellationToken);

            return Ok(group);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Active groups for the authenticated user.</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> GetMyGroups(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var groups = await _groupService.GetUserGroupsAsync(
                userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            return Ok(groups);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Group details + active members (userIds only — merge SoftOnCloud User API in UI).</summary>
    [HttpGet("groups/{groupId:long}")]
    public async Task<IActionResult> GetGroupDetails(
        long groupId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var details = await _groupService.GetGroupDetailsAsync(
                groupId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            return Ok(details);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Admin-only: add SoftOnCloud userIds as non-admin members.</summary>
    [HttpPost("groups/{groupId:long}/members")]
    public async Task<IActionResult> AddGroupMembers(
        long groupId,
        [FromBody] AddGroupMembersRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var result = await _groupService.AddGroupMembersAsync(
                groupId, userId.Value, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            var addedIds = result
                .Where(r => r.IsActive && r.UserId != userId.Value)
                .Select(r => r.UserId)
                .Distinct()
                .ToList();

            if (addedIds.Count > 0)
            {
                try
                {
                    var details = await _groupService.GetGroupDetailsAsync(
                        groupId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

                    await NotifyMembersAddedToGroupAsync(
                        new ChatGroupDto
                        {
                            GroupId = details.GroupId,
                            ChatId = details.ChatId,
                            GroupName = details.GroupName,
                            GroupCode = details.GroupCode,
                            Description = details.Description,
                            CreatedBy = details.CreatedBy
                        },
                        userId.Value,
                        addedIds,
                        tenant.OrgId,
                        tenant.AppId,
                        tenant.FiscalYearId.Value,
                        isNewGroup: false,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify new members for group {GroupId}", groupId);
                }
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Admin-only soft-remove member (left_at set). Cannot remove last admin.</summary>
    [HttpDelete("groups/{groupId:long}/members/{targetUserId:long}")]
    public async Task<IActionResult> RemoveGroupMember(
        long groupId,
        long targetUserId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var result = await _groupService.RemoveGroupMemberAsync(
                groupId, targetUserId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Admin-only: make or dismiss a member as group admin (WhatsApp-style multi-admin).
    /// Also allows an admin to dismiss themselves when another admin exists.
    /// SignalR: GroupMemberAdminChanged + ReceiveNotification (no page refresh).
    /// </summary>
    [HttpPut("groups/{groupId:long}/members/{targetUserId:long}/admin")]
    public async Task<IActionResult> SetGroupMemberAdmin(
        long groupId,
        long targetUserId,
        [FromBody] SetGroupMemberAdminRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var isAdmin = request?.IsAdmin ?? true;
            var result = await _groupService.SetGroupMemberAdminAsync(
                groupId,
                targetUserId,
                userId.Value,
                isAdmin,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId.Value,
                cancellationToken);

            // Live UI + bell — do not fail the API if notify fails.
            try
            {
                await NotifyGroupAdminRoleChangedAsync(
                    groupId,
                    userId.Value,
                    result,
                    isAdmin,
                    tenant.OrgId,
                    tenant.AppId,
                    tenant.FiscalYearId.Value,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Admin role saved but SignalR/notify failed for group {GroupId} user {UserId}",
                    groupId,
                    targetUserId);
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Admin-only: update group name/description.</summary>
    [HttpPut("groups/{groupId:long}")]
    public async Task<IActionResult> UpdateGroup(
        long groupId,
        [FromBody] UpdateGroupRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var group = await _groupService.UpdateGroupAsync(
                groupId, userId.Value, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            return Ok(group);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>Leave group (soft). Last active admin cannot leave.</summary>
    [HttpPost("groups/{groupId:long}/leave")]
    public async Task<IActionResult> LeaveGroup(
        long groupId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            if (tenant.FiscalYearId is null or <= 0)
                return BadRequest(new { message = "fiscalYearId is required." });

            var result = await _groupService.LeaveGroupAsync(
                groupId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// WhatsApp-style Message Info for a group message (Read by / Delivered to).
    /// </summary>
    [HttpGet("messages/{messageId:long}/info")]
    public async Task<IActionResult> GetMessageInfo(
        long messageId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _groupService.GetMessageInfoAsync(
                messageId,
                userId.Value,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Persist + SignalR fan-out so added members (e.g. Kenil) get a live bell notification.
    /// </summary>
    private async Task NotifyMembersAddedToGroupAsync(
        ChatGroupDto group,
        long actorUserId,
        IReadOnlyList<long> memberUserIds,
        int orgId,
        int appId,
        int fiscalYearId,
        bool isNewGroup,
        CancellationToken cancellationToken)
    {
        if (group.GroupId <= 0 || memberUserIds.Count == 0)
            return;

        var groupName = string.IsNullOrWhiteSpace(group.GroupName) ? "a group" : group.GroupName.Trim();
        var title = isNewGroup ? "Added to group" : "Added to group";
        var preview = isNewGroup
            ? $"You were added to \"{groupName}\""
            : $"You were added to \"{groupName}\"";
        if (preview.Length > 200)
            preview = preview[..200];

        var referenceType = $"GROUP:{group.GroupId}";

        foreach (var receiverId in memberUserIds)
        {
            if (receiverId <= 0 || receiverId == actorUserId)
                continue;

            try
            {
                var notification = await _chatRepository.CreateNotificationAsync(
                    receiverId,
                    actorUserId,
                    title,
                    preview,
                    group.GroupId,
                    orgId,
                    appId,
                    fiscalYearId,
                    cancellationToken,
                    referenceType: referenceType,
                    notificationType: "GROUP",
                    referenceEntity: "tab_groups");

                var notificationPayload = new
                {
                    notificationId = notification.NotificationId,
                    notificationType = notification.NotificationType ?? "GROUP",
                    title = notification.Title,
                    message = notification.Message,
                    referenceId = notification.ReferenceId,
                    referenceType = notification.ReferenceType ?? referenceType,
                    referenceEntity = notification.ReferenceEntity ?? "tab_groups",
                    senderUserId = notification.SenderUserId,
                    createdDate = notification.CreatedDate,
                    isRead = notification.IsRead,
                    groupId = group.GroupId,
                    chatId = group.ChatId,
                    groupName = group.GroupName
                };

                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(receiverId))
                    .SendAsync("ReceiveNotification", notificationPayload, cancellationToken);

                // Live list refresh for members already on Groups page.
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(receiverId))
                    .SendAsync(
                        "GroupCreated",
                        new
                        {
                            groupId = group.GroupId,
                            chatId = group.ChatId,
                            groupName = group.GroupName,
                            groupCode = group.GroupCode,
                            description = group.Description,
                            createdBy = actorUserId,
                            isAdmin = false
                        },
                        cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to notify user {ReceiverId} about group {GroupId}",
                    receiverId,
                    group.GroupId);
            }
        }
    }

    /// <summary>
    /// Live admin badge updates + bell when someone is made / dismissed as admin.
    /// </summary>
    private async Task NotifyGroupAdminRoleChangedAsync(
        long groupId,
        long actorUserId,
        GroupMemberChangeResult result,
        bool isAdmin,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken)
    {
        ChatGroupDetailsDto? details = null;
        try
        {
            details = await _groupService.GetGroupDetailsAsync(
                groupId, actorUserId, orgId, appId, fiscalYearId, cancellationToken);
        }
        catch
        {
            details = null;
        }

        var groupName = string.IsNullOrWhiteSpace(details?.GroupName)
            ? "group"
            : details!.GroupName.Trim();

        var payload = new
        {
            groupId,
            chatId = details?.ChatId,
            groupName,
            userId = result.UserId,
            isAdmin = result.IsAdmin,
            actorUserId,
            changedAt = DateTimeOffset.UtcNow
        };

        // Members who joined the group hub.
        await _hubContext.Clients
            .Group(ChatHub.ChatGroup(groupId))
            .SendAsync("GroupMemberAdminChanged", payload, cancellationToken);

        // Fan-out on personal hubs (info panel open / list Admin badge).
        var memberIds = details?.Members?
            .Where(m => m.IsActive)
            .Select(m => m.UserId)
            .Distinct()
            .ToList()
            ?? new List<long> { result.UserId, actorUserId };

        foreach (var memberId in memberIds)
        {
            if (memberId <= 0) continue;
            await _hubContext.Clients
                .Group(ChatHub.UserGroup(memberId))
                .SendAsync("GroupMemberAdminChanged", payload, cancellationToken);
        }

        // Bell for the target (not when you dismiss yourself).
        if (result.UserId > 0 && result.UserId != actorUserId)
        {
            var title = isAdmin ? "Now a group admin" : "No longer a group admin";
            var preview = isAdmin
                ? $"You are now an admin of \"{groupName}\""
                : $"You are no longer an admin of \"{groupName}\"";
            if (preview.Length > 200)
                preview = preview[..200];

            var referenceType = $"GROUP:{groupId}";
            var notification = await _chatRepository.CreateNotificationAsync(
                result.UserId,
                actorUserId,
                title,
                preview,
                groupId,
                orgId,
                appId,
                fiscalYearId,
                cancellationToken,
                referenceType: referenceType,
                notificationType: "GROUP",
                referenceEntity: "tab_groups");

            await _hubContext.Clients
                .Group(ChatHub.UserGroup(result.UserId))
                .SendAsync(
                    "ReceiveNotification",
                    new
                    {
                        notificationId = notification.NotificationId,
                        notificationType = notification.NotificationType ?? "GROUP",
                        title = notification.Title,
                        message = notification.Message,
                        referenceId = notification.ReferenceId,
                        referenceType = notification.ReferenceType ?? referenceType,
                        referenceEntity = notification.ReferenceEntity ?? "tab_groups",
                        senderUserId = notification.SenderUserId,
                        createdDate = notification.CreatedDate,
                        isRead = notification.IsRead,
                        groupId,
                        chatId = details?.ChatId,
                        groupName
                    },
                    cancellationToken);
        }
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
            ?? CurrentUserHelper.GetIntClaim(User, "fiscalYearId", "fiscal_year_id");

        return (resolvedOrg, resolvedApp, resolvedFy);
    }
}
