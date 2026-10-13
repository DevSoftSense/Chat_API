using Chat.Application.Services.Interfaces.Permissions;
using Chat.Domain.DTOs.Permissions;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Permissions;

namespace Chat.Application.Services.Classes.Permissions;

public sealed class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _repository;

    public PermissionService(IPermissionRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<PermissionRoleDto>> GetRolesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(orgId, appId);
        return _repository.GetRolesAsync(orgId, appId, cancellationToken);
    }

    public Task<IReadOnlyList<PermissionTabDto>> GetMatrixAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(orgId, appId);
        if (roleId is null or <= 0 && string.IsNullOrWhiteSpace(roleCode))
            throw new ChatOperationException("roleId or roleCode is required.", 400);

        return _repository.GetByRoleAsync(orgId, appId, roleId, roleCode, cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionTabDto>> GetMenuAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        IEnumerable<string>? sessionRoleCodes,
        string? sessionUserType = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(orgId, appId);

        var roles = await _repository.GetRolesAsync(orgId, appId, cancellationToken)
            .ConfigureAwait(false);
        var session = (sessionRoleCodes ?? [])
            .Select(c => (c ?? "").Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userType = (sessionUserType ?? "").Trim().ToUpperInvariant();
        var isPrimeOrAdminUser =
            userType is "PRIME" or "ADMIN" ||
            session.Any(s => s is "PRIME" or "ADMIN" || s.EndsWith("_ADMIN", StringComparison.Ordinal));

        var resolvedCode = string.IsNullOrWhiteSpace(roleCode) ? null : roleCode.Trim();
        var resolvedId = roleId is > 0 ? roleId : null;

        // Client may send a roleCode that does not belong to this session (e.g. forced Admin).
        // Only trust it when it matches session roles or the user is SoftOnCloud prime/admin.
        if (!string.IsNullOrWhiteSpace(resolvedCode) &&
            session.Count > 0 &&
            !session.Contains(resolvedCode.ToUpperInvariant()) &&
            !isPrimeOrAdminUser)
        {
            resolvedCode = null;
            resolvedId = null;
        }

        if ((resolvedId is null or <= 0) && string.IsNullOrWhiteSpace(resolvedCode))
        {
            PermissionRoleDto? match = null;

            if (session.Count > 0)
            {
                match = roles.FirstOrDefault(r =>
                    session.Contains(r.RoleCode.ToUpperInvariant()));
                if (match is null && session.Any(s => s.EndsWith("_ADMIN", StringComparison.Ordinal)))
                {
                    match = roles.FirstOrDefault(r =>
                        r.RoleCode.EndsWith("_ADMIN", StringComparison.OrdinalIgnoreCase));
                }
                if (match is null && session.Any(s => s.Contains("EMPLOYEE", StringComparison.Ordinal)))
                {
                    match = roles.FirstOrDefault(r =>
                        r.RoleCode.Contains("EMPLOYEE", StringComparison.OrdinalIgnoreCase));
                }
            }

            // SoftOnCloud PRIME / ADMIN → admin permission matrix (Master allowed)
            if (match is null && isPrimeOrAdminUser)
            {
                match = roles.FirstOrDefault(r =>
                    r.RoleCode.EndsWith("_ADMIN", StringComparison.OrdinalIgnoreCase));
            }

            // Non-admin session → never fall back to Admin matrix
            if (match is null && !isPrimeOrAdminUser)
            {
                match = roles.FirstOrDefault(r =>
                    !r.RoleCode.EndsWith("_ADMIN", StringComparison.OrdinalIgnoreCase));
            }

            if (match is null)
                return Array.Empty<PermissionTabDto>();

            resolvedId = match.RoleId;
            resolvedCode = match.RoleCode;
        }

        var matrix = await _repository
            .GetByRoleAsync(orgId, appId, resolvedId, resolvedCode, cancellationToken)
            .ConfigureAwait(false);

        // Sidebar: viewable + active. Keep Master parent (no URL) so UI can render section.
        return matrix
            .Where(t => t.CanView && t.IsActive)
            .Where(t =>
                !string.Equals(t.TabCode, "CHAT_LOGOUT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.SortOrder)
            .ToList();
    }

    public async Task<IReadOnlyList<PermissionTabDto>> SaveMatrixAsync(
        int orgId,
        int appId,
        PermissionMatrixSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(orgId, appId);
        if (request is null || request.RoleId <= 0)
            throw new ChatOperationException("roleId is required.", 400);
        if (request.Rows is null || request.Rows.Count == 0)
            throw new ChatOperationException("At least one permission row is required.", 400);

        var roleCode = string.IsNullOrWhiteSpace(request.RoleCode)
            ? $"ROLE_{request.RoleId}"
            : request.RoleCode.Trim();

        await _repository
            .SaveMatrixAsync(orgId, appId, request.RoleId, roleCode, request.Rows, cancellationToken)
            .ConfigureAwait(false);

        return await _repository
            .GetByRoleAsync(orgId, appId, request.RoleId, roleCode, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureTenant(int orgId, int appId)
    {
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required", 400);
    }
}
