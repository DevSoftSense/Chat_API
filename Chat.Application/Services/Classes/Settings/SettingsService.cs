using Chat.Application.Services.Classes.Organisation;
using Chat.Application.Services.Interfaces.Settings;
using Chat.Domain.DTOs.Settings;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Settings;

namespace Chat.Application.Services.Classes.Settings;

public sealed class SettingsService : ISettingsService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Online", "Away", "Busy", "Offline"
    };

    private static readonly HashSet<string> AllowedWhoCanMessage = new(StringComparer.OrdinalIgnoreCase)
    {
        "Everyone", "Contacts", "Nobody"
    };

    private static readonly HashSet<string> AllowedLastSeen = new(StringComparer.OrdinalIgnoreCase)
    {
        "Everyone", "ContactsOnly", "Nobody"
    };

    private readonly ISettingsRepository _repository;
    private readonly IAttachmentFileSizeResolver _attachmentFileSizeResolver;

    public SettingsService(
        ISettingsRepository repository,
        IAttachmentFileSizeResolver attachmentFileSizeResolver)
    {
        _repository = repository;
        _attachmentFileSizeResolver = attachmentFileSizeResolver;
    }

    public async Task<ChatSettingsDto> GetSettingsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);

        var profile = await _repository.GetEmployeeProfileAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false)
            ?? new SettingsProfileDto { UserId = userId };

        var userSettings = await _repository.GetChatUserSettingsAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false);
        var messageTypes = await _repository.GetActiveMessageTypesAsync(orgId, appId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);
        var notifications = await _repository.GetNotificationSettingsAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false);
        var integrations = await _repository.GetIntegrationsAsync(orgId, appId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);
        var storage = await BuildStorageAsync(userId, orgId, appId, cancellationToken).ConfigureAwait(false);

        ApplyUserSettingsToProfile(profile, userSettings);

        var defaultTypeId = GetInt(userSettings, "defaultMessageTypeId", messageTypes.FirstOrDefault()?.MessageTypeId ?? 0);

        return new ChatSettingsDto
        {
            Profile = profile,
            MessageDefaults = new MessageDefaultsDto
            {
                MessageTypes = messageTypes,
                DefaultMessageTypeId = defaultTypeId,
                RequestReadReceipt = GetBool(userSettings, "requestReadReceipt", true),
                AutoSaveDraft = GetBool(userSettings, "autoSaveDraft", false)
            },
            Notifications = notifications,
            Status = new StatusSettingsDto
            {
                Status = GetString(userSettings, "status", "Online"),
                StatusMessage = GetString(userSettings, "statusMessage", string.Empty)
            },
            Privacy = new PrivacySettingsDto
            {
                WhoCanMessage = GetString(userSettings, "whoCanMessage", "Everyone"),
                ShowLastSeen = GetString(userSettings, "showLastSeen", "ContactsOnly"),
                BlockListAvailable = false
            },
            Storage = storage,
            Integrations = integrations
        };
    }

    public async Task<SettingsProfileDto> UpdateProfileAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateProfileSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ChatOperationException("fullName is required.");

        var language = (request.Language ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(language))
            throw new ChatOperationException("language is required.");

        var dateFormat = (request.DateFormat ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dateFormat))
            throw new ChatOperationException("dateFormat is required.");

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && phone.Length > 30)
            throw new ChatOperationException("phone is invalid.");

        var photoUrl = string.IsNullOrWhiteSpace(request.PhotoUrl) ? null : request.PhotoUrl.Trim();

        // Employee row may be missing / user_id NULL in SoftOnCloud HR tables.
        // Never block Settings prefs — always save ChatUser keys.
        await _repository.TryUpdateEmployeeProfileAsync(
            userId, orgId, appId, fullName, phone, photoUrl, cancellationToken).ConfigureAwait(false);

        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "fullName", fullName, "string", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "phone", phone ?? string.Empty, "string", cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(photoUrl))
        {
            await _repository.UpsertChatUserSettingAsync(
                userId, orgId, appId, fiscalYearId, "photoUrl", photoUrl, "string", cancellationToken)
                .ConfigureAwait(false);
        }
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "language", language, "string", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "dateFormat", dateFormat, "string", cancellationToken)
            .ConfigureAwait(false);

        var profile = await _repository.GetEmployeeProfileAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false)
            ?? new SettingsProfileDto { UserId = userId };

        profile.FullName = fullName;
        if (phone is not null) profile.Phone = phone;
        if (!string.IsNullOrWhiteSpace(photoUrl)) profile.PhotoUrl = photoUrl;

        var userSettings = await _repository.GetChatUserSettingsAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false);
        ApplyUserSettingsToProfile(profile, userSettings);
        return profile;
    }

    public async Task<StatusSettingsDto> UpdateStatusAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateStatusSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");

        var status = (request.Status ?? string.Empty).Trim();
        if (!AllowedStatuses.Contains(status))
            throw new ChatOperationException("status must be Online, Away, Busy, or Offline.");

        // Normalize casing to canonical values
        status = AllowedStatuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));
        var message = (request.StatusMessage ?? string.Empty).Trim();
        if (message.Length > 200)
            throw new ChatOperationException("statusMessage cannot exceed 200 characters.");

        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "status", status, "string", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "statusMessage", message, "string", cancellationToken)
            .ConfigureAwait(false);

        return new StatusSettingsDto { Status = status, StatusMessage = message };
    }

    public async Task<MessageDefaultsDto> UpdateMessageDefaultsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateMessageDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");

        var valid = await _repository.IsActiveMessageTypeAsync(
            request.DefaultMessageTypeId, orgId, appId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);
        if (!valid)
            throw new ChatOperationException("defaultMessageTypeId is invalid or inactive.");

        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId,
            "defaultMessageTypeId", request.DefaultMessageTypeId.ToString(), "number", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId,
            "requestReadReceipt", request.RequestReadReceipt ? "true" : "false", "boolean", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId,
            "autoSaveDraft", request.AutoSaveDraft ? "true" : "false", "boolean", cancellationToken)
            .ConfigureAwait(false);

        var types = await _repository.GetActiveMessageTypesAsync(orgId, appId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);

        return new MessageDefaultsDto
        {
            MessageTypes = types,
            DefaultMessageTypeId = request.DefaultMessageTypeId,
            RequestReadReceipt = request.RequestReadReceipt,
            AutoSaveDraft = request.AutoSaveDraft
        };
    }

    public async Task<NotificationSettingsDto> UpdateNotificationsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");

        await _repository.UpsertNotificationSettingsAsync(
            userId, orgId, appId, fiscalYearId, request, cancellationToken).ConfigureAwait(false);

        return await _repository.GetNotificationSettingsAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PrivacySettingsDto> UpdatePrivacyAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdatePrivacySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        if (request is null)
            throw new ChatOperationException("Request body is required.");

        var who = (request.WhoCanMessage ?? string.Empty).Trim();
        var lastSeen = (request.ShowLastSeen ?? string.Empty).Trim();

        if (!AllowedWhoCanMessage.Contains(who))
            throw new ChatOperationException("whoCanMessage must be Everyone, Contacts, or Nobody.");
        if (!AllowedLastSeen.Contains(lastSeen))
            throw new ChatOperationException("showLastSeen must be Everyone, ContactsOnly, or Nobody.");

        who = AllowedWhoCanMessage.First(s => s.Equals(who, StringComparison.OrdinalIgnoreCase));
        lastSeen = AllowedLastSeen.First(s => s.Equals(lastSeen, StringComparison.OrdinalIgnoreCase));

        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "whoCanMessage", who, "string", cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpsertChatUserSettingAsync(
            userId, orgId, appId, fiscalYearId, "showLastSeen", lastSeen, "string", cancellationToken)
            .ConfigureAwait(false);

        return new PrivacySettingsDto
        {
            WhoCanMessage = who,
            ShowLastSeen = lastSeen,
            BlockListAvailable = false
        };
    }

    public Task<StorageSettingsDto> GetStorageAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        EnsureUser(userId);
        EnsureTenant(orgId, appId);
        return BuildStorageAsync(userId, orgId, appId, cancellationToken);
    }

    public async Task<List<IntegrationSettingsDto>> GetIntegrationsAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(orgId, appId);
        return await _repository.GetIntegrationsAsync(orgId, appId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StorageSettingsDto> BuildStorageAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken)
    {
        var limitMB = await _repository.GetOrgSettingIntAsync(
            orgId, appId, OrganisationSettingService.ChatMaxFileSizeMBKey, cancellationToken)
            .ConfigureAwait(false)
            ?? OrganisationSettingService.DefaultChatMaxFileSizeMB;

        if (limitMB <= 0)
            limitMB = OrganisationSettingService.DefaultChatMaxFileSizeMB;

        var paths = await _repository.GetUserAttachmentPathsAsync(userId, orgId, appId, cancellationToken)
            .ConfigureAwait(false);
        var usedBytes = _attachmentFileSizeResolver.SumExistingFileBytes(paths);
        var usedMB = Math.Round((decimal)usedBytes / (1024m * 1024m), 2);
        var limitBytes = (long)limitMB * 1024L * 1024L;
        var percentage = limitBytes <= 0
            ? 0
            : Math.Round((decimal)usedBytes * 100m / limitBytes, 2);

        return new StorageSettingsDto
        {
            UsedBytes = usedBytes,
            UsedMB = usedMB,
            LimitMB = limitMB,
            Percentage = Math.Clamp(percentage, 0, 100)
        };
    }

    private static void ApplyUserSettingsToProfile(
        SettingsProfileDto profile, Dictionary<string, string> userSettings)
    {
        profile.Language = GetString(userSettings, "language", "en");
        profile.DateFormat = GetString(userSettings, "dateFormat", "DD MMM YYYY");
        if (string.IsNullOrWhiteSpace(profile.Timezone))
            profile.Timezone = "UTC";

        // When HR employee row is missing, ChatUser keys hold display profile.
        if (string.IsNullOrWhiteSpace(profile.FullName))
            profile.FullName = GetString(userSettings, "fullName", profile.FullName);
        if (string.IsNullOrWhiteSpace(profile.Phone))
            profile.Phone = GetString(userSettings, "phone", profile.Phone);
        if (string.IsNullOrWhiteSpace(profile.PhotoUrl))
        {
            var photo = GetString(userSettings, "photoUrl", string.Empty);
            if (!string.IsNullOrWhiteSpace(photo))
                profile.PhotoUrl = photo;
        }
    }

    private static string GetString(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static bool GetBool(Dictionary<string, string> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        if (bool.TryParse(value, out var b)) return b;
        if (value is "1" or "yes") return true;
        if (value is "0" or "no") return false;
        return fallback;
    }

    private static int GetInt(Dictionary<string, string> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        return int.TryParse(value, out var n) ? n : fallback;
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
