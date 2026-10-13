using Chat.Domain.DTOs.Permissions;

namespace Chat.Infrastructure.Repositories.Interfaces.Permissions;

public interface IPermissionRepository
{
    Task<IReadOnlyList<PermissionRoleDto>> GetRolesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionTabDto>> GetByRoleAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        CancellationToken cancellationToken = default);

    Task SaveMatrixAsync(
        int orgId,
        int appId,
        int roleId,
        string roleCode,
        IReadOnlyList<PermissionMatrixSaveItem> rows,
        CancellationToken cancellationToken = default);
}
