using Chat.Domain.DTOs.Permissions;

namespace Chat.Application.Services.Interfaces.Permissions;

public interface IPermissionService
{
    Task<IReadOnlyList<PermissionRoleDto>> GetRolesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionTabDto>> GetMatrixAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionTabDto>> GetMenuAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        IEnumerable<string>? sessionRoleCodes,
        string? sessionUserType = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionTabDto>> SaveMatrixAsync(
        int orgId,
        int appId,
        PermissionMatrixSaveRequest request,
        CancellationToken cancellationToken = default);
}
