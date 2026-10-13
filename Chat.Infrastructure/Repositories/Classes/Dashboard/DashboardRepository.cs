using Chat.Domain.DTOs.Dashboard;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Dashboard;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Dashboard;

/// <summary>
/// Dashboard aggregation over existing chat/employee/announcement tables.
/// No new tables. Soft-deleted and draft placeholder rows are excluded where applicable.
/// </summary>
public sealed class DashboardRepository : IDashboardRepository
{
    private static readonly string[] WeekDayLabels =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<DashboardRepository> _logger;

    public DashboardRepository(DatabaseHelper databaseHelper, ILogger<DashboardRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<DashboardResponseDto> GetDashboardAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        IReadOnlyCollection<long> onlineUserIds,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ChatOperationException("userId is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.", 400);

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var profile = await LoadProfileAsync(connection, userId, orgId, appId, cancellationToken)
                .ConfigureAwait(false);
            var timezone = SanitizeTimezone(profile.Timezone);

            var summary = new DashboardSummaryDto
            {
                // Unread = Messages + Groups for current user:
                // received not read by me, OR I sent and recipient(s) have not read yet.
                UnreadMessages = await ScalarIntAsync(
                    connection,
                    @"SELECT (
                          -- Messages: private received, unread
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          WHERE m.receiver_user_id = @p_user_id
                            AND m.sender_user_id IS DISTINCT FROM @p_user_id
                            AND m.group_id IS NULL
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND (
                                  @p_fiscal_year_id IS NULL
                                  OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                                  OR m.fiscal_year_id IS NULL
                                )
                            AND m.read_at IS NULL
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                      ) + (
                          -- Groups: received by me, not read yet
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          INNER JOIN public.tab_group_members gm
                              ON gm.group_id = m.group_id
                             AND gm.user_id = @p_user_id
                             AND gm.left_at IS NULL
                             AND gm.org_id = @p_org_id
                             AND gm.app_id = @p_app_id
                          WHERE m.group_id IS NOT NULL
                            AND m.sender_user_id IS DISTINCT FROM @p_user_id
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND (
                                  @p_fiscal_year_id IS NULL
                                  OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                                  OR m.fiscal_year_id IS NULL
                                )
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                            AND NOT EXISTS (
                                SELECT 1
                                FROM public.tab_message_receipts r
                                WHERE r.message_id = m.message_id
                                  AND r.user_id = @p_user_id
                                  AND r.read_at IS NOT NULL
                            )
                      ) + (
                          -- Messages: I sent private, receiver not read
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          WHERE m.sender_user_id = @p_user_id
                            AND m.receiver_user_id IS NOT NULL
                            AND m.group_id IS NULL
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND (
                                  @p_fiscal_year_id IS NULL
                                  OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                                  OR m.fiscal_year_id IS NULL
                                )
                            AND m.read_at IS NULL
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                      ) + (
                          -- Groups: I sent, each other member without read (Message Info Remaining)
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          INNER JOIN public.tab_group_members gm
                              ON gm.group_id = m.group_id
                             AND gm.left_at IS NULL
                             AND gm.org_id = @p_org_id
                             AND gm.app_id = @p_app_id
                             AND gm.user_id IS DISTINCT FROM @p_user_id
                          LEFT JOIN LATERAL (
                              SELECT rr.read_at
                              FROM public.tab_message_receipts rr
                              WHERE rr.message_id = m.message_id
                                AND rr.user_id = gm.user_id
                              ORDER BY rr.read_at DESC NULLS LAST
                              LIMIT 1
                          ) r ON TRUE
                          WHERE m.group_id IS NOT NULL
                            AND m.sender_user_id = @p_user_id
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND (
                                  @p_fiscal_year_id IS NULL
                                  OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                                  OR m.fiscal_year_id IS NULL
                                )
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                            AND r.read_at IS NULL
                      );",
                    BindTenant(userId, orgId, appId, fiscalYearId),
                    cancellationToken).ConfigureAwait(false),

                SentToday = await ScalarIntAsync(
                    connection,
                    @"SELECT COUNT(*)::integer
                      FROM public.tab_messages m
                      WHERE m.sender_user_id = @p_user_id
                        AND m.org_id = @p_org_id
                        AND m.app_id = @p_app_id
                        AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                        AND COALESCE(m.delete_flag, 0) = 0
                        AND m.deleted_at IS NULL
                        AND COALESCE(m.is_draft, false) = false
                        AND m.sent_at IS NOT NULL
                        AND (m.sent_at AT TIME ZONE @p_tz)::date
                            = (NOW() AT TIME ZONE @p_tz)::date;",
                    cmd =>
                    {
                        BindTenant(userId, orgId, appId, fiscalYearId)(cmd);
                        cmd.Parameters.AddWithValue("p_tz", timezone);
                    },
                    cancellationToken).ConfigureAwait(false),

                PendingReads = await ScalarIntAsync(
                    connection,
                    @"SELECT (
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          WHERE m.sender_user_id = @p_user_id
                            AND m.receiver_user_id IS NOT NULL
                            AND m.group_id IS NULL
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                            AND m.read_at IS NULL
                      ) + (
                          SELECT COUNT(*)::integer
                          FROM public.tab_messages m
                          WHERE m.sender_user_id = @p_user_id
                            AND m.group_id IS NOT NULL
                            AND m.org_id = @p_org_id
                            AND m.app_id = @p_app_id
                            AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                            AND COALESCE(m.delete_flag, 0) = 0
                            AND m.deleted_at IS NULL
                            AND COALESCE(m.is_draft, false) = false
                            AND m.sent_at IS NOT NULL
                            AND EXISTS (
                                SELECT 1
                                FROM public.tab_group_members gm
                                WHERE gm.group_id = m.group_id
                                  AND gm.user_id::bigint IS DISTINCT FROM @p_user_id
                                  AND gm.left_at IS NULL
                                  AND NOT EXISTS (
                                      SELECT 1
                                      FROM public.tab_message_receipts r
                                      WHERE r.message_id = m.message_id
                                        AND r.user_id::bigint = gm.user_id::bigint
                                        AND r.read_at IS NOT NULL
                                  )
                            )
                      );",
                    BindTenant(userId, orgId, appId, fiscalYearId),
                    cancellationToken).ConfigureAwait(false),

