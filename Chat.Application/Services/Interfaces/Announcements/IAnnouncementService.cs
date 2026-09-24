using Chat.Domain.DTOs.Announcements;

namespace Chat.Application.Services.Interfaces.Announcements;

public interface IAnnouncementService
{
    Task<AnnouncementListResult> GetAnnouncementsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        AnnouncementListQuery query, CancellationToken cancellationToken = default);

    Task<AnnouncementDto> GetByIdAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementDto> CreateAsync(
        long userId, CreateAnnouncementRequest request, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementDto> UpdateAsync(
        long announcementId, long userId, UpdateAnnouncementRequest request, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementDeleteResult> DeleteAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementMarkReadResult> MarkReadAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementPinResult> PinAsync(
        long announcementId, long userId, int orgId, int appId, bool pinned,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementCategoryDto>> GetCategoriesAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default);

    Task<AnnouncementStatsDto> GetStatsAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default);

    Task<AnnouncementUnreadCountDto> GetUnreadCountAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default);
}
