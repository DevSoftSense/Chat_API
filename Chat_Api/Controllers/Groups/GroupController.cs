using Chat.Application.Services.Interfaces.Groups;
using Chat.Domain.DTOs.Groups;
using Chat.Domain.Exceptions.Messages;
using Chat_Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Chat_Api.Controllers.Groups;

[Authorize]
[ApiController]
[Route("api/chat")]
public sealed class GroupController : ControllerBase
{
    private readonly IGroupService _groupService;
    private readonly IConfiguration _configuration;

    public GroupController(IGroupService groupService, IConfiguration configuration)
    {
        _groupService = groupService;
        _configuration = configuration;
    }

    /// <summary>
    /// Create a group. Creator (JWT) becomes admin; memberUserIds are SoftOnCloud user ids.
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
