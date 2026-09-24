using Chat.Application.Services.Interfaces.Announcements;
using Chat.Domain.DTOs.Announcements;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Announcements;

namespace Chat.Application.Services.Classes.Announcements;

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly IAnnouncementRepository _repository;

    public AnnouncementService(IAnnouncementRepository repository)
    {
        _repository = repository;
    }

    public Task<AnnouncementListResult> GetAnnouncementsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        AnnouncementListQuery query, CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        return _repository.GetAnnouncementsAsync(userId, orgId, appId, fiscalYearId, query, cancellationToken);
    }

    public Task<AnnouncementDto> GetByIdAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (announcementId <= 0)
            throw new ChatOperationException("announcementId is required.");
        return _repository.GetByIdAsync(announcementId, userId, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementDto> CreateAsync(
        long userId, CreateAnnouncementRequest request, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ChatOperationException("title is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ChatOperationException("description is required.");

        return _repository.CreateAsync(
            userId, request.PostedByName, request, orgId, appId, fiscalYearId, cancellationToken);
    }

    public Task<AnnouncementDto> UpdateAsync(
        long announcementId, long userId, UpdateAnnouncementRequest request, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (announcementId <= 0)
            throw new ChatOperationException("announcementId is required.");
        if (request is null)
            throw new ChatOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ChatOperationException("title is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ChatOperationException("description is required.");

        return _repository.UpdateAsync(announcementId, userId, request, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementDeleteResult> DeleteAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (announcementId <= 0)
            throw new ChatOperationException("announcementId is required.");
        return _repository.DeleteAsync(announcementId, userId, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementMarkReadResult> MarkReadAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (announcementId <= 0)
            throw new ChatOperationException("announcementId is required.");
        return _repository.MarkReadAsync(announcementId, userId, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementPinResult> PinAsync(
        long announcementId, long userId, int orgId, int appId, bool pinned,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (announcementId <= 0)
            throw new ChatOperationException("announcementId is required.");
        return _repository.PinAsync(announcementId, userId, orgId, appId, pinned, cancellationToken);
    }

    public Task<IReadOnlyList<AnnouncementCategoryDto>> GetCategoriesAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        return _repository.GetCategoriesAsync(userId, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementStatsDto> GetStatsAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        return _repository.GetStatsAsync(userId, orgId, appId, cancellationToken);
    }

    public Task<AnnouncementUnreadCountDto> GetUnreadCountAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        return _repository.GetUnreadCountAsync(userId, orgId, appId, cancellationToken);
    }

    private static void EnsureUser(long userId)
    {
        if (userId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
    }

    private static void EnsureTenant(int orgId, int appId)
    {
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
    }
}
