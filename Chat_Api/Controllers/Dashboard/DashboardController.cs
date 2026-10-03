using Chat.Application.Services.Interfaces.Dashboard;
using Chat.Domain.Exceptions.Messages;
using Chat_Api.Helpers;
using Chat_Api.Hubs.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat_Api.Controllers.Dashboard;

[Authorize]
[ApiController]
[Route("api/chat")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IConfiguration _configuration;

    public DashboardController(
        IDashboardService dashboardService,
        IConfiguration configuration)
    {
        _dashboardService = dashboardService;
        _configuration = configuration;
    }

    /// <summary>
    /// Full communication dashboard for the authenticated user (one round-trip).
    /// GET /api/chat/dashboard?orgId&amp;appId&amp;fiscalYearId
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            if (tenant.OrgId <= 0 || tenant.AppId <= 0)
                return BadRequest(new { message = "orgId and appId are required." });

            // Online presence from existing SignalR in-memory hub (no presence table).
            var onlineUserIds = ChatHub.GetOnlineUserIds();

            var result = await _dashboardService.GetDashboardAsync(
                userId.Value,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                onlineUserIds,
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
            ?? CurrentUserHelper.GetIntClaim(User, "fiscalYearId", "fiscal_year_id", "FiscalYearId");

        return (resolvedOrg, resolvedApp, resolvedFy);
    }
}
