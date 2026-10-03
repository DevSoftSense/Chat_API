using Chat.Domain.DTOs.Dashboard;

namespace Chat.Infrastructure.Repositories.Interfaces.Dashboard;

public interface IDashboardRepository
{
    Task<DashboardResponseDto> GetDashboardAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        IReadOnlyCollection<long> onlineUserIds,
        CancellationToken cancellationToken = default);
}
