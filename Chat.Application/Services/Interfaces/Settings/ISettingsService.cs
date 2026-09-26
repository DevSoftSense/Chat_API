using Chat.Domain.DTOs.Settings;

namespace Chat.Application.Services.Interfaces.Settings;

public interface ISettingsService
{
    Task<ChatSettingsDto> GetSettingsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<SettingsProfileDto> UpdateProfileAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateProfileSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<StatusSettingsDto> UpdateStatusAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateStatusSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<MessageDefaultsDto> UpdateMessageDefaultsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateMessageDefaultsRequest request,
        CancellationToken cancellationToken = default);

    Task<NotificationSettingsDto> UpdateNotificationsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<PrivacySettingsDto> UpdatePrivacyAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdatePrivacySettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageSettingsDto> GetStorageAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<List<IntegrationSettingsDto>> GetIntegrationsAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);
}
