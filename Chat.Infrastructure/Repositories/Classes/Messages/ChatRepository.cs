using Chat.Domain.DTOs.Messages;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Messages;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Messages;

public sealed class ChatRepository : IChatRepository
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<ChatRepository> _logger;

    public ChatRepository(DatabaseHelper databaseHelper, ILogger<ChatRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<int> GetOrCreatePrivateChatAsync(
        long user1Id,
        long user2Id,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT chat_id
              FROM public.fn_chat_get_or_create_private_chat(
                  @p_user1_id, @p_user2_id, @p_org_id, @p_app_id, @p_fiscal_year_id);";

        var commandTimeout = _databaseHelper.CommandTimeoutSeconds;

        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = commandTimeout
            };

            command.Parameters.AddWithValue("p_user1_id", user1Id);
            command.Parameters.AddWithValue("p_user2_id", user2Id);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                (object?)fiscalYearId ?? DBNull.Value;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null or DBNull)
                throw new ChatOperationException("Unable to get or create private chat.", 500);

            return Convert.ToInt32(result);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(
                ex,
                "Canceled GetOrCreatePrivateChatAsync. Sql={Sql} User1Id={User1Id} User2Id={User2Id} OrgId={OrgId} AppId={AppId} FiscalYearId={FiscalYearId} CommandTimeoutSeconds={CommandTimeout} CancellationRequested={CancellationRequested} Inner={Inner}",
                sql,
                user1Id,
                user2Id,
                orgId,
                appId,
                fiscalYearId,
                commandTimeout,
                cancellationToken.IsCancellationRequested,
                ex.InnerException?.Message);
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(
                ex,
                "PostgreSQL error in GetOrCreatePrivateChatAsync. Sql={Sql} User1Id={User1Id} User2Id={User2Id} OrgId={OrgId} AppId={AppId} FiscalYearId={FiscalYearId} CommandTimeoutSeconds={CommandTimeout} SqlState={SqlState}",
                sql,
                user1Id,
                user2Id,
                orgId,
                appId,
                fiscalYearId,
                commandTimeout,
                ex.SqlState);
            throw new ChatOperationException(ex.MessageText, 400);
        }
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetChatMessagesAsync(
        int chatId,
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT *
              FROM public.fn_chat_get_chat_messages(
                  @p_chat_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);";

        try
        {
            return await _databaseHelper.ExecuteRawQueryAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("p_chat_id", chatId);
                    command.Parameters.AddWithValue("p_user_id", userId);
                    command.Parameters.AddWithValue("p_org_id", orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                        (object?)fiscalYearId ?? DBNull.Value;
                },
                reader => MapChatMessage(reader, userId),
                cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(
                ex,
                "Canceled GetChatMessagesAsync. Sql={Sql} ChatId={ChatId} UserId={UserId} OrgId={OrgId} AppId={AppId} FiscalYearId={FiscalYearId} CommandTimeoutSeconds={CommandTimeout} CancellationRequested={CancellationRequested} Inner={Inner}",
                sql,
                chatId,
                userId,
                orgId,
                appId,
                fiscalYearId,
                _databaseHelper.CommandTimeoutSeconds,
                cancellationToken.IsCancellationRequested,
                ex.InnerException?.Message);
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(
                ex,
                "PostgreSQL error in GetChatMessagesAsync. Sql={Sql} ChatId={ChatId} UserId={UserId} OrgId={OrgId} AppId={AppId} FiscalYearId={FiscalYearId} CommandTimeoutSeconds={CommandTimeout} SqlState={SqlState}",
                sql,
                chatId,
                userId,
                orgId,
                appId,
                fiscalYearId,
                _databaseHelper.CommandTimeoutSeconds,
                ex.SqlState);
            throw new ChatOperationException(ex.MessageText, 400);
        }
    }

    public async Task<SentMessageDto> SendMessageAsync(
        int chatId,
        long senderUserId,
        long? receiverUserId,
        string messageBody,
        short messageTypeId,
        int orgId,
        int appId,
        int? fiscalYearId,
        string? attachmentPath1 = null,
        string? attachmentPath2 = null,
        string? attachmentPath3 = null,
        string? attachmentPath4 = null,
        string? attachmentPath5 = null,
        long? parentMessageId = null,
        long? groupId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            try
            {
                await EnsureTabMessagesGroupAndForwardColumnsAsync(connection, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureTabMessagesGroupAndForwardColumnsAsync warning before send");
            }

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, chat_id, sender_user_id, receiver_user_id,
                         message_body, message_type_id, sent_at, created_at,
                         attachment_path_1, attachment_path_2, attachment_path_3,
                         attachment_path_4, attachment_path_5, group_id
                  FROM public.fn_chat_send_message(
                      @p_chat_id, @p_sender_user_id, @p_receiver_user_id, @p_message_body,
                      @p_message_type_id, @p_org_id, @p_app_id, @p_fiscal_year_id,
                      @p_attachment_path_1, @p_attachment_path_2, @p_attachment_path_3,
                      @p_attachment_path_4, @p_attachment_path_5, @p_group_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_chat_id", NpgsqlDbType.Integer).Value = chatId;
            command.Parameters.Add("p_sender_user_id", NpgsqlDbType.Bigint).Value = senderUserId;
            command.Parameters.Add("p_receiver_user_id", NpgsqlDbType.Bigint).Value =
                receiverUserId is > 0 ? receiverUserId.Value : DBNull.Value;
            command.Parameters.Add("p_message_body", NpgsqlDbType.Text).Value = messageBody ?? string.Empty;
            command.Parameters.Add("p_message_type_id", NpgsqlDbType.Smallint).Value = messageTypeId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");
            AddNullableText(command, "p_attachment_path_1", attachmentPath1);
            AddNullableText(command, "p_attachment_path_2", attachmentPath2);
            AddNullableText(command, "p_attachment_path_3", attachmentPath3);
            AddNullableText(command, "p_attachment_path_4", attachmentPath4);
            AddNullableText(command, "p_attachment_path_5", attachmentPath5);
            command.Parameters.Add("p_group_id", NpgsqlDbType.Bigint).Value =
                groupId is > 0 ? groupId.Value : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Message was not created.", 500);

            var saved = MapSentMessage(reader);
            await reader.DisposeAsync();

            if (parentMessageId is > 0)
            {
                await using var linkCmd = new NpgsqlCommand(
                    @"UPDATE public.tab_messages
                      SET parent_message_id = @p_parent_message_id,
                          updated_at = CURRENT_TIMESTAMP
                      WHERE message_id = @p_message_id
                        AND chat_id = @p_chat_id
                        AND org_id = @p_org_id
                        AND app_id = @p_app_id
                        AND fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                        AND deleted_at IS NULL
                        AND EXISTS (
                              SELECT 1
                              FROM public.tab_messages p
                              WHERE p.message_id = @p_parent_message_id
                                AND p.chat_id = @p_chat_id
                                AND p.org_id = @p_org_id
                                AND p.app_id = @p_app_id
                                AND p.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                                AND p.deleted_at IS NULL
                          );",
                    connection)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };
                linkCmd.Parameters.Add("p_parent_message_id", NpgsqlDbType.Bigint).Value = parentMessageId.Value;
                linkCmd.Parameters.Add("p_message_id", NpgsqlDbType.Bigint).Value = saved.MessageId;
                linkCmd.Parameters.Add("p_chat_id", NpgsqlDbType.Integer).Value = chatId;
                linkCmd.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
                linkCmd.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
                linkCmd.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value = fiscalYearId!.Value;
                var linked = await linkCmd.ExecuteNonQueryAsync(cancellationToken);
                if (linked > 0)
                    saved.ParentMessageId = parentMessageId;
            }

            return saved;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in SendMessageAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<SentMessageDto> ForwardMessageAsync(
        long originalMessageId,
        int destinationChatId,
        long forwardedByUserId,
        long receiverUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            try
            {
                await EnsureTabMessagesGroupAndForwardColumnsAsync(connection, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureTabMessagesGroupAndForwardColumnsAsync warning before forward");
            }

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, chat_id, sender_user_id, receiver_user_id,
                         message_body, message_type_id, sent_at, created_at,
                         attachment_path_1, attachment_path_2, attachment_path_3,
                         attachment_path_4, attachment_path_5,
                         forwarded_from_message_id, forwarded_by, group_id
                  FROM public.fn_chat_forward_message(
                      @p_original_message_id, @p_destination_chat_id, @p_forwarded_by,
                      @p_receiver_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_original_message_id", NpgsqlDbType.Bigint).Value = originalMessageId;
            command.Parameters.Add("p_destination_chat_id", NpgsqlDbType.Integer).Value = destinationChatId;
            command.Parameters.Add("p_forwarded_by", NpgsqlDbType.Bigint).Value = forwardedByUserId;
            command.Parameters.Add("p_receiver_user_id", NpgsqlDbType.Bigint).Value = receiverUserId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Forwarded message was not created.", 500);

            return MapSentMessage(reader, includeForwardMeta: true);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in ForwardMessageAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<DeleteMessageResult> DeleteMessageAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, chat_id, sender_user_id, receiver_user_id,
                         deleted_by, deleted_at, delete_flag
                  FROM public.fn_chat_delete_message(
                      @p_message_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_message_id", NpgsqlDbType.Bigint).Value = messageId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = authenticatedUserId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Message was not deleted.", 500);

            var receiverOrdinal = reader.GetOrdinal("receiver_user_id");
            return new DeleteMessageResult
            {
                MessageId = reader.GetInt64(reader.GetOrdinal("message_id")),
                ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
                SenderUserId = reader.GetInt64(reader.GetOrdinal("sender_user_id")),
                ReceiverUserId = reader.IsDBNull(receiverOrdinal) ? null : reader.GetInt64(receiverOrdinal),
                DeletedBy = reader.GetInt64(reader.GetOrdinal("deleted_by")),
                DeletedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("deleted_at")),
                DeleteFlag = reader.GetInt16(reader.GetOrdinal("delete_flag"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in DeleteMessageAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ToggleMessageStarResult> ToggleMessageStarAsync(
        long messageId,
        long authenticatedUserId,
        bool isStarred,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, chat_id, sender_user_id, receiver_user_id,
                         is_starred_by_me, starred_at
                  FROM public.fn_chat_toggle_message_star(
                      @p_message_id, @p_user_id, @p_is_starred, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_message_id", NpgsqlDbType.Bigint).Value = messageId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = authenticatedUserId;
            command.Parameters.Add("p_is_starred", NpgsqlDbType.Boolean).Value = isStarred;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Unable to update message star.", 500);

            var receiverOrdinal = reader.GetOrdinal("receiver_user_id");
            var starredAtOrdinal = reader.GetOrdinal("starred_at");
            return new ToggleMessageStarResult
            {
                MessageId = reader.GetInt64(reader.GetOrdinal("message_id")),
                ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
                SenderUserId = reader.GetInt64(reader.GetOrdinal("sender_user_id")),
                ReceiverUserId = reader.IsDBNull(receiverOrdinal) ? null : reader.GetInt64(receiverOrdinal),
                IsStarredByMe = reader.GetBoolean(reader.GetOrdinal("is_starred_by_me")),
                StarredAt = reader.IsDBNull(starredAtOrdinal)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(starredAtOrdinal)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in ToggleMessageStarAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ToggleMessageReactionResult> ToggleMessageReactionAsync(
        long messageId,
        long authenticatedUserId,
        string? reactionCode,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT *
                  FROM public.fn_chat_toggle_message_reaction(
                      @p_message_id, @p_user_id, @p_reaction_code, @p_app_id, @p_org_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_message_id", NpgsqlDbType.Bigint).Value = messageId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = authenticatedUserId;
            command.Parameters.Add("p_reaction_code", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(reactionCode) ? DBNull.Value : reactionCode.Trim();
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Unable to update message reaction.", 500);

            static bool HasColumn(NpgsqlDataReader r, string name)
            {
                for (var i = 0; i < r.FieldCount; i++)
                {
                    if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            static string? ReadString(NpgsqlDataReader r, string name)
            {
                if (!HasColumn(r, name)) return null;
                var ordinal = r.GetOrdinal(name);
                return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
            }

            var receiverOrdinal = reader.GetOrdinal("receiver_user_id");
            var reactionIdOrdinal = reader.GetOrdinal("reaction_id");
            var reactedOnOrdinal = reader.GetOrdinal("reacted_on");
            // Function returns reaction_emoji; older drafts used reaction — accept either.
            var reactionEmoji =
                ReadString(reader, "reaction_emoji") ?? ReadString(reader, "reaction");

            return new ToggleMessageReactionResult
            {
                MessageId = reader.GetInt64(reader.GetOrdinal("message_id")),
                ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
                SenderUserId = reader.GetInt64(reader.GetOrdinal("sender_user_id")),
                ReceiverUserId = reader.IsDBNull(receiverOrdinal) ? null : reader.GetInt64(receiverOrdinal),
                ReactionId = reader.IsDBNull(reactionIdOrdinal) ? null : reader.GetInt64(reactionIdOrdinal),
                ReactionCode = ReadString(reader, "reaction_code"),
                Reaction = reactionEmoji,
                ReactedOn = reader.IsDBNull(reactedOnOrdinal)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(reactedOnOrdinal)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in ToggleMessageReactionAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ClearChatResult> ClearChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT chat_id, affected_count, peer_user_id, cleared_by, cleared_at
                  FROM public.fn_chat_clear_chat(
                      @p_chat_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_chat_id", NpgsqlDbType.Integer).Value = chatId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = authenticatedUserId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Unable to clear chat.", 500);

            var peerOrdinal = reader.GetOrdinal("peer_user_id");
            return new ClearChatResult
            {
                ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
                AffectedCount = reader.GetInt32(reader.GetOrdinal("affected_count")),
                PeerUserId = reader.IsDBNull(peerOrdinal) ? null : reader.GetInt64(peerOrdinal),
                ClearedBy = reader.GetInt64(reader.GetOrdinal("cleared_by")),
                ClearedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("cleared_at"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in ClearChatAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<DeleteChatResult> DeleteChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT chat_id, affected_count, peer_user_id, deleted_by, deleted_at
                  FROM public.fn_chat_delete_chat(
                      @p_chat_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_chat_id", NpgsqlDbType.Integer).Value = chatId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = authenticatedUserId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Unable to delete chat.", 500);

            var peerOrdinal = reader.GetOrdinal("peer_user_id");
            return new DeleteChatResult
            {
                ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
                AffectedCount = reader.GetInt32(reader.GetOrdinal("affected_count")),
                PeerUserId = reader.IsDBNull(peerOrdinal) ? null : reader.GetInt64(peerOrdinal),
                DeletedBy = reader.GetInt64(reader.GetOrdinal("deleted_by")),
                DeletedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("deleted_at"))
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in DeleteChatAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetStarredMessagesAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT *
              FROM public.fn_chat_get_starred_messages(
                  @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);";

        try
        {
            return await _databaseHelper.ExecuteRawQueryAsync(
                sql,
                command =>
                {
                    command.Parameters.AddWithValue("p_user_id", authenticatedUserId);
                    command.Parameters.AddWithValue("p_org_id", orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                        (object?)fiscalYearId ?? DBNull.Value;
                },
                reader => MapChatMessage(reader, authenticatedUserId, fromStarredList: true),
                cancellationToken);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetStarredMessagesAsync");
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
    }

    public async Task<ChatNotificationDto> CreateNotificationAsync(
        long receiverUserId,
        long senderUserId,
        string title,
        string message,
        long referenceId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default,
        string? referenceType = null,
        string? notificationType = null,
        string? referenceEntity = null)
    {
        try
        {
            var resolvedReferenceType = string.IsNullOrWhiteSpace(referenceType)
                ? "CHAT"
                : referenceType.Trim();
            var resolvedNotificationType = string.IsNullOrWhiteSpace(notificationType)
                ? "MESSAGE"
                : notificationType.Trim();
            var resolvedReferenceEntity = string.IsNullOrWhiteSpace(referenceEntity)
                ? "tab_messages"
                : referenceEntity.Trim();

            var rows = await _databaseHelper.ExecuteRawQueryAsync(
                @"SELECT *
                  FROM public.fn_chat_create_notification(
                      @p_user_id, @p_sender_user_id, @p_title, @p_message, @p_reference_id,
                      @p_org_id, @p_app_id, @p_fiscal_year_id, @p_notification_type,
                      @p_reference_type, @p_reference_entity);",
                command =>
                {
                    command.Parameters.AddWithValue("p_user_id", receiverUserId);
                    command.Parameters.AddWithValue("p_sender_user_id", senderUserId);
                    command.Parameters.AddWithValue("p_title", title ?? "New Message");
                    command.Parameters.AddWithValue("p_message", message ?? "");
                    command.Parameters.AddWithValue("p_reference_id", referenceId);
                    command.Parameters.AddWithValue("p_org_id", (long)orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);
                    command.Parameters.AddWithValue("p_notification_type", resolvedNotificationType);
                    command.Parameters.AddWithValue("p_reference_type", resolvedReferenceType);
                    command.Parameters.AddWithValue("p_reference_entity", resolvedReferenceEntity);
                },
                MapNotification,
                cancellationToken);

            return rows.FirstOrDefault()
                ?? throw new ChatOperationException("Notification was not created.", 500);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in CreateNotificationAsync");
            throw new ChatOperationException(ex.MessageText, 400);
        }
    }

    public async Task<ChatNotificationListResult> GetNotificationsAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _databaseHelper.ExecuteRawQueryAsync(
                @"SELECT *
                  FROM public.fn_chat_get_notifications(
                      @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                command =>
                {
                    command.Parameters.AddWithValue("p_user_id", userId);
                    command.Parameters.AddWithValue("p_org_id", (long)orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                        (object?)fiscalYearId ?? DBNull.Value;
                },
                MapNotificationWithUnread,
                cancellationToken);

            return new ChatNotificationListResult
            {
                Notifications = rows.Select(r => r.Notification).ToList(),
                UnreadCount = rows.FirstOrDefault()?.UnreadCount ?? 0
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetNotificationsAsync");
            throw new ChatOperationException(ex.MessageText, 400);
        }
    }

    public async Task<ChatNotificationListResult> MarkNotificationReadAsync(
        long notificationId,
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _databaseHelper.ExecuteRawQueryAsync(
                @"SELECT *
                  FROM public.fn_chat_mark_notification_read(
                      @p_notification_id, @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                command =>
                {
                    command.Parameters.AddWithValue("p_notification_id", notificationId);
                    command.Parameters.AddWithValue("p_user_id", userId);
                    command.Parameters.AddWithValue("p_org_id", (long)orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                        (object?)fiscalYearId ?? DBNull.Value;
                },
                MapNotificationWithUnread,
                cancellationToken);

            var first = rows.FirstOrDefault()
                ?? throw new ChatOperationException(
                    "Notification not found or not owned by user.", 403);

            return new ChatNotificationListResult
            {
                Notifications = new[] { first.Notification },
                UnreadCount = first.UnreadCount
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in MarkNotificationReadAsync");
            var status = ex.SqlState == "42501" ? 403 : 400;
            throw new ChatOperationException(ex.MessageText, status);
        }
    }

    public async Task<PeerUnreadCountListResult> GetUnreadCountsByPeerAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _databaseHelper.ExecuteRawQueryAsync(
                @"SELECT peer_user_id, unread_count
                  FROM public.fn_chat_get_unread_counts_by_peer(
                      @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                command =>
                {
                    command.Parameters.AddWithValue("p_user_id", userId);
                    command.Parameters.AddWithValue("p_org_id", orgId);
                    command.Parameters.AddWithValue("p_app_id", appId);
                    command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                        (object?)fiscalYearId ?? DBNull.Value;
                },
                reader => new PeerUnreadCountDto
                {
                    PeerUserId = reader.GetInt64(reader.GetOrdinal("peer_user_id")),
                    UnreadCount = reader.IsDBNull(reader.GetOrdinal("unread_count"))
                        ? 0
                        : reader.GetInt32(reader.GetOrdinal("unread_count"))
                },
                cancellationToken);

            var items = rows
                .Where(r => r.PeerUserId > 0 && r.UnreadCount > 0)
                .ToList();

            return new PeerUnreadCountListResult
            {
                Items = items,
                TotalUnread = items.Sum(r => r.UnreadCount)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetUnreadCountsByPeerAsync");
            throw new ChatOperationException(ex.MessageText, 400);
        }
    }

    public async Task<MarkMessagesReadResult> MarkMessagesReadAsync(
        int chatId,
        long readerUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fy = fiscalYearId
                ?? throw new ChatOperationException("fiscalYearId is required.");

            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            try
            {
                await EnsureMessageReceiptsTableAsync(connection, cancellationToken);
            }
            catch (Exception ex)
            {
                // Table may already exist or CREATE may be restricted — still try INSERT.
                _logger.LogWarning(ex, "EnsureMessageReceiptsTableAsync warning for chat {ChatId}", chatId);
            }

            var groupId = await TryResolveGroupIdByChatAsync(
                connection, chatId, orgId, appId, fy, cancellationToken);

            // Group path: always upsert receipts by chat_id (membership soft-check).
            if (groupId is > 0)
            {
                var isMember = await IsActiveGroupMemberAsync(
                    connection, groupId.Value, readerUserId, orgId, appId, cancellationToken);
                if (!isMember)
                {
                    _logger.LogWarning(
                        "MarkMessagesRead: user {UserId} not in group {GroupId} membership check; still writing receipts for chat {ChatId}",
                        readerUserId, groupId, chatId);
                }

                return await UpsertGroupMessageReceiptsAsync(
                    connection, chatId, groupId.Value, readerUserId, orgId, appId, fy, cancellationToken);
            }

            // Fallback: chat has group messages but tab_groups lookup missed.
            var inferredGroupId = await TryResolveGroupIdFromMessagesAsync(
                connection, chatId, cancellationToken);
            if (inferredGroupId is > 0)
            {
                return await UpsertGroupMessageReceiptsAsync(
                    connection,
                    chatId,
                    inferredGroupId.Value,
                    readerUserId,
                    orgId,
                    appId,
                    fy,
                    cancellationToken);
            }

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, chat_id, reader_user_id, sender_user_id, read_at
                  FROM public.fn_chat_mark_messages_read(
                      @p_chat_id, @p_reader_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("p_chat_id", chatId);
            command.Parameters.AddWithValue("p_reader_user_id", readerUserId);
            command.Parameters.AddWithValue("p_org_id", orgId);
            command.Parameters.AddWithValue("p_app_id", appId);
            command.Parameters.AddWithValue("p_fiscal_year_id", fy);

            var messageIds = new List<long>();
            var senderUserIds = new HashSet<long>();
            DateTimeOffset? readAt = null;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messageIds.Add(reader.GetInt64(reader.GetOrdinal("message_id")));
                senderUserIds.Add(reader.GetInt64(reader.GetOrdinal("sender_user_id")));
                if (!reader.IsDBNull(reader.GetOrdinal("read_at")))
                    readAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("read_at"));
            }

            return new MarkMessagesReadResult
            {
                ChatId = chatId,
                ReaderUserId = readerUserId,
                ReadAt = readAt,
                MessageIds = messageIds,
                SenderUserIds = senderUserIds.ToList()
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in MarkMessagesReadAsync chatId={ChatId} userId={UserId}", chatId, readerUserId);
            throw new ChatOperationException(ex.MessageText, MapPostgresStatus(ex.SqlState));
        }
        catch (Exception ex) when (ex is not ChatOperationException)
        {
            _logger.LogError(ex, "MarkMessagesReadAsync failed chatId={ChatId} userId={UserId}", chatId, readerUserId);
            throw new ChatOperationException(ex.Message, 500);
        }
    }

    private async Task EnsureTabMessagesGroupAndForwardColumnsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var alter = new NpgsqlCommand(
            @"DO $$
              BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'tab_messages'
                      AND column_name = 'group_id'
                ) THEN
                    ALTER TABLE public.tab_messages ADD COLUMN group_id bigint NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'tab_messages'
                      AND column_name = 'forwarded_from_message_id'
                ) THEN
                    ALTER TABLE public.tab_messages
                        ADD COLUMN forwarded_from_message_id bigint NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'tab_messages'
                      AND column_name = 'forwarded_by'
                ) THEN
                    ALTER TABLE public.tab_messages
                        ADD COLUMN forwarded_by bigint NULL;
                END IF;
              END $$;

              CREATE INDEX IF NOT EXISTS ix_tab_messages_group_id
                  ON public.tab_messages (group_id)
                  WHERE group_id IS NOT NULL;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };

        await alter.ExecuteNonQueryAsync(cancellationToken);
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

    private async Task<MarkMessagesReadResult> UpsertGroupMessageReceiptsAsync(
        NpgsqlConnection connection,
        int chatId,
        long groupId,
        long readerUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken)
    {
        // Match by chat_id only (group messages sometimes have null / mismatched group_id).
        // Avoid ON CONFLICT column lists — live unique indexes can differ by environment.
        await using var cmd = new NpgsqlCommand(
            @"WITH target AS (
                  SELECT m.message_id, m.sender_user_id
                  FROM public.tab_messages m
                  WHERE m.chat_id = @p_chat_id
                    AND m.deleted_at IS NULL
                    AND COALESCE(m.delete_flag, 0) = 0
                    AND COALESCE(m.is_draft, false) = false
                    AND m.sent_at IS NOT NULL
                    AND m.sender_user_id IS DISTINCT FROM @p_reader_user_id
                    AND NOT EXISTS (
                        SELECT 1
                        FROM public.tab_message_receipts r
                        WHERE r.message_id = m.message_id
                          AND r.user_id = @p_reader_user_id_int
                          AND r.org_id = @p_org_id
                          AND r.app_id = @p_app_id
                          AND r.fiscal_year_id = @p_fiscal_year_id
                          AND r.read_at IS NOT NULL
                    )
              ),
              updated AS (
                  UPDATE public.tab_message_receipts r
                  SET delivered_at = COALESCE(r.delivered_at, CURRENT_TIMESTAMP),
                      read_at = COALESCE(r.read_at, CURRENT_TIMESTAMP),
                      updated_at = CURRENT_TIMESTAMP
                  FROM target t
                  WHERE r.message_id = t.message_id
                    AND r.user_id = @p_reader_user_id_int
                    AND r.org_id = @p_org_id
                    AND r.app_id = @p_app_id
                    AND r.fiscal_year_id = @p_fiscal_year_id
                    AND r.read_at IS NULL
                  RETURNING r.message_id, r.read_at
              ),
              inserted AS (
                  INSERT INTO public.tab_message_receipts (
                      message_id, user_id, delivered_at, read_at,
                      org_id, app_id, fiscal_year_id, created_at, updated_at
                  )
                  SELECT
                      t.message_id,
                      @p_reader_user_id_int,
                      CURRENT_TIMESTAMP,
                      CURRENT_TIMESTAMP,
                      @p_org_id,
                      @p_app_id,
                      @p_fiscal_year_id,
                      CURRENT_TIMESTAMP,
                      CURRENT_TIMESTAMP
                  FROM target t
                  WHERE NOT EXISTS (
                      SELECT 1
                      FROM public.tab_message_receipts r
                      WHERE r.message_id = t.message_id
                        AND r.user_id = @p_reader_user_id_int
                        AND r.org_id = @p_org_id
                        AND r.app_id = @p_app_id
                        AND r.fiscal_year_id = @p_fiscal_year_id
                  )
                  RETURNING message_id, read_at
              ),
              upserted AS (
                  SELECT message_id, read_at FROM updated
                  UNION ALL
                  SELECT message_id, read_at FROM inserted
              )
              SELECT u.message_id, t.sender_user_id, u.read_at
              FROM upserted u
              INNER JOIN target t ON t.message_id = u.message_id;",
            connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };

        cmd.Parameters.AddWithValue("p_chat_id", chatId);
        cmd.Parameters.AddWithValue("p_reader_user_id", readerUserId);
        cmd.Parameters.AddWithValue("p_reader_user_id_int", checked((int)readerUserId));
        cmd.Parameters.AddWithValue("p_org_id", orgId);
        cmd.Parameters.AddWithValue("p_app_id", appId);
        cmd.Parameters.AddWithValue("p_fiscal_year_id", fiscalYearId);

        var messageIds = new List<long>();
        var senderUserIds = new HashSet<long>();
        DateTimeOffset? readAt = null;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messageIds.Add(reader.GetInt64(0));
            senderUserIds.Add(reader.GetInt64(1));
            if (!reader.IsDBNull(2))
                readAt = reader.GetFieldValue<DateTimeOffset>(2);
        }

        return new MarkMessagesReadResult
        {
            ChatId = chatId,
            ReaderUserId = readerUserId,
            ReadAt = readAt ?? (messageIds.Count > 0 ? DateTimeOffset.UtcNow : null),
            MessageIds = messageIds,
            SenderUserIds = senderUserIds.ToList()
        };
    }

    private static int MapPostgresStatus(string? sqlState) =>
        sqlState switch
        {
            "42501" => 403,
            "P0002" => 404,
            _ => 400
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static SentMessageDto MapSentMessage(NpgsqlDataReader reader, bool includeForwardMeta = false)
    {
        static string? ReadString(NpgsqlDataReader r, string name)
        {
            try
            {
                var ordinal = r.GetOrdinal(name);
                return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        static long? ReadInt64(NpgsqlDataReader r, string name)
        {
            try
            {
                var ordinal = r.GetOrdinal(name);
                return r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        var dto = new SentMessageDto
        {
            MessageId = reader.GetInt64(reader.GetOrdinal("message_id")),
            ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
            SenderUserId = reader.GetInt64(reader.GetOrdinal("sender_user_id")),
            ReceiverUserId = ReadInt64(reader, "receiver_user_id"),
            MessageBody = reader.IsDBNull(reader.GetOrdinal("message_body"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("message_body")),
            MessageTypeId = reader.GetInt16(reader.GetOrdinal("message_type_id")),
            SentAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("sent_at")),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            AttachmentPath1 = ReadString(reader, "attachment_path_1"),
            AttachmentPath2 = ReadString(reader, "attachment_path_2"),
            AttachmentPath3 = ReadString(reader, "attachment_path_3"),
            AttachmentPath4 = ReadString(reader, "attachment_path_4"),
            AttachmentPath5 = ReadString(reader, "attachment_path_5"),
            GroupId = ReadInt64(reader, "group_id")
        };

        if (includeForwardMeta)
        {
            dto.ForwardedFromMessageId = ReadInt64(reader, "forwarded_from_message_id");
            dto.ForwardedBy = ReadInt64(reader, "forwarded_by");
        }

        return dto;
    }

    private static ChatNotificationDto MapNotification(NpgsqlDataReader reader)
    {
        static long? ReadInt64(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
        }

        static string? ReadString(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
        }

        static int? ReadInt32(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
        }

        return new ChatNotificationDto
        {
            NotificationId = reader.GetInt64(reader.GetOrdinal("notification_id")),
            UserId = reader.GetInt64(reader.GetOrdinal("user_id")),
            SenderUserId = ReadInt64(reader, "sender_user_id"),
            NotificationType = ReadString(reader, "notification_type"),
            Title = ReadString(reader, "title"),
            Message = ReadString(reader, "message"),
            ReferenceId = ReadInt64(reader, "reference_id"),
            ReferenceType = ReadString(reader, "reference_type"),
            ReferenceEntity = ReadString(reader, "reference_entity"),
            IsRead = !reader.IsDBNull(reader.GetOrdinal("is_read"))
                && reader.GetBoolean(reader.GetOrdinal("is_read")),
            CreatedDate = reader.GetDateTime(reader.GetOrdinal("created_date")),
            OrgId = reader.GetInt64(reader.GetOrdinal("org_id")),
            AppId = reader.GetInt32(reader.GetOrdinal("app_id")),
            FiscalYearId = ReadInt32(reader, "fiscal_year_id")
        };
    }

    private sealed record NotificationRow(ChatNotificationDto Notification, int UnreadCount);

    private static NotificationRow MapNotificationWithUnread(NpgsqlDataReader reader)
    {
        var notification = MapNotification(reader);
        var unreadOrdinal = reader.GetOrdinal("unread_count");
        var unread = reader.IsDBNull(unreadOrdinal) ? 0 : reader.GetInt32(unreadOrdinal);
        return new NotificationRow(notification, unread);
    }

    private static ChatMessageDto MapChatMessage(
        NpgsqlDataReader reader,
        long currentUserId,
        bool fromStarredList = false)
    {
        static DateTimeOffset? ReadOffset(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetFieldValue<DateTimeOffset>(ordinal);
        }

        static long? ReadInt64(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
        }

        static string? ReadString(NpgsqlDataReader r, string name)
        {
            var ordinal = r.GetOrdinal(name);
            return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
        }

        static bool HasColumn(NpgsqlDataReader r, string name)
        {
            for (var i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        var dto = new ChatMessageDto
        {
            MessageId = reader.GetInt64(reader.GetOrdinal("message_id")),
            ChatId = reader.GetInt32(reader.GetOrdinal("chat_id")),
            SenderUserId = reader.GetInt64(reader.GetOrdinal("sender_user_id")),
            ReceiverUserId = ReadInt64(reader, "receiver_user_id"),
            MessageBody = reader.GetString(reader.GetOrdinal("message_body")),
            MessageTypeId = reader.GetInt16(reader.GetOrdinal("message_type_id")),
            TypeName = reader.IsDBNull(reader.GetOrdinal("type_name"))
                ? null
                : reader.GetString(reader.GetOrdinal("type_name")),
            ParentMessageId = ReadInt64(reader, "parent_message_id"),
            SentAt = ReadOffset(reader, "sent_at"),
            ReadAt = ReadOffset(reader, "read_at"),
            CreatedAt = ReadOffset(reader, "created_at"),
            UpdatedAt = ReadOffset(reader, "updated_at"),
            AttachmentPath1 = ReadString(reader, "attachment_path_1"),
            AttachmentPath2 = ReadString(reader, "attachment_path_2"),
            AttachmentPath3 = ReadString(reader, "attachment_path_3"),
            AttachmentPath4 = ReadString(reader, "attachment_path_4"),
            AttachmentPath5 = ReadString(reader, "attachment_path_5"),
            IsStarredBySender = reader.GetBoolean(reader.GetOrdinal("is_starred_by_sender")),
            StarredBySenderAt = ReadOffset(reader, "starred_by_sender_at"),
            IsStarredByReceiver = reader.GetBoolean(reader.GetOrdinal("is_starred_by_receiver")),
            StarredByReceiverAt = ReadOffset(reader, "starred_by_receiver_at"),
            ForwardedFromMessageId = ReadInt64(reader, "forwarded_from_message_id"),
            ForwardedBy = ReadInt64(reader, "forwarded_by")
        };

        if (HasColumn(reader, "my_reaction_code"))
            dto.MyReactionCode = ReadString(reader, "my_reaction_code");
        if (HasColumn(reader, "my_reaction"))
            dto.MyReaction = ReadString(reader, "my_reaction");
        if (HasColumn(reader, "peer_reaction_code"))
            dto.PeerReactionCode = ReadString(reader, "peer_reaction_code");
        if (HasColumn(reader, "peer_reaction"))
            dto.PeerReaction = ReadString(reader, "peer_reaction");
        if (HasColumn(reader, "group_id"))
            dto.GroupId = ReadInt64(reader, "group_id");

        if (fromStarredList && HasColumn(reader, "is_starred_by_me"))
        {
            dto.IsStarredByMe = reader.GetBoolean(reader.GetOrdinal("is_starred_by_me"));
            dto.StarredAt = HasColumn(reader, "starred_at")
                ? ReadOffset(reader, "starred_at")
                : null;
            dto.PeerUserId = HasColumn(reader, "peer_user_id")
                ? ReadInt64(reader, "peer_user_id")
                : null;
        }
        else if (dto.SenderUserId == currentUserId)
        {
            dto.IsStarredByMe = dto.IsStarredBySender;
            dto.StarredAt = dto.StarredBySenderAt;
            dto.PeerUserId = dto.ReceiverUserId;
        }
        else
        {
            dto.IsStarredByMe = dto.IsStarredByReceiver;
            dto.StarredAt = dto.StarredByReceiverAt;
            dto.PeerUserId = dto.SenderUserId;
        }

        return dto;
    }
}