                Announcements = await ScalarIntAsync(
                    connection,
                    @"SELECT COUNT(*)::integer
                      FROM public.tab_announcement_recipients r
                      INNER JOIN public.tab_announcements a
                          ON a.announcement_id = r.announcement_id
                      WHERE r.user_id::bigint = @p_user_id
                        AND LOWER(TRIM(COALESCE(r.read_status, ''))) <> 'read'
                        AND a.org_id = @p_org_id
                        AND a.app_id = @p_app_id
                        AND (a.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                             OR @p_fiscal_year_id IS NULL)
                        AND COALESCE(a.delete_flag, 0) = 0
                        AND LOWER(TRIM(COALESCE(a.status_name, ''))) <> 'deleted';",
                    BindTenant(userId, orgId, appId, fiscalYearId),
                    cancellationToken).ConfigureAwait(false),

                // Real draft bodies only — empty is_draft rows are private-chat anchors.
                Drafts = await ScalarIntAsync(
                    connection,
                    @"SELECT COUNT(*)::integer
                      FROM public.tab_messages m
                      WHERE m.sender_user_id = @p_user_id
                        AND m.org_id = @p_org_id
                        AND m.app_id = @p_app_id
                        AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
                        AND COALESCE(m.is_draft, false) = true
                        AND COALESCE(m.delete_flag, 0) = 0
                        AND m.deleted_at IS NULL
                        AND NULLIF(TRIM(COALESCE(m.message_body, '')), '') IS NOT NULL;",
                    BindTenant(userId, orgId, appId, fiscalYearId),
                    cancellationToken).ConfigureAwait(false),
            };

            // Online Employees total/count are resolved on the React UI from SoftOnCloud
            // GET /api/organisations/{orgId}/products/{productId}/users + SignalR presence.
            // Do not use tab_employee_master for that card.
            var onlineSet = onlineUserIds is HashSet<long> hs
                ? hs
                : new HashSet<long>(onlineUserIds);
            summary.TotalEmployees = 0;
            summary.OnlineEmployees = 0;

            var recentMessages = await LoadRecentMessagesAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var unreadMessages = await LoadUnreadMessagesAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var recentSent = await LoadRecentSentMessagesAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var sentTodayList = await LoadSentTodayListAsync(
                connection, userId, orgId, appId, fiscalYearId, timezone, cancellationToken)
                .ConfigureAwait(false);
            var departments = await LoadDepartmentSummaryAsync(
                connection, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var announcements = await LoadRecentAnnouncementsAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var unreadAnnouncements = await LoadUnreadAnnouncementsAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var readUnread = await LoadReadUnreadAsync(
                connection, userId, orgId, appId, fiscalYearId, cancellationToken)
                .ConfigureAwait(false);
            var (weekly, weeklyTotals) = await LoadPeriodAnalyticsAsync(
                connection, userId, orgId, appId, fiscalYearId, timezone, "week", cancellationToken)
                .ConfigureAwait(false);
            var (monthly, monthlyTotals) = await LoadPeriodAnalyticsAsync(
                connection, userId, orgId, appId, fiscalYearId, timezone, "month", cancellationToken)
                .ConfigureAwait(false);
            var (yearly, yearlyTotals) = await LoadPeriodAnalyticsAsync(
                connection, userId, orgId, appId, fiscalYearId, timezone, "year", cancellationToken)
                .ConfigureAwait(false);

            profile.Status = onlineSet.Contains(userId) ? "Online" : "Offline";

            return new DashboardResponseDto
            {
                Summary = summary,
                RecentMessages = recentMessages,
                UnreadMessageList = unreadMessages,
                RecentSentMessages = recentSent,
                SentTodayList = sentTodayList,
                DepartmentMessageSummary = departments,
                RecentAnnouncements = announcements,
                UnreadAnnouncementList = unreadAnnouncements,
                ReadUnreadSummary = readUnread,
                WeeklyAnalytics = weekly,
                WeeklyTotals = weeklyTotals,
                MonthlyAnalytics = monthly,
                MonthlyTotals = monthlyTotals,
                YearlyAnalytics = yearly,
                YearlyTotals = yearlyTotals,
                Profile = profile,
                FavoriteContacts = Array.Empty<DashboardFavoriteContactDto>(),
                EmployeeUserIds = Array.Empty<long>()
            };
        }
        catch (ChatOperationException)
        {
            throw;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetDashboardAsync. SqlState={SqlState}", ex.SqlState);
            throw new ChatOperationException(ex.MessageText, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetDashboardAsync");
            throw;
        }
    }

    private static Action<NpgsqlCommand> BindTenant(
        long userId, int orgId, int appId, int? fiscalYearId) =>
        cmd =>
        {
            cmd.Parameters.AddWithValue("p_user_id", userId);
            cmd.Parameters.AddWithValue("p_org_id", orgId);
            cmd.Parameters.AddWithValue("p_app_id", appId);
            cmd.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                (object?)fiscalYearId ?? DBNull.Value;
        };

    private async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        bind(command);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull) return 0;
        return Convert.ToInt32(result);
    }

    private async Task<DashboardProfileDto> LoadProfileAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId,
        CancellationToken cancellationToken)
    {
        // SoftOnCloud owns profile / employee / role / branch data.
        // UI fills FullName / Role / Department / Photo from SoftOnCloud APIs.
        _ = connection;
        _ = orgId;
        _ = appId;
        _ = cancellationToken;
        await Task.CompletedTask.ConfigureAwait(false);
        return new DashboardProfileDto { UserId = userId, Timezone = "UTC" };
    }

    private async Task<IReadOnlyList<DashboardRecentMessageDto>> LoadRecentMessagesAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT *
