using Chat.Domain.DTOs.Settings;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Settings;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Settings;

/// <summary>
/// Settings data access using existing SoftOnCloud tables only (no new tables).
/// User prefs: tab_organisation_setting (setting_group=ChatUser, key={userId}.{suffix}).
/// Notifications: tab_notification_settings (channel + is_enabled).
/// Message types: tab_message_type_master (Excel alias: message_types).
/// Messages/attachments: tab_messages (Excel alias: messages).
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    public const string ChatUserGroup = "ChatUser";
    public const string ChatMaxFileSizeMBKey = "ChatMaxFileSizeMB";

    public static class NotifChannels
    {
        public const string MessageReceived = "message_received";
        public const string GroupMessages = "group_messages";
        public const string Announcements = "announcements";
        public const string Sound = "sound_enabled";
        public const string Desktop = "desktop_enabled";
        public const string Email = "email_enabled";
        public const string MobilePush = "mobile_push_enabled";
    }

    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(DatabaseHelper databaseHelper, ILogger<SettingsRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<SettingsProfileDto?> GetEmployeeProfileAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    e.employee_id,
    e.user_id,
    COALESCE(e.full_name, '') AS full_name,
    COALESCE(e.email, '') AS email,
    COALESCE(e.phone, '') AS phone,
    e.photo_url,
    COALESCE(
        NULLIF(TRIM(erm.role_name), ''),
        NULLIF(TRIM(rm.role_name), ''),
        ''
    ) AS role_name,
    COALESCE(NULLIF(TRIM(d.department_name), ''), '') AS department_name,
    COALESCE(NULLIF(TRIM(b.timezone), ''), '') AS timezone
FROM public.tab_employee_master e
LEFT JOIN public.tab_employee_role_master erm
    ON erm.staff_role_id = e.staff_role_id
   AND erm.org_id = e.org_id
   AND erm.app_id = e.app_id
LEFT JOIN public.tab_user_role ur
    ON ur.user_id = e.user_id
   AND ur.org_id = e.org_id
   AND ur.app_id = e.app_id
   AND COALESCE(ur.is_active, 1) = 1
LEFT JOIN public.tab_role_master rm
    ON rm.role_id = ur.role_id
   AND rm.org_id = ur.org_id
   AND rm.app_id = ur.app_id
   AND COALESCE(rm.is_active, true) = true
   AND COALESCE(rm.is_deleted, false) = false
LEFT JOIN public.tab_department_master d
    ON d.department_id = e.department_id
   AND d.org_id = e.org_id
   AND d.app_id = e.app_id
LEFT JOIN public.tab_branch_master b
    ON b.branch_id = e.branch_id
   AND b.org_id = e.org_id
   AND b.app_id = e.app_id
WHERE e.user_id = @user_id
  AND e.org_id = @org_id
  AND (e.app_id = @app_id OR e.app_id IS NULL)
ORDER BY CASE WHEN e.app_id = @app_id THEN 0 ELSE 1 END, e.employee_id ASC
LIMIT 1;";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("user_id", (int)userId);
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new SettingsProfileDto
            {
                EmployeeId = reader.GetInt32(reader.GetOrdinal("employee_id")),
                UserId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("user_id"))),
                FullName = reader.GetString(reader.GetOrdinal("full_name")),
                Email = reader.GetString(reader.GetOrdinal("email")),
                Phone = reader.GetString(reader.GetOrdinal("phone")),
                PhotoUrl = reader.IsDBNull(reader.GetOrdinal("photo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("photo_url")),
                Role = reader.GetString(reader.GetOrdinal("role_name")),
                Department = reader.GetString(reader.GetOrdinal("department_name")),
                Timezone = reader.GetString(reader.GetOrdinal("timezone"))
            };
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<bool> TryUpdateEmployeeProfileAsync(
        long userId, int orgId, int appId,
        string fullName, string? phone, string? photoUrl,
        CancellationToken cancellationToken = default)
    {
        // SoftOnCloud often has employee rows with NULL user_id or different app_id.
        // Prefer exact match; fall back to org + user_id only.
        const string sqlExact = @"
UPDATE public.tab_employee_master
SET full_name = @full_name,
    phone = @phone,
    photo_url = COALESCE(@photo_url, photo_url),
    updated_on = NOW()
WHERE user_id = @user_id
  AND org_id = @org_id
  AND app_id = @app_id;";

        const string sqlOrgUser = @"
UPDATE public.tab_employee_master
SET full_name = @full_name,
    phone = @phone,
    photo_url = COALESCE(@photo_url, photo_url),
    updated_on = NOW()
WHERE user_id = @user_id
  AND org_id = @org_id;";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            async Task<int> Exec(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };
                command.Parameters.AddWithValue("full_name", fullName);
                command.Parameters.AddWithValue("phone", (object?)phone ?? DBNull.Value);
                command.Parameters.AddWithValue("photo_url", (object?)photoUrl ?? DBNull.Value);
                command.Parameters.AddWithValue("user_id", (int)userId);
                command.Parameters.AddWithValue("org_id", orgId);
                command.Parameters.AddWithValue("app_id", appId);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var rows = await Exec(sqlExact).ConfigureAwait(false);
            if (rows <= 0)
                rows = await Exec(sqlOrgUser).ConfigureAwait(false);

            return rows > 0;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<Dictionary<string, string>> GetChatUserSettingsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{userId}.";
        const string sql = @"
SELECT setting_key, setting_value
FROM public.tab_organisation_setting
WHERE org_id = @org_id
  AND app_id = @app_id
  AND setting_group = @setting_group
  AND setting_key LIKE @key_prefix;";

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("setting_group", ChatUserGroup);
            command.Parameters.AddWithValue("key_prefix", prefix + "%");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = key[prefix.Length..];
                    result[suffix] = value;
                }
            }

            return result;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task UpsertChatUserSettingAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        string keySuffix, string value, string dataType,
        CancellationToken cancellationToken = default)
    {
        var settingKey = $"{userId}.{keySuffix}";
        const string sql = @"
INSERT INTO public.tab_organisation_setting (
    org_id, app_id, setting_key, setting_value, setting_group, data_type,
    updated_by, updated_on, fiscal_year_id
) VALUES (
    @org_id, @app_id, @setting_key, @setting_value, @setting_group, @data_type,
    @updated_by, NOW(), @fiscal_year_id
)
ON CONFLICT (org_id, app_id, setting_key)
DO UPDATE SET
    setting_value = EXCLUDED.setting_value,
    setting_group = EXCLUDED.setting_group,
    data_type = EXCLUDED.data_type,
    updated_by = EXCLUDED.updated_by,
    updated_on = NOW(),
    fiscal_year_id = COALESCE(EXCLUDED.fiscal_year_id, public.tab_organisation_setting.fiscal_year_id);";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("setting_key", settingKey);
            command.Parameters.AddWithValue("setting_value", value);
            command.Parameters.AddWithValue("setting_group", ChatUserGroup);
            command.Parameters.AddWithValue("data_type", dataType);
            command.Parameters.AddWithValue("updated_by", (int)userId);
            command.Parameters.AddWithValue("fiscal_year_id", (object?)fiscalYearId ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<List<MessageTypeOptionDto>> GetActiveMessageTypesAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT message_type_id, type_name, description
FROM public.tab_message_type_master
WHERE org_id = @org_id
  AND app_id = @app_id
  AND COALESCE(is_active, true) = true
  AND (fiscal_year_id IS NULL OR fiscal_year_id IS NOT DISTINCT FROM @fiscal_year_id)
ORDER BY message_type_id ASC;";

        var list = new List<MessageTypeOptionDto>();
        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("fiscal_year_id", (object?)fiscalYearId ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new MessageTypeOptionDto
                {
                    MessageTypeId = reader.GetInt32(0),
                    TypeName = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }

            return list;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<bool> IsActiveMessageTypeAsync(
        int messageTypeId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT 1
FROM public.tab_message_type_master
WHERE message_type_id = @message_type_id
  AND org_id = @org_id
  AND app_id = @app_id
  AND COALESCE(is_active, true) = true
  AND (fiscal_year_id IS NULL OR fiscal_year_id IS NOT DISTINCT FROM @fiscal_year_id)
LIMIT 1;";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("message_type_id", messageTypeId);
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("fiscal_year_id", (object?)fiscalYearId ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is not null and not DBNull;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT channel, is_enabled
FROM public.tab_notification_settings
WHERE user_id = @user_id
  AND org_id = @org_id
  AND app_id = @app_id;";

        var dto = new NotificationSettingsDto();

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("user_id", (int)userId);
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var channel = reader.GetString(0);
                var enabled = reader.GetBoolean(1);
                ApplyChannel(dto, channel, enabled);
            }

            return dto;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task UpsertNotificationSettingsAsync(
        long userId, int orgId, int appId, int? fiscalYearId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var pairs = new (string Channel, bool Enabled)[]
        {
            (NotifChannels.MessageReceived, request.MessageReceived),
            (NotifChannels.GroupMessages, request.GroupMessages),
            (NotifChannels.Announcements, request.Announcements),
            (NotifChannels.Sound, request.SoundEnabled),
            (NotifChannels.Desktop, request.DesktopEnabled),
            (NotifChannels.Email, request.EmailEnabled),
            (NotifChannels.MobilePush, request.MobilePushEnabled)
        };

        const string sql = @"
INSERT INTO public.tab_notification_settings (
    user_id, app_id, channel, is_enabled, org_id, fiscal_year_id
) VALUES (
    @user_id, @app_id, @channel, @is_enabled, @org_id, @fiscal_year_id
)
ON CONFLICT (user_id, app_id, channel)
DO UPDATE SET
    is_enabled = EXCLUDED.is_enabled,
    org_id = EXCLUDED.org_id,
    fiscal_year_id = COALESCE(EXCLUDED.fiscal_year_id, public.tab_notification_settings.fiscal_year_id);";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var (channel, enabled) in pairs)
            {
                await using var command = new NpgsqlCommand(sql, connection, tx)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };
                command.Parameters.AddWithValue("user_id", (int)userId);
                command.Parameters.AddWithValue("app_id", appId);
                command.Parameters.AddWithValue("channel", channel);
                command.Parameters.AddWithValue("is_enabled", enabled);
                command.Parameters.AddWithValue("org_id", orgId);
                command.Parameters.AddWithValue("fiscal_year_id", (object?)fiscalYearId ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<List<string>> GetUserAttachmentPathsAsync(
        long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT attachment_path_1, attachment_path_2, attachment_path_3,
       attachment_path_4, attachment_path_5
FROM public.tab_messages
WHERE org_id = @org_id
  AND app_id = @app_id
  AND sender_user_id = @user_id
  AND COALESCE(delete_flag, 0) = 0
  AND deleted_at IS NULL
  AND (
        NULLIF(TRIM(attachment_path_1), '') IS NOT NULL
     OR NULLIF(TRIM(attachment_path_2), '') IS NOT NULL
     OR NULLIF(TRIM(attachment_path_3), '') IS NOT NULL
     OR NULLIF(TRIM(attachment_path_4), '') IS NOT NULL
     OR NULLIF(TRIM(attachment_path_5), '') IS NOT NULL
  );";

        var paths = new List<string>();
        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.Add("user_id", NpgsqlDbType.Bigint).Value = userId;
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                for (var i = 0; i < 5; i++)
                {
                    if (reader.IsDBNull(i)) continue;
                    var path = reader.GetString(i)?.Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
            }

            return paths;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    public async Task<int?> GetOrgSettingIntAsync(
        int orgId, int appId, string settingKey,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT setting_value
FROM public.tab_organisation_setting
WHERE org_id = @org_id
  AND app_id = @app_id
  AND setting_key = @setting_key
LIMIT 1;";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("setting_key", settingKey);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null or DBNull) return null;
            return int.TryParse(Convert.ToString(result), out var n) ? n : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading org setting {Key}", settingKey);
            return null;
        }
    }

    public async Task<List<IntegrationSettingsDto>> GetIntegrationsAsync(
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    p.provider_id,
    COALESCE(p.name, '') AS name,
    COALESCE(p.category_name, '') AS category_name,
    p.logo_url,
    COALESCE(oi.status, 'Disconnected') AS status,
    oi.connected_on,
    oi.last_synced_on
FROM public.tab_integration_provider p
LEFT JOIN public.tab_organisation_integration oi
    ON oi.provider_id = p.provider_id
   AND oi.org_id = @org_id
   AND oi.app_id = @app_id
   AND (oi.fiscal_year_id IS NULL OR oi.fiscal_year_id IS NOT DISTINCT FROM @fiscal_year_id)
WHERE (p.org_id IS NULL OR p.org_id = @org_id)
  AND (p.app_id IS NULL OR p.app_id = @app_id)
ORDER BY p.name ASC;";

        var list = new List<IntegrationSettingsDto>();
        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("fiscal_year_id", (object?)fiscalYearId ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new IntegrationSettingsDto
                {
                    ProviderId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    LogoUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Status = reader.GetString(4),
                    ConnectedOn = reader.IsDBNull(5)
                        ? null
                        : ToOffset(reader.GetValue(5)),
                    LastSyncedOn = reader.IsDBNull(6)
                        ? null
                        : ToOffset(reader.GetValue(6))
                });
            }

            return list;
        }
        catch (PostgresException ex)
        {
            throw MapPg(ex);
        }
    }

    private static void ApplyChannel(NotificationSettingsDto dto, string channel, bool enabled)
    {
        switch (channel.Trim().ToLowerInvariant())
        {
            case NotifChannels.MessageReceived:
            case "chat.message":
                dto.MessageReceived = enabled;
                break;
            case NotifChannels.GroupMessages:
            case "chat.group":
                dto.GroupMessages = enabled;
                break;
            case NotifChannels.Announcements:
            case "chat.announcement":
                dto.Announcements = enabled;
                break;
            case NotifChannels.Sound:
            case "sound":
                dto.SoundEnabled = enabled;
                break;
            case NotifChannels.Desktop:
            case "desktop":
                dto.DesktopEnabled = enabled;
                break;
            case NotifChannels.Email:
            case "email":
                dto.EmailEnabled = enabled;
                break;
            case NotifChannels.MobilePush:
            case "mobile_push":
                dto.MobilePushEnabled = enabled;
                break;
        }
    }

    private static DateTimeOffset? ToOffset(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => null
    };

    private static ChatOperationException MapPg(PostgresException ex)
    {
        var status = ex.SqlState switch
        {
            "P0002" => 404,
            "42501" => 403,
            "22023" => 400,
            _ => 500
        };
        return new ChatOperationException(ex.MessageText, status);
    }
}
