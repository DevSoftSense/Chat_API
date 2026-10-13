using Chat.Application.Services.Interfaces.Permissions;
using Chat.Domain.DTOs.Permissions;
using Chat.Domain.Exceptions.Messages;
using Chat_Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat_Api.Controllers.Permissions;

[Authorize]
[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionService _permissionService;
    private readonly IConfiguration _configuration;

    public PermissionsController(IPermissionService permissionService, IConfiguration configuration)
    {
        _permissionService = permissionService;
        _configuration = configuration;
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _permissionService
                .GetRolesAsync(tenant.OrgId, tenant.AppId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("matrix")]
    public async Task<IActionResult> GetMatrix(
        [FromQuery] int? roleId,
        [FromQuery] string? roleCode,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _permissionService
                .GetMatrixAsync(tenant.OrgId, tenant.AppId, roleId, roleCode, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpGet("menu")]
    public async Task<IActionResult> GetMenu(
        [FromQuery] int? roleId,
        [FromQuery] string? roleCode,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var sessionCodes = CurrentUserHelper.GetRoleCodes(User);
            var sessionUserType = CurrentUserHelper.GetIdentityType(User);
            var result = await _permissionService
                .GetMenuAsync(
                    tenant.OrgId,
                    tenant.AppId,
                    roleId,
                    roleCode,
                    sessionCodes,
                    sessionUserType,
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    [HttpPut("matrix")]
    public async Task<IActionResult> SaveMatrix(
        [FromBody] PermissionMatrixSaveRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _permissionService
                .SaveMatrixAsync(tenant.OrgId, tenant.AppId, request, cancellationToken)
                .ConfigureAwait(false);
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