FROM (
    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        m.sender_user_id::bigint AS sender_user_id,
        ('User ' || m.sender_user_id::text) AS sender_name,
        NULL::text AS sender_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        (m.read_at IS NULL) AS is_unread,
        false AS is_group,
        NULL::bigint AS group_id,
        NULL::text AS group_name
    FROM public.tab_messages m
    WHERE m.receiver_user_id = @p_user_id
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.group_id IS NULL
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        m.sender_user_id::bigint AS sender_user_id,
        ('User ' || m.sender_user_id::text) AS sender_name,
        NULL::text AS sender_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        NOT EXISTS (
            SELECT 1
            FROM public.tab_message_receipts r
            WHERE r.message_id = m.message_id
              AND r.user_id::bigint = @p_user_id
              AND r.read_at IS NOT NULL
        ) AS is_unread,
        true AS is_group,
        m.group_id::bigint AS group_id,
        COALESCE(g.group_name, 'Group') AS group_name
    FROM public.tab_messages m
    INNER JOIN public.tab_group_members gm
        ON gm.group_id = m.group_id
       AND gm.user_id::bigint = @p_user_id
       AND gm.left_at IS NULL
    LEFT JOIN public.tab_groups g
        ON g.group_id = m.group_id
    WHERE m.group_id IS NOT NULL
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
) q
ORDER BY q.sent_at DESC
LIMIT 5;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        var list = new List<DashboardRecentMessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DashboardRecentMessageDto
            {
                MessageId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("message_id"))),
                ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("chat_id"))),
                SenderUserId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("sender_user_id"))),
                SenderName = reader.GetString(reader.GetOrdinal("sender_name")),
                SenderPhotoUrl = reader.IsDBNull(reader.GetOrdinal("sender_photo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("sender_photo_url")),
                Preview = Truncate(reader.GetString(reader.GetOrdinal("preview")), 120),
                SentAt = ReadTimestamp(reader, "sent_at"),
                IsUnread = reader.GetBoolean(reader.GetOrdinal("is_unread")),
                IsGroup = reader.GetBoolean(reader.GetOrdinal("is_group")),
                GroupId = reader.IsDBNull(reader.GetOrdinal("group_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("group_id"))),
                GroupName = reader.IsDBNull(reader.GetOrdinal("group_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("group_name"))
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DashboardRecentMessageDto>> LoadUnreadMessagesAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        // Same 4 buckets as UnreadMessages count. Display user = other party (sender or recipient/member).
        const string sql = @"
SELECT *
FROM (
    -- 1) Private received (unread)
    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        m.sender_user_id::bigint AS display_user_id,
        'User ' || m.sender_user_id::text AS display_name,
        NULL::text AS display_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        false AS is_outgoing,
        false AS is_group,
        NULL::bigint AS group_id,
        NULL::text AS group_name
    FROM public.tab_messages m
    WHERE m.receiver_user_id = @p_user_id
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.group_id IS NULL
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND m.read_at IS NULL
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    -- 2) Group received (unread by me)
    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        m.sender_user_id::bigint AS display_user_id,
        'User ' || m.sender_user_id::text AS display_name,
        NULL::text AS display_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        false AS is_outgoing,
        true AS is_group,
        m.group_id::bigint AS group_id,
        COALESCE(g.group_name, 'Group') AS group_name
    FROM public.tab_messages m
    INNER JOIN public.tab_group_members gm
        ON gm.group_id = m.group_id
       AND gm.user_id = @p_user_id
       AND gm.left_at IS NULL
       AND gm.org_id = @p_org_id
       AND gm.app_id = @p_app_id
    LEFT JOIN public.tab_groups g
        ON g.group_id = m.group_id
    WHERE m.group_id IS NOT NULL
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM public.tab_message_receipts r
          WHERE r.message_id = m.message_id
            AND r.user_id = @p_user_id
            AND r.read_at IS NOT NULL
      )

    UNION ALL

    -- 3) Private sent — receiver has not read (display = receiver)
    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        m.receiver_user_id::bigint AS display_user_id,
        ('User ' || m.receiver_user_id::text) AS display_name,
        NULL::text AS display_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        true AS is_outgoing,
        false AS is_group,
        NULL::bigint AS group_id,
        NULL::text AS group_name
    FROM public.tab_messages m
    WHERE m.sender_user_id = @p_user_id
      AND m.receiver_user_id IS NOT NULL
      AND m.group_id IS NULL
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND m.read_at IS NULL
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    -- 4) Group sent — each other member without read (display = member)
    SELECT
        m.message_id,
        m.chat_id::bigint AS chat_id,
        gm.user_id::bigint AS display_user_id,
        'User ' || gm.user_id::text AS display_name,
        NULL::text AS display_photo_url,
        COALESCE(m.message_body, '') AS preview,
        m.sent_at,
        true AS is_outgoing,
        true AS is_group,
        m.group_id::bigint AS group_id,
        COALESCE(g.group_name, 'Group') AS group_name
    FROM public.tab_messages m
    INNER JOIN public.tab_group_members gm
        ON gm.group_id = m.group_id
       AND gm.left_at IS NULL
       AND gm.org_id = @p_org_id
       AND gm.app_id = @p_app_id
       AND gm.user_id IS DISTINCT FROM @p_user_id
    LEFT JOIN LATERAL (
        SELECT rr.read_at
        FROM public.tab_message_receipts rr
        WHERE rr.message_id = m.message_id
          AND rr.user_id = gm.user_id
        ORDER BY rr.read_at DESC NULLS LAST
        LIMIT 1
    ) r ON TRUE
    LEFT JOIN public.tab_groups g
        ON g.group_id = m.group_id
    WHERE m.group_id IS NOT NULL
      AND m.sender_user_id = @p_user_id
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND r.read_at IS NULL
) q
ORDER BY q.sent_at DESC
LIMIT 100;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        var list = new List<DashboardRecentMessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DashboardRecentMessageDto
            {
                MessageId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("message_id"))),
                ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("chat_id"))),
                SenderUserId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("display_user_id"))),
                SenderName = reader.GetString(reader.GetOrdinal("display_name")),
                SenderPhotoUrl = reader.IsDBNull(reader.GetOrdinal("display_photo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("display_photo_url")),
                Preview = Truncate(reader.GetString(reader.GetOrdinal("preview")), 120),
                SentAt = ReadTimestamp(reader, "sent_at"),
                IsUnread = true,
                IsOutgoing = reader.GetBoolean(reader.GetOrdinal("is_outgoing")),
                IsGroup = reader.GetBoolean(reader.GetOrdinal("is_group")),
                GroupId = reader.IsDBNull(reader.GetOrdinal("group_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("group_id"))),
                GroupName = reader.IsDBNull(reader.GetOrdinal("group_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("group_name"))
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DashboardRecentSentMessageDto>> LoadSentTodayListAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId, string timezone,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    m.message_id,
    m.chat_id::bigint AS chat_id,
    m.receiver_user_id::bigint AS recipient_user_id,
    CASE
        WHEN m.group_id IS NOT NULL THEN COALESCE(g.group_name, 'Group')
        ELSE 'User ' || COALESCE(m.receiver_user_id::text, '')
    END AS recipient_name,
    CASE
        WHEN m.group_id IS NOT NULL THEN g.profile_pic
        ELSE NULL::text
    END AS recipient_photo_url,
    COALESCE(m.message_body, '') AS preview,
    m.sent_at,
    (m.group_id IS NOT NULL) AS is_group,
    m.group_id::bigint AS group_id,
    false AS is_read
FROM public.tab_messages m
LEFT JOIN public.tab_groups g
    ON g.group_id = m.group_id
WHERE m.sender_user_id = @p_user_id
  AND m.org_id = @p_org_id
  AND m.app_id = @p_app_id
  AND (
        @p_fiscal_year_id IS NULL
        OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
        OR m.fiscal_year_id IS NULL
      )
  AND COALESCE(m.delete_flag, 0) = 0
  AND m.deleted_at IS NULL
  AND COALESCE(m.is_draft, false) = false
  AND m.sent_at IS NOT NULL
  AND (m.sent_at AT TIME ZONE @p_tz)::date = (NOW() AT TIME ZONE @p_tz)::date
ORDER BY m.sent_at DESC
LIMIT 50;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);
        command.Parameters.AddWithValue("p_tz", timezone);

        var list = new List<DashboardRecentSentMessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DashboardRecentSentMessageDto
            {
                MessageId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("message_id"))),
                ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("chat_id"))),
                RecipientUserId = reader.IsDBNull(reader.GetOrdinal("recipient_user_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("recipient_user_id"))),
                RecipientName = reader.GetString(reader.GetOrdinal("recipient_name")),
                RecipientPhotoUrl = reader.IsDBNull(reader.GetOrdinal("recipient_photo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("recipient_photo_url")),
                Preview = Truncate(reader.GetString(reader.GetOrdinal("preview")), 120),
                SentAt = ReadTimestamp(reader, "sent_at"),
                IsGroup = reader.GetBoolean(reader.GetOrdinal("is_group")),
                GroupId = reader.IsDBNull(reader.GetOrdinal("group_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("group_id"))),
                IsRead = false,
                ReadStatus = null
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DashboardRecentSentMessageDto>> LoadRecentSentMessagesAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    m.message_id,
    m.chat_id::bigint AS chat_id,
    m.receiver_user_id::bigint AS recipient_user_id,
    CASE
        WHEN m.group_id IS NOT NULL THEN COALESCE(g.group_name, 'Group')
        ELSE 'User ' || COALESCE(m.receiver_user_id::text, '')
    END AS recipient_name,
    CASE
        WHEN m.group_id IS NOT NULL THEN g.profile_pic
        ELSE NULL::text
    END AS recipient_photo_url,
    COALESCE(m.message_body, '') AS preview,
    m.sent_at,
    (m.group_id IS NOT NULL) AS is_group,
    m.group_id::bigint AS group_id,
    CASE
        WHEN m.group_id IS NULL THEN (m.read_at IS NOT NULL)
        ELSE NOT EXISTS (
            SELECT 1
            FROM public.tab_group_members gm
            WHERE gm.group_id = m.group_id
              AND gm.user_id::bigint IS DISTINCT FROM @p_user_id
              AND gm.left_at IS NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM public.tab_message_receipts r
                  WHERE r.message_id = m.message_id
                    AND r.user_id::bigint = gm.user_id::bigint
                    AND r.read_at IS NOT NULL
              )
        )
    END AS is_read
FROM public.tab_messages m
LEFT JOIN public.tab_groups g
    ON g.group_id = m.group_id
WHERE m.sender_user_id = @p_user_id
  AND m.org_id = @p_org_id
  AND m.app_id = @p_app_id
  AND m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
  AND COALESCE(m.delete_flag, 0) = 0
  AND m.deleted_at IS NULL
  AND COALESCE(m.is_draft, false) = false
  AND m.sent_at IS NOT NULL
ORDER BY m.sent_at DESC
LIMIT 5;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        var list = new List<DashboardRecentSentMessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var isRead = reader.GetBoolean(reader.GetOrdinal("is_read"));
            list.Add(new DashboardRecentSentMessageDto
            {
                MessageId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("message_id"))),
                ChatId = reader.IsDBNull(reader.GetOrdinal("chat_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("chat_id"))),
                RecipientUserId = reader.IsDBNull(reader.GetOrdinal("recipient_user_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("recipient_user_id"))),
                RecipientName = reader.GetString(reader.GetOrdinal("recipient_name")),
                RecipientPhotoUrl = reader.IsDBNull(reader.GetOrdinal("recipient_photo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("recipient_photo_url")),
                Preview = Truncate(reader.GetString(reader.GetOrdinal("preview")), 120),
                SentAt = ReadTimestamp(reader, "sent_at"),
                IsGroup = reader.GetBoolean(reader.GetOrdinal("is_group")),
                GroupId = reader.IsDBNull(reader.GetOrdinal("group_id"))
                    ? null
                    : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("group_id"))),
                IsRead = isRead,
                ReadStatus = isRead ? "Read" : "Pending"
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DashboardDepartmentSummaryDto>> LoadDepartmentSummaryAsync(
        NpgsqlConnection connection,
        int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        // SoftOnCloud owns department names (product-users API). Chat DB has no reliable
        // department_name column — return a single aggregate bucket for the chart.
        const string sql = @"
SELECT
    'Other'::text AS department,
    COUNT(*)::integer AS message_count
FROM public.tab_messages m
WHERE m.org_id = @p_org_id
  AND m.app_id = @p_app_id
  AND (
        @p_fiscal_year_id IS NULL
        OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
        OR m.fiscal_year_id IS NULL
      )
  AND COALESCE(m.delete_flag, 0) = 0
  AND m.deleted_at IS NULL
  AND COALESCE(m.is_draft, false) = false
  AND m.sent_at IS NOT NULL
GROUP BY 1
ORDER BY message_count DESC, department ASC;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("p_org_id", orgId);
        command.Parameters.AddWithValue("p_app_id", appId);
        command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
            (object?)fiscalYearId ?? DBNull.Value;

        var rows = new List<(string Department, int Count)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add((
                reader.GetString(reader.GetOrdinal("department")),
                reader.GetInt32(reader.GetOrdinal("message_count"))
            ));
        }

        var total = rows.Sum(r => r.Count);
        return rows.Select(r => new DashboardDepartmentSummaryDto
        {
            Department = r.Department,
            MessageCount = r.Count,
            Percentage = total <= 0
                ? 0
                : Math.Round((decimal)r.Count * 100m / total, 1)
        }).ToList();
    }

    private async Task<IReadOnlyList<DashboardAnnouncementDto>> LoadRecentAnnouncementsAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        // SoftOnCloud owns department display names — tab_announcements has department_id only.
        // Do not join tab_department_master or select a.department_name (column does not exist).
        const string sql = @"
SELECT
    a.announcement_id,
    COALESCE(a.title, '') AS title,
    COALESCE(a.description, '') AS description,
    c.category_name,
    ''::text AS department_name,
    CASE
        WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 3
        WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 2
        ELSE 1
    END AS status_id,
    CASE
        WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'Expired'
        WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'Expiring Soon'
        ELSE COALESCE(NULLIF(TRIM(a.status_name), ''), 'Active')
    END AS status_name,
    COALESCE(a.published_at, a.created_at, NOW()) AS posted_date,
    a.expires_at,
    COALESCE(a.is_important, false) AS is_important,
    COALESCE(a.is_pinned, false) AS is_pinned,
    COALESCE(a.posted_by, 0)::bigint AS posted_by,
    '' AS posted_by_name
FROM public.tab_announcements a
LEFT JOIN public.tab_announcement_category_master c
    ON c.category_id = a.category_id
   AND c.org_id = a.org_id
   AND c.app_id = a.app_id
WHERE a.org_id = @p_org_id
  AND a.app_id = @p_app_id
  AND (a.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id OR @p_fiscal_year_id IS NULL)
  AND COALESCE(a.delete_flag, 0) = 0
  AND LOWER(TRIM(COALESCE(a.status_name, ''))) <> 'deleted'
  AND (
      a.posted_by = @p_user_id
      OR EXISTS (
          SELECT 1
          FROM public.tab_announcement_recipients r
          WHERE r.announcement_id = a.announcement_id
            AND r.user_id::bigint = @p_user_id
      )
  )
ORDER BY COALESCE(a.is_pinned, false) DESC, COALESCE(a.published_at, a.created_at) DESC
LIMIT 5;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        var list = new List<DashboardAnnouncementDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DashboardAnnouncementDto
            {
                AnnouncementId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("announcement_id"))),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
                CategoryName = reader.IsDBNull(reader.GetOrdinal("category_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("category_name")),
                DepartmentName = reader.IsDBNull(reader.GetOrdinal("department_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("department_name")),
                StatusId = reader.GetInt32(reader.GetOrdinal("status_id")),
                StatusName = reader.GetString(reader.GetOrdinal("status_name")),
                PostedDate = ReadTimestamp(reader, "posted_date"),
                ExpiryDate = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                    ? null
                    : ReadTimestamp(reader, "expires_at"),
                IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
                IsPinned = reader.GetBoolean(reader.GetOrdinal("is_pinned")),
                PostedBy = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("posted_by"))),
                PostedByName = reader.IsDBNull(reader.GetOrdinal("posted_by_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("posted_by_name"))
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DashboardAnnouncementDto>> LoadUnreadAnnouncementsAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        // SoftOnCloud owns department display names — do not select a.department_name
        // or join tab_department_master (d.department_name does not exist on Chat tenant).
        const string sql = @"
SELECT
    a.announcement_id,
    COALESCE(a.title, '') AS title,
    COALESCE(a.description, '') AS description,
    c.category_name,
    ''::text AS department_name,
    CASE
        WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 3
        WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 2
        ELSE 1
    END AS status_id,
    CASE
        WHEN a.expires_at IS NOT NULL AND a.expires_at < NOW() THEN 'Expired'
        WHEN a.expires_at IS NOT NULL AND a.expires_at <= (NOW() + INTERVAL '7 days') THEN 'Expiring Soon'
        ELSE COALESCE(NULLIF(TRIM(a.status_name), ''), 'Active')
    END AS status_name,
    COALESCE(a.published_at, a.created_at, NOW()) AS posted_date,
    a.expires_at,
    COALESCE(a.is_important, false) AS is_important,
    COALESCE(a.is_pinned, false) AS is_pinned,
    COALESCE(a.posted_by, 0)::bigint AS posted_by,
    '' AS posted_by_name
FROM public.tab_announcement_recipients r
INNER JOIN public.tab_announcements a
    ON a.announcement_id = r.announcement_id
LEFT JOIN public.tab_announcement_category_master c
    ON c.category_id = a.category_id
   AND c.org_id = a.org_id
   AND c.app_id = a.app_id
WHERE r.user_id::bigint = @p_user_id
  AND LOWER(TRIM(COALESCE(r.read_status, ''))) <> 'read'
  AND a.org_id = @p_org_id
  AND a.app_id = @p_app_id
  AND (a.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id OR @p_fiscal_year_id IS NULL)
  AND COALESCE(a.delete_flag, 0) = 0
  AND LOWER(TRIM(COALESCE(a.status_name, ''))) <> 'deleted'
ORDER BY COALESCE(a.is_pinned, false) DESC, COALESCE(a.published_at, a.created_at) DESC
LIMIT 50;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        var list = new List<DashboardAnnouncementDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DashboardAnnouncementDto
            {
                AnnouncementId = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("announcement_id"))),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
                CategoryName = reader.IsDBNull(reader.GetOrdinal("category_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("category_name")),
                DepartmentName = reader.IsDBNull(reader.GetOrdinal("department_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("department_name")),
                StatusId = reader.GetInt32(reader.GetOrdinal("status_id")),
                StatusName = reader.GetString(reader.GetOrdinal("status_name")),
                PostedDate = ReadTimestamp(reader, "posted_date"),
                ExpiryDate = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                    ? null
                    : ReadTimestamp(reader, "expires_at"),
                IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
                IsPinned = reader.GetBoolean(reader.GetOrdinal("is_pinned")),
                PostedBy = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("posted_by"))),
                PostedByName = reader.IsDBNull(reader.GetOrdinal("posted_by_name"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("posted_by_name"))
            });
        }

        return list;
    }

    /// <summary>
    /// Read vs Unread from Messages + Groups:
    /// received by me (read/unread) + I sent awaiting recipient read (per member for groups).
    /// </summary>
    private async Task<DashboardReadUnreadDto> LoadReadUnreadAsync(
        NpgsqlConnection connection,
        long userId, int orgId, int appId, int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    COALESCE(SUM(CASE WHEN q.is_unread THEN 0 ELSE 1 END), 0)::integer AS read_count,
    COALESCE(SUM(CASE WHEN q.is_unread THEN 1 ELSE 0 END), 0)::integer AS unread_count
FROM (
    -- Messages: private received
    SELECT (m.read_at IS NULL) AS is_unread
    FROM public.tab_messages m
    WHERE m.receiver_user_id = @p_user_id
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.group_id IS NULL
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    -- Groups: received by me
    SELECT NOT EXISTS (
        SELECT 1
        FROM public.tab_message_receipts r
        WHERE r.message_id = m.message_id
          AND r.user_id = @p_user_id
          AND r.read_at IS NOT NULL
    ) AS is_unread
    FROM public.tab_messages m
    INNER JOIN public.tab_group_members gm
        ON gm.group_id = m.group_id
       AND gm.user_id = @p_user_id
       AND gm.left_at IS NULL
       AND gm.org_id = @p_org_id
       AND gm.app_id = @p_app_id
    WHERE m.group_id IS NOT NULL
      AND m.sender_user_id IS DISTINCT FROM @p_user_id
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    -- Messages: I sent private
    SELECT (m.read_at IS NULL) AS is_unread
    FROM public.tab_messages m
    WHERE m.sender_user_id = @p_user_id
      AND m.receiver_user_id IS NOT NULL
      AND m.group_id IS NULL
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL

    UNION ALL

    -- Groups: I sent — one slot per other member (Message Info Remaining)
    SELECT (r.read_at IS NULL) AS is_unread
    FROM public.tab_messages m
    INNER JOIN public.tab_group_members gm
        ON gm.group_id = m.group_id
       AND gm.left_at IS NULL
       AND gm.org_id = @p_org_id
       AND gm.app_id = @p_app_id
       AND gm.user_id IS DISTINCT FROM @p_user_id
    LEFT JOIN LATERAL (
        SELECT rr.read_at
        FROM public.tab_message_receipts rr
        WHERE rr.message_id = m.message_id
          AND rr.user_id = gm.user_id
        ORDER BY rr.read_at DESC NULLS LAST
        LIMIT 1
    ) r ON TRUE
    WHERE m.group_id IS NOT NULL
      AND m.sender_user_id = @p_user_id
      AND m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
) q;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new DashboardReadUnreadDto();

        return new DashboardReadUnreadDto
        {
            Read = reader.GetInt32(reader.GetOrdinal("read_count")),
            Unread = reader.GetInt32(reader.GetOrdinal("unread_count"))
        };
    }

    private static readonly string[] MonthLabels =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private async Task<(IReadOnlyList<DashboardWeeklyPointDto> Points, DashboardWeeklyTotalsDto Totals)>
        LoadPeriodAnalyticsAsync(
            NpgsqlConnection connection,
            long userId, int orgId, int appId, int? fiscalYearId, string timezone,
            string period,
            CancellationToken cancellationToken)
    {
        var mode = string.IsNullOrWhiteSpace(period)
            ? "week"
            : period.Trim().ToLowerInvariant();
        if (mode != "week" && mode != "month" && mode != "year")
            mode = "week";

        // Shared message filter; buckets differ by period.
        string sql = mode switch
        {
            "month" => @"
WITH bounds AS (
    SELECT
        date_trunc('month', (NOW() AT TIME ZONE @p_tz))::date AS range_start,
        (date_trunc('month', (NOW() AT TIME ZONE @p_tz)) + INTERVAL '1 month' - INTERVAL '1 day')::date AS range_end
),
buckets AS (
    SELECT generate_series(b.range_start, b.range_end, INTERVAL '1 day')::date AS bucket_date
    FROM bounds b
),
msg AS (
    SELECT
        (m.sent_at AT TIME ZONE @p_tz)::date AS day_date,
        m.sender_user_id,
        m.parent_message_id
    FROM public.tab_messages m
    WHERE m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND (
            m.sender_user_id = @p_user_id
            OR m.receiver_user_id = @p_user_id
            OR EXISTS (
                SELECT 1
                FROM public.tab_group_members gm
                WHERE gm.group_id = m.group_id
                  AND gm.user_id::bigint = @p_user_id
                  AND gm.left_at IS NULL
            )
        )
)
SELECT
    b.bucket_date AS sort_date,
    EXTRACT(DAY FROM b.bucket_date)::integer AS ord,
    to_char(b.bucket_date, 'DD') AS label,
    COALESCE(COUNT(m.day_date), 0)::integer AS message_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id = @p_user_id), 0)::integer AS sent_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id IS DISTINCT FROM @p_user_id), 0)::integer AS received_count,
    COALESCE(COUNT(*) FILTER (
        WHERE m.sender_user_id = @p_user_id AND m.parent_message_id IS NOT NULL
    ), 0)::integer AS reply_count
FROM buckets b
LEFT JOIN msg m ON m.day_date = b.bucket_date
GROUP BY b.bucket_date
ORDER BY b.bucket_date;",
            "year" => @"
WITH bounds AS (
    SELECT date_trunc('year', (NOW() AT TIME ZONE @p_tz))::date AS year_start
),
buckets AS (
    SELECT
        (b.year_start + (gs.month_offset * INTERVAL '1 month'))::date AS month_start,
        gs.month_offset + 1 AS ord
    FROM bounds b
    CROSS JOIN generate_series(0, 11) AS gs(month_offset)
),
msg AS (
    SELECT
        date_trunc('month', (m.sent_at AT TIME ZONE @p_tz))::date AS month_date,
        m.sender_user_id,
        m.parent_message_id
    FROM public.tab_messages m
    WHERE m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND (m.sent_at AT TIME ZONE @p_tz) >= (SELECT year_start FROM bounds)
      AND (m.sent_at AT TIME ZONE @p_tz) < ((SELECT year_start FROM bounds) + INTERVAL '1 year')
      AND (
            m.sender_user_id = @p_user_id
            OR m.receiver_user_id = @p_user_id
            OR EXISTS (
                SELECT 1
                FROM public.tab_group_members gm
                WHERE gm.group_id = m.group_id
                  AND gm.user_id::bigint = @p_user_id
                  AND gm.left_at IS NULL
            )
        )
)
SELECT
    b.month_start AS sort_date,
    b.ord,
    to_char(b.month_start, 'Mon') AS label,
    COALESCE(COUNT(m.month_date), 0)::integer AS message_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id = @p_user_id), 0)::integer AS sent_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id IS DISTINCT FROM @p_user_id), 0)::integer AS received_count,
    COALESCE(COUNT(*) FILTER (
        WHERE m.sender_user_id = @p_user_id AND m.parent_message_id IS NOT NULL
    ), 0)::integer AS reply_count
FROM buckets b
LEFT JOIN msg m ON m.month_date = b.month_start
GROUP BY b.month_start, b.ord
ORDER BY b.ord;",
            _ => @"
WITH bounds AS (
    SELECT
        date_trunc('week', (NOW() AT TIME ZONE @p_tz))::date AS range_start,
        (date_trunc('week', (NOW() AT TIME ZONE @p_tz)) + INTERVAL '6 days')::date AS range_end
),
buckets AS (
    SELECT generate_series(b.range_start, b.range_end, INTERVAL '1 day')::date AS bucket_date
    FROM bounds b
),
msg AS (
    SELECT
        (m.sent_at AT TIME ZONE @p_tz)::date AS day_date,
        m.sender_user_id,
        m.parent_message_id
    FROM public.tab_messages m
    WHERE m.org_id = @p_org_id
      AND m.app_id = @p_app_id
      AND (
            @p_fiscal_year_id IS NULL
            OR m.fiscal_year_id IS NOT DISTINCT FROM @p_fiscal_year_id
            OR m.fiscal_year_id IS NULL
          )
      AND COALESCE(m.delete_flag, 0) = 0
      AND m.deleted_at IS NULL
      AND COALESCE(m.is_draft, false) = false
      AND m.sent_at IS NOT NULL
      AND (
            m.sender_user_id = @p_user_id
            OR m.receiver_user_id = @p_user_id
            OR EXISTS (
                SELECT 1
                FROM public.tab_group_members gm
                WHERE gm.group_id = m.group_id
                  AND gm.user_id::bigint = @p_user_id
                  AND gm.left_at IS NULL
            )
        )
)
SELECT
    b.bucket_date AS sort_date,
    EXTRACT(ISODOW FROM b.bucket_date)::integer AS ord,
    CASE EXTRACT(ISODOW FROM b.bucket_date)::integer
        WHEN 1 THEN 'Monday'
        WHEN 2 THEN 'Tuesday'
        WHEN 3 THEN 'Wednesday'
        WHEN 4 THEN 'Thursday'
        WHEN 5 THEN 'Friday'
        WHEN 6 THEN 'Saturday'
        ELSE 'Sunday'
    END AS label,
    COALESCE(COUNT(m.day_date), 0)::integer AS message_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id = @p_user_id), 0)::integer AS sent_count,
    COALESCE(COUNT(*) FILTER (WHERE m.sender_user_id IS DISTINCT FROM @p_user_id), 0)::integer AS received_count,
    COALESCE(COUNT(*) FILTER (
        WHERE m.sender_user_id = @p_user_id AND m.parent_message_id IS NOT NULL
    ), 0)::integer AS reply_count
FROM buckets b
LEFT JOIN msg m ON m.day_date = b.bucket_date
GROUP BY b.bucket_date
ORDER BY b.bucket_date;"
        };

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _databaseHelper.CommandTimeoutSeconds
        };
        BindTenant(userId, orgId, appId, fiscalYearId)(command);
        command.Parameters.AddWithValue("p_tz", timezone);

        var points = new List<DashboardWeeklyPointDto>();
        var totals = new DashboardWeeklyTotalsDto();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var ord = reader.GetInt32(reader.GetOrdinal("ord"));
            var label = reader.GetString(reader.GetOrdinal("label"));
            var sortDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("sort_date")));
            var count = reader.GetInt32(reader.GetOrdinal("message_count"));
            points.Add(new DashboardWeeklyPointDto
            {
                Day = label,
                DayOfWeek = ord,
                Count = count,
                Date = sortDate
            });
            totals.Sent += reader.GetInt32(reader.GetOrdinal("sent_count"));
            totals.Received += reader.GetInt32(reader.GetOrdinal("received_count"));
            totals.Replies += reader.GetInt32(reader.GetOrdinal("reply_count"));
        }

        if (points.Count == 0)
        {
            if (mode == "year")
            {
                var yearStart = new DateOnly(DateTime.UtcNow.Year, 1, 1);
                for (var i = 0; i < 12; i++)
                {
                    points.Add(new DashboardWeeklyPointDto
                    {
                        Day = MonthLabels[i],
                        DayOfWeek = i + 1,
                        Count = 0,
                        Date = yearStart.AddMonths(i)
                    });
                }
            }
            else if (mode == "month")
            {
                var now = DateTime.UtcNow;
                var monthStart = new DateOnly(now.Year, now.Month, 1);
                var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                for (var i = 0; i < daysInMonth; i++)
                {
                    var d = monthStart.AddDays(i);
                    points.Add(new DashboardWeeklyPointDto
                    {
                        Day = d.Day.ToString("00"),
                        DayOfWeek = d.Day,
                        Count = 0,
                        Date = d
                    });
                }
            }
            else
            {
                var monday = DateOnly.FromDateTime(DateTime.UtcNow);
                var diff = ((int)monday.DayOfWeek + 6) % 7;
                monday = monday.AddDays(-diff);
                for (var i = 0; i < 7; i++)
                {
                    points.Add(new DashboardWeeklyPointDto
                    {
                        Day = WeekDayLabels[i],
                        DayOfWeek = i + 1,
                        Count = 0,
                        Date = monday.AddDays(i)
                    });
                }
            }
        }

        return (points, totals);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;
        return value[..(max - 1)] + "…";
    }

    private static string SanitizeTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return "UTC";

        var tz = timezone.Trim();
        if (tz.Length > 64)
            return "UTC";

        foreach (var ch in tz)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '/' or '_' or '-' or '+' or ' '))
                return "UTC";
        }

        return tz.Replace(' ', '_');
    }

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt when dt.Kind == DateTimeKind.Unspecified =>
                new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            DateTime dt => new DateTimeOffset(dt),
            _ => DateTimeOffset.UtcNow
        };
    }
}
