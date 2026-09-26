using Chat.Domain.DTOs.Groups;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Groups;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Groups;

public sealed class GroupRepository : IGroupRepository
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<GroupRepository> _logger;

    public GroupRepository(DatabaseHelper databaseHelper, ILogger<GroupRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<ChatGroupDto> CreateGroupAsync(
        string groupName,
        string groupCode,
        string? description,
        long createdByUserId,
        IReadOnlyList<long> memberUserIds,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT group_id, chat_id, group_name, group_code, description,
                         created_by, created_at, is_active, is_admin
                  FROM public.fn_chat_create_group(
                      @p_group_name, @p_group_code, @p_description, @p_created_by,
                      @p_org_id, @p_app_id, @p_fiscal_year_id, @p_member_user_ids);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_group_name", NpgsqlDbType.Varchar).Value = groupName;
            command.Parameters.Add("p_group_code", NpgsqlDbType.Varchar).Value = groupCode;
            command.Parameters.Add("p_description", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim();
            command.Parameters.Add("p_created_by", NpgsqlDbType.Bigint).Value = createdByUserId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value = fiscalYearId;
            command.Parameters.Add("p_member_user_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value =
                memberUserIds.Count == 0 ? Array.Empty<long>() : memberUserIds.ToArray();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Group was not created.", 500);

            return MapChatGroup(reader, includeIsAdmin: true);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in CreateGroupAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<IReadOnlyList<ChatGroupDto>> GetUserGroupsAsync(
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT f.group_id, f.chat_id, f.group_name, f.group_code, f.description,
                     f.created_by, f.created_at, f.is_admin, f.is_active, g.profile_pic,
                     (
                         SELECT COUNT(*)::integer
                         FROM public.tab_messages msg
                         WHERE msg.group_id = f.group_id
                           AND msg.sender_user_id IS DISTINCT FROM @p_user_id
                           AND COALESCE(msg.delete_flag, 0) = 0
                           AND msg.deleted_at IS NULL
                           AND COALESCE(msg.is_draft, false) = false
                           AND msg.sent_at IS NOT NULL
                           AND NOT EXISTS (
                               SELECT 1
                               FROM public.tab_message_receipts r
                               WHERE r.message_id = msg.message_id
                                 AND r.user_id = @p_user_id
                                 AND r.read_at IS NOT NULL
                           )
                     ) AS unread_count
              FROM public.fn_chat_get_user_groups(
                  @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id) f
              INNER JOIN public.tab_groups g ON g.group_id = f.group_id;";

        try
        {
            return await _databaseHelper.ExecuteRawQueryAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("p_user_id", userId);
                    command.Parameters.AddWithValue("p_org_id", orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);
                },
                reader => MapChatGroup(reader, includeIsAdmin: true),
                cancellationToken);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetUserGroupsAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ChatGroupDetailsDto> GetGroupDetailsAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT f.group_id, f.chat_id, f.group_name, f.group_code, f.description,
                     f.created_by, f.is_active, f.created_at, g.profile_pic,
                     f.member_user_id, f.member_is_admin, f.member_joined_at, f.member_is_active
              FROM public.fn_chat_get_group_details(
                  @p_group_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id) f
              INNER JOIN public.tab_groups g ON g.group_id = f.group_id;";

        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.AddWithValue("p_user_id", userId);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            ChatGroupDetailsDto? details = null;
            var members = new List<ChatGroupMemberDto>();

            while (await reader.ReadAsync(cancellationToken))
            {
                details ??= new ChatGroupDetailsDto
                {
                    GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
                    ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("chat_id")),
                    GroupName = reader.GetString(reader.GetOrdinal("group_name")),
                    GroupCode = reader.GetString(reader.GetOrdinal("group_code")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("description")),
                    ProfilePic = ReadOptionalString(reader, "profile_pic"),
                    CreatedBy = reader.GetInt32(reader.GetOrdinal("created_by")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                    CreatedAt = reader.IsDBNull(reader.GetOrdinal("created_at"))
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"))
                };

                if (!reader.IsDBNull(reader.GetOrdinal("member_user_id")))
                {
                    members.Add(new ChatGroupMemberDto
                    {
                        UserId = reader.GetInt32(reader.GetOrdinal("member_user_id")),
                        IsAdmin = reader.GetBoolean(reader.GetOrdinal("member_is_admin")),
                        JoinedAt = reader.IsDBNull(reader.GetOrdinal("member_joined_at"))
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("member_joined_at")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("member_is_active"))
                    });
                }
            }

            if (details is null)
                throw new ChatOperationException("Group not found or you are not an active member.", 404);

            details.Members = members;
            return details;
        }
        catch (ChatOperationException)
        {
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetGroupDetailsAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<IReadOnlyList<GroupMemberChangeResult>> AddGroupMembersAsync(
        long groupId,
        IReadOnlyList<long> memberUserIds,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _databaseHelper.ExecuteRawQueryAsync(
                @"SELECT group_id, user_id, is_admin, joined_at, is_active, reactivated
                  FROM public.fn_chat_add_group_members(
                      @p_group_id, @p_member_user_ids, @p_requesting_user_id,
                      @p_org_id, @p_app_id, @p_fiscal_year_id);",
                command =>
                {
                    command.Parameters.AddWithValue("p_group_id", groupId);
                    command.Parameters.Add("p_member_user_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value =
                        memberUserIds.ToArray();
                    command.Parameters.AddWithValue("p_requesting_user_id", requestingUserId);
                    command.Parameters.AddWithValue("p_org_id", orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);
                },
                reader => new GroupMemberChangeResult
                {
                    GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
                    UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                    IsAdmin = reader.GetBoolean(reader.GetOrdinal("is_admin")),
                    JoinedAt = reader.IsDBNull(reader.GetOrdinal("joined_at"))
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("joined_at")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                    Reactivated = reader.GetBoolean(reader.GetOrdinal("reactivated"))
                },
                cancellationToken);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in AddGroupMembersAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<GroupMemberChangeResult> RemoveGroupMemberAsync(
        long groupId,
        long targetUserId,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT group_id, user_id, is_active, left_at
                  FROM public.fn_chat_remove_group_member(
                      @p_group_id, @p_target_user_id, @p_requesting_user_id,
                      @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.AddWithValue("p_target_user_id", targetUserId);
            command.Parameters.AddWithValue("p_requesting_user_id", requestingUserId);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Member was not removed.", 500);

            return new GroupMemberChangeResult
            {
                GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
                UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                LeftAt = reader.IsDBNull(reader.GetOrdinal("left_at"))
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("left_at"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in RemoveGroupMemberAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<GroupMemberChangeResult> SetGroupMemberAdminAsync(
        long groupId,
        long targetUserId,
        long requestingUserId,
        bool isAdmin,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT group_id, user_id, is_admin, joined_at, is_active
                  FROM public.fn_chat_set_group_member_admin(
                      @p_group_id, @p_target_user_id, @p_requesting_user_id, @p_is_admin,
                      @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.AddWithValue("p_target_user_id", targetUserId);
            command.Parameters.AddWithValue("p_requesting_user_id", requestingUserId);
            command.Parameters.AddWithValue("p_is_admin", isAdmin);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Admin role was not updated.", 500);

            return new GroupMemberChangeResult
            {
                GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
                UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                IsAdmin = reader.GetBoolean(reader.GetOrdinal("is_admin")),
                JoinedAt = reader.IsDBNull(reader.GetOrdinal("joined_at"))
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("joined_at")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in SetGroupMemberAdminAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ChatGroupDto> UpdateGroupAsync(
        long groupId,
        string groupName,
        string? description,
        string? profilePic,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            // Admin check (existing function — unchanged for one-to-one).
            await using (var assertCmd = new NpgsqlCommand(
                @"SELECT public.fn_chat_assert_group_admin(
                      @p_group_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            })
            {
                assertCmd.Parameters.AddWithValue("p_group_id", groupId);
                assertCmd.Parameters.AddWithValue("p_user_id", requestingUserId);
                assertCmd.Parameters.AddWithValue("p_org_id", orgId);
                assertCmd.Parameters.AddWithValue("p_app_id", appId);
                assertCmd.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);
                await assertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Direct UPDATE so profile_pic is written even if fn_chat_update_group
            // on the live DB has not been replaced yet.
            await using var command = new NpgsqlCommand(
                @"UPDATE public.tab_groups g
                  SET group_name = @p_group_name,
                      description = @p_description,
                      profile_pic = CASE
                          WHEN @p_set_profile_pic THEN @p_profile_pic
                          ELSE g.profile_pic
                      END,
                      updated_at = CURRENT_TIMESTAMP
                  WHERE g.group_id = @p_group_id
                    AND g.org_id = @p_org_id
                    AND g.app_id = @p_app_id
                    AND g.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                    AND COALESCE(g.is_deleted, false) = false
                  RETURNING
                      g.group_id,
                      g.chat_id,
                      g.group_name,
                      g.group_code,
                      g.description,
                      g.created_by,
                      g.created_on AS created_at,
                      (COALESCE(g.is_deleted, false) = false) AS is_active,
                      g.profile_pic;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.Add("p_group_name", NpgsqlDbType.Varchar).Value = groupName;
            command.Parameters.Add("p_description", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim();
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            // null = leave unchanged; otherwise set (empty string clears to NULL).
            var setProfilePic = profilePic is not null;
            command.Parameters.AddWithValue("p_set_profile_pic", setProfilePic);
            command.Parameters.Add("p_profile_pic", NpgsqlDbType.Varchar).Value =
                setProfilePic
                    ? (string.IsNullOrWhiteSpace(profilePic)
                        ? DBNull.Value
                        : profilePic.Trim())
                    : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Group was not updated.", 500);

            return MapChatGroup(reader, includeIsAdmin: false);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in UpdateGroupAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<GroupMemberChangeResult> LeaveGroupAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT group_id, user_id, is_active, left_at
                  FROM public.fn_chat_leave_group(
                      @p_group_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.AddWithValue("p_user_id", userId);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Leave group failed.", 500);

            return new GroupMemberChangeResult
            {
                GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
                UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                LeftAt = reader.IsDBNull(reader.GetOrdinal("left_at"))
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("left_at"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in LeaveGroupAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<long?> GetGroupIdByChatIdAsync(
        int chatId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            try
            {
                await using var command = new NpgsqlCommand(
                    @"SELECT group_id
                      FROM public.fn_chat_get_group_by_chat_id(
                          @p_chat_id, @p_org_id, @p_app_id, @p_fiscal_year_id)
                      WHERE is_active = true
                      LIMIT 1;",
                    connection)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };

                command.Parameters.AddWithValue("p_chat_id", chatId);
                command.Parameters.AddWithValue("p_org_id", orgId);
                command.Parameters.AddWithValue("p_app_id", appId);
                command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result is not null and not DBNull)
                    return Convert.ToInt64(result);
            }
            catch (PostgresException)
            {
                // Fall through to direct tab_groups lookup.
            }

            await using var fallback = new NpgsqlCommand(
                @"SELECT g.group_id
                  FROM public.tab_groups g
                  WHERE g.chat_id = @p_chat_id
                    AND COALESCE(g.is_deleted, false) = false
                  ORDER BY CASE
                               WHEN g.org_id = @p_org_id AND g.app_id = @p_app_id
                                    AND g.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id THEN 0
                               WHEN g.org_id = @p_org_id AND g.app_id = @p_app_id THEN 1
                               ELSE 2
                           END
                  LIMIT 1;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            fallback.Parameters.AddWithValue("p_chat_id", chatId);
            fallback.Parameters.AddWithValue("p_org_id", orgId);
            fallback.Parameters.AddWithValue("p_app_id", appId);
            fallback.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            var fallbackResult = await fallback.ExecuteScalarAsync(cancellationToken);
            if (fallbackResult is null or DBNull) return null;
            return Convert.ToInt64(fallbackResult);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetGroupIdByChatIdAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task AssertGroupMemberAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT public.fn_chat_assert_group_member(
                      @p_group_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_group_id", groupId);
            command.Parameters.AddWithValue("p_user_id", userId);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in AssertGroupMemberAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<MessageInfoResult> GetMessageInfoAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await EnsureMessageReceiptsTableAsync(connection, cancellationToken);

            await using var msgCmd = new NpgsqlCommand(
                @"SELECT message_id, chat_id, group_id, sender_user_id, sent_at,
                         org_id, app_id, fiscal_year_id, delete_flag, deleted_at, is_draft
                  FROM public.tab_messages
                  WHERE message_id = @p_message_id
                  ORDER BY CASE
                               WHEN org_id = @p_org_id AND app_id = @p_app_id THEN 0
                               ELSE 1
                           END
                  LIMIT 1;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            msgCmd.Parameters.AddWithValue("p_message_id", messageId);
            msgCmd.Parameters.AddWithValue("p_org_id", orgId);
            msgCmd.Parameters.AddWithValue("p_app_id", appId);

            long outMessageId;
            int chatId;
            long? groupId;
            long senderUserId;
            DateTimeOffset? sentAt;

            await using (var msgReader = await msgCmd.ExecuteReaderAsync(cancellationToken))
            {
                if (!await msgReader.ReadAsync(cancellationToken))
                    throw new ChatOperationException($"Message {messageId} not found.", 404);

                outMessageId = msgReader.GetInt64(msgReader.GetOrdinal("message_id"));
                chatId = msgReader.GetInt32(msgReader.GetOrdinal("chat_id"));
                groupId = msgReader.IsDBNull(msgReader.GetOrdinal("group_id"))
                    ? null
                    : msgReader.GetInt64(msgReader.GetOrdinal("group_id"));
                senderUserId = msgReader.GetInt64(msgReader.GetOrdinal("sender_user_id"));
                sentAt = msgReader.IsDBNull(msgReader.GetOrdinal("sent_at"))
                    ? null
                    : msgReader.GetFieldValue<DateTimeOffset>(msgReader.GetOrdinal("sent_at"));

                var deleteFlag = msgReader.IsDBNull(msgReader.GetOrdinal("delete_flag"))
                    ? 0
                    : Convert.ToInt32(msgReader.GetValue(msgReader.GetOrdinal("delete_flag")));
                var deletedAt = msgReader.IsDBNull(msgReader.GetOrdinal("deleted_at"))
                    ? (DateTimeOffset?)null
                    : msgReader.GetFieldValue<DateTimeOffset>(msgReader.GetOrdinal("deleted_at"));
                var isDraft = !msgReader.IsDBNull(msgReader.GetOrdinal("is_draft"))
                    && msgReader.GetBoolean(msgReader.GetOrdinal("is_draft"));

                if (deleteFlag != 0 || deletedAt is not null)
                    throw new ChatOperationException($"Message {messageId} is deleted.", 400);
                if (isDraft)
                    throw new ChatOperationException($"Message {messageId} not found.", 404);
            }

            if (groupId is null or <= 0)
            {
                groupId = await TryResolveGroupIdByChatAsync(
                    connection, chatId, orgId, appId, fiscalYearId!.Value, cancellationToken);
            }

            if (groupId is null or <= 0)
                throw new ChatOperationException($"Message {messageId} is not a group message.", 400);

            await AssertActiveGroupMemberAsync(
                connection, groupId.Value, authenticatedUserId, orgId, appId, cancellationToken);

            var members = new List<MessageInfoMemberDto>();
            await using var membersCmd = new NpgsqlCommand(
                @"SELECT gm.user_id AS member_user_id,
                         r.delivered_at,
                         r.read_at
                  FROM public.tab_group_members gm
                  LEFT JOIN LATERAL (
                      SELECT rr.delivered_at, rr.read_at
                      FROM public.tab_message_receipts rr
                      WHERE rr.message_id = @p_message_id
                        AND rr.user_id = gm.user_id
                      ORDER BY rr.read_at DESC NULLS LAST, rr.delivered_at DESC NULLS LAST
                      LIMIT 1
                  ) r ON TRUE
                  WHERE gm.group_id = @p_group_id
                    AND gm.left_at IS NULL
                    AND gm.org_id = @p_org_id
                    AND gm.app_id = @p_app_id
                    AND gm.user_id IS DISTINCT FROM @p_sender_user_id
                  ORDER BY
                      CASE WHEN r.read_at IS NOT NULL THEN 0
                           WHEN r.delivered_at IS NOT NULL THEN 1
                           ELSE 2 END,
                      COALESCE(r.read_at, r.delivered_at) DESC NULLS LAST,
                      gm.user_id;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            membersCmd.Parameters.AddWithValue("p_message_id", messageId);
            membersCmd.Parameters.AddWithValue("p_group_id", groupId.Value);
            membersCmd.Parameters.AddWithValue("p_org_id", orgId);
            membersCmd.Parameters.AddWithValue("p_app_id", appId);
            membersCmd.Parameters.AddWithValue("p_sender_user_id", Convert.ToInt32(senderUserId));

            await using (var memberReader = await membersCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await memberReader.ReadAsync(cancellationToken))
                {
                    members.Add(new MessageInfoMemberDto
                    {
                        UserId = memberReader.GetInt64(memberReader.GetOrdinal("member_user_id")),
                        DeliveredAt = memberReader.IsDBNull(memberReader.GetOrdinal("delivered_at"))
                            ? null
                            : memberReader.GetFieldValue<DateTimeOffset>(
                                memberReader.GetOrdinal("delivered_at")),
                        ReadAt = memberReader.IsDBNull(memberReader.GetOrdinal("read_at"))
                            ? null
                            : memberReader.GetFieldValue<DateTimeOffset>(
                                memberReader.GetOrdinal("read_at"))
                    });
                }
            }

            return new MessageInfoResult
            {
                MessageId = outMessageId,
                ChatId = chatId,
                GroupId = groupId,
                SenderUserId = senderUserId,
                SentAt = sentAt,
                Members = members
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetMessageInfoAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task MarkMessageDeliveredAsync(
        long messageId,
        long receiverUserId,
        int chatId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0 || receiverUserId <= 0)
            return;

        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            try
            {
                await EnsureMessageReceiptsTableAsync(connection, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureMessageReceiptsTableAsync warning for delivered {MessageId}", messageId);
            }

            int resolvedOrg = orgId;
            int resolvedApp = appId;
            int resolvedFy = fiscalYearId ?? 0;

            await using (var lookup = new NpgsqlCommand(
                @"SELECT org_id, app_id, fiscal_year_id, sender_user_id
                  FROM public.tab_messages
                  WHERE message_id = @p_message_id
                  LIMIT 1;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            })
            {
                lookup.Parameters.AddWithValue("p_message_id", messageId);
                await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return;

                resolvedOrg = reader.GetInt32(reader.GetOrdinal("org_id"));
                resolvedApp = reader.GetInt32(reader.GetOrdinal("app_id"));
                if (!reader.IsDBNull(reader.GetOrdinal("fiscal_year_id")))
                    resolvedFy = reader.GetInt32(reader.GetOrdinal("fiscal_year_id"));

                if (reader.GetInt64(reader.GetOrdinal("sender_user_id")) == receiverUserId)
                    return;
            }

            if (resolvedOrg <= 0 || resolvedApp <= 0 || resolvedFy <= 0)
                return;

            var userIdInt = checked((int)receiverUserId);

            await using var upsertCmd = new NpgsqlCommand(
                @"INSERT INTO public.tab_message_receipts (
                      message_id, user_id, delivered_at, read_at,
                      org_id, app_id, fiscal_year_id, created_at, updated_at
                  )
                  VALUES (
                      @p_message_id, @p_user_id, CURRENT_TIMESTAMP, NULL,
                      @p_org_id, @p_app_id, @p_fiscal_year_id,
                      CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                  )
                  ON CONFLICT (message_id, user_id, org_id, app_id, fiscal_year_id)
                  DO UPDATE SET
                      delivered_at = COALESCE(public.tab_message_receipts.delivered_at, EXCLUDED.delivered_at),
                      updated_at = CURRENT_TIMESTAMP;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };
            upsertCmd.Parameters.AddWithValue("p_message_id", messageId);
            upsertCmd.Parameters.AddWithValue("p_user_id", userIdInt);
            upsertCmd.Parameters.AddWithValue("p_org_id", resolvedOrg);
            upsertCmd.Parameters.AddWithValue("p_app_id", resolvedApp);
            upsertCmd.Parameters.AddWithValue("p_fiscal_year_id", resolvedFy);
            await upsertCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MarkMessageDeliveredAsync failed for message {MessageId} user {UserId}",
                messageId,
                receiverUserId);
        }
    }

    private async Task EnsureMessageReceiptsTableAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var createTable = new NpgsqlCommand(
            @"CREATE TABLE IF NOT EXISTS public.tab_message_receipts (
                  receipt_id      bigserial PRIMARY KEY,
                  message_id      bigint NOT NULL,
                  user_id         integer NOT NULL,
                  delivered_at    timestamptz NULL,
                  read_at         timestamptz NULL,
                  org_id          integer NOT NULL,
                  app_id          integer NOT NULL,
                  fiscal_year_id  integer NOT NULL,
                  created_at      timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  updated_at      timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
              );",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        })
        {
            await createTable.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createUnique = new NpgsqlCommand(
            @"DO $$
              BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'uq_tab_message_receipts_msg_user'
                ) THEN
                    ALTER TABLE public.tab_message_receipts
                        ADD CONSTRAINT uq_tab_message_receipts_msg_user
                        UNIQUE (message_id, user_id, org_id, app_id, fiscal_year_id);
                END IF;
              END $$;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        })
        {
            await createUnique.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var createIndexes = new NpgsqlCommand(
            @"CREATE INDEX IF NOT EXISTS ix_tab_message_receipts_message
                  ON public.tab_message_receipts (message_id);
              CREATE INDEX IF NOT EXISTS ix_tab_message_receipts_user
                  ON public.tab_message_receipts (user_id);",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        await createIndexes.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long?> TryResolveGroupIdByChatAsync(
        NpgsqlConnection connection,
        int chatId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT g.group_id
              FROM public.tab_groups g
              WHERE g.chat_id = @p_chat_id
                AND COALESCE(g.is_deleted, false) = false
              ORDER BY CASE
                           WHEN g.org_id = @p_org_id AND g.app_id = @p_app_id
                                AND g.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id THEN 0
                           WHEN g.org_id = @p_org_id AND g.app_id = @p_app_id THEN 1
                           ELSE 2
                       END
              LIMIT 1;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("p_chat_id", chatId);
        cmd.Parameters.AddWithValue("p_org_id", orgId);
        cmd.Parameters.AddWithValue("p_app_id", appId);
        cmd.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is not null and not DBNull)
            return Convert.ToInt64(result);

        return await TryResolveGroupIdFromMessagesAsync(connection, chatId, cancellationToken);
    }

    private async Task<long?> TryResolveGroupIdFromMessagesAsync(
        NpgsqlConnection connection,
        int chatId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT m.group_id
              FROM public.tab_messages m
              WHERE m.chat_id = @p_chat_id
                AND m.group_id IS NOT NULL
              ORDER BY m.message_id DESC
              LIMIT 1;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("p_chat_id", chatId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull) return null;
        return Convert.ToInt64(result);
    }

    private async Task<bool> IsActiveGroupMemberAsync(
        NpgsqlConnection connection,
        long groupId,
        long userId,
        int orgId,
        int appId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT 1
              FROM public.tab_group_members gm
              WHERE gm.group_id = @p_group_id
                AND gm.user_id = @p_user_id
                AND gm.left_at IS NULL
              ORDER BY CASE
                           WHEN gm.org_id = @p_org_id AND gm.app_id = @p_app_id THEN 0
                           ELSE 1
                       END
              LIMIT 1;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("p_group_id", groupId);
        cmd.Parameters.AddWithValue("p_user_id", checked((int)userId));
        cmd.Parameters.AddWithValue("p_org_id", orgId);
        cmd.Parameters.AddWithValue("p_app_id", appId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private async Task AssertActiveGroupMemberAsync(
        NpgsqlConnection connection,
        long groupId,
        long userId,
        int orgId,
        int appId,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveGroupMemberAsync(
                connection, groupId, userId, orgId, appId, cancellationToken))
        {
            throw new ChatOperationException(
                $"User {userId} is not an active member of group {groupId}.",
                403);
        }
    }

    private static string? ReadOptionalString(NpgsqlDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static ChatGroupDto MapChatGroup(NpgsqlDataReader reader, bool includeIsAdmin)
    {
        var dto = new ChatGroupDto
        {
            GroupId = reader.GetInt64(reader.GetOrdinal("group_id")),
            ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("chat_id")),
            GroupName = reader.GetString(reader.GetOrdinal("group_name")),
            GroupCode = reader.GetString(reader.GetOrdinal("group_code")),
            Description = reader.IsDBNull(reader.GetOrdinal("description"))
                ? null
                : reader.GetString(reader.GetOrdinal("description")),
            ProfilePic = ReadOptionalString(reader, "profile_pic"),
            CreatedBy = reader.GetInt32(reader.GetOrdinal("created_by")),
            CreatedAt = reader.IsDBNull(reader.GetOrdinal("created_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
        };

        if (includeIsAdmin)
        {
            try
            {
                dto.IsAdmin = reader.GetBoolean(reader.GetOrdinal("is_admin"));
            }
            catch (IndexOutOfRangeException)
            {
                dto.IsAdmin = false;
            }
        }

        try
        {
            var unreadOrdinal = reader.GetOrdinal("unread_count");
            if (!reader.IsDBNull(unreadOrdinal))
                dto.UnreadCount = reader.GetInt32(unreadOrdinal);
        }
        catch (IndexOutOfRangeException)
        {
            dto.UnreadCount = 0;
        }

        return dto;
    }

    private static int MapPostgresStatus(string? sqlState) =>
        sqlState switch
        {
            "42501" => 403,
            "P0002" => 404,
            _ => 400
        };
}
