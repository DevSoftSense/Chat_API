using Chat.Domain.DTOs.Settings;

namespace Chat.Infrastructure.Repositories.Interfaces.Settings;

public interface ISettingsRepository
{
    Task<SettingsProfileDto?> GetEmployeeProfileAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<bool> TryUpdateEmployeeProfileAsync(
        long userId, int orgId, int appId,
        string fullName, string? phone, string? photoUrl,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, string>> GetChatUserSettingsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task UpsertChatUserSettingAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        string keySuffix, string value, string dataType,
        CancellationToken cancellationToken = default);

    Task<List<MessageTypeOptionDto>> GetActiveMessageTypesAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveMessageTypeAsync(
        int messageTypeId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<NotificationSettingsDto> GetNotificationSettingsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task UpsertNotificationSettingsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetUserAttachmentPathsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default);

    Task<int?> GetOrgSettingIntAsync(
        int orgId, int appId, string settingKey,
        CancellationToken cancellationToken = default);

    Task<List<IntegrationSettingsDto>> GetIntegrationsAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default);
}
