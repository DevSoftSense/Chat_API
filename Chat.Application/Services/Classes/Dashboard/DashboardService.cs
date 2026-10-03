using Chat.Application.Services.Interfaces.Dashboard;
using Chat.Domain.DTOs.Dashboard;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Dashboard;

namespace Chat.Application.Services.Classes.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _repository;

    public DashboardService(IDashboardRepository repository)
    {
        _repository = repository;
    }

    public Task<DashboardResponseDto> GetDashboardAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        IReadOnlyCollection<long> onlineUserIds,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ChatOperationException("Authenticated user id claim is missing.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.", 400);

        return _repository.GetDashboardAsync(
            userId,
            orgId,
            appId,
            fiscalYearId,
            onlineUserIds ?? Array.Empty<long>(),
            cancellationToken);
    }
}
