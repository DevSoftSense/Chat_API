using Chat.Application.Services.Interfaces.Settings;
using Chat.Domain.DTOs.Settings;
using Chat.Domain.Exceptions.Messages;
using Chat_Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat_Api.Controllers.Settings;

[Authorize]
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;
    private readonly IConfiguration _configuration;

    public SettingsController(ISettingsService settingsService, IConfiguration configuration)
    {
        _settingsService = settingsService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.GetSettingsAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileSettingsRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.UpdateProfileAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, request, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("status")]
    public async Task<IActionResult> UpdateStatus(
        [FromBody] UpdateStatusSettingsRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.UpdateStatusAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, request, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("message-defaults")]
    public async Task<IActionResult> UpdateMessageDefaults(
        [FromBody] UpdateMessageDefaultsRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.UpdateMessageDefaultsAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, request, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("notifications")]
    public async Task<IActionResult> UpdateNotifications(
        [FromBody] UpdateNotificationSettingsRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.UpdateNotificationsAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, request, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("privacy")]
    public async Task<IActionResult> UpdatePrivacy(
        [FromBody] UpdatePrivacySettingsRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.UpdatePrivacyAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, request, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("storage")]
    public async Task<IActionResult> GetStorage(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (userId, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.GetStorageAsync(
                userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, tenant) = RequireUserAndTenant(orgId, appId, fiscalYearId);
            var result = await _settingsService.GetIntegrationsAsync(
                tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
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
