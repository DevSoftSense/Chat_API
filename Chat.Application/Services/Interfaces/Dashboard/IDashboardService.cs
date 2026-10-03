using Chat.Domain.DTOs.Dashboard;

namespace Chat.Application.Services.Interfaces.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponseDto> GetDashboardAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        IReadOnlyCollection<long> onlineUserIds,
        CancellationToken cancellationToken = default);
}
