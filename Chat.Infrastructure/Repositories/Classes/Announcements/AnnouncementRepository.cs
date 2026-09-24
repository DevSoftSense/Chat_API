using Chat.Domain.DTOs.Announcements;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Announcements;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Announcements;

public sealed class AnnouncementRepository : IAnnouncementRepository
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<AnnouncementRepository> _logger;

    public AnnouncementRepository(
        DatabaseHelper databaseHelper,
        ILogger<AnnouncementRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<AnnouncementListResult> GetAnnouncementsAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        AnnouncementListQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new AnnouncementListQuery();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, 100);

        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT g.announcement_id, g.title, g.description, g.category_id, g.category_name,
                         g.department_id, g.department_name, g.location, g.is_important, g.is_pinned,
                         g.status_id, g.status_name, g.posted_by, g.posted_by_name, g.posted_date,
                         g.expiry_date, g.read_count, g.recipient_count, g.is_read_by_current_user,
                         COUNT(*) OVER() AS total_count
                  FROM public.fn_chat_announcement_get(
                      @p_user_id, @p_org_id, @p_app_id, @p_fiscal_year_id,
                      1, 1000, @p_search, @p_category_id, @p_department_id,
                      @p_location, @p_important, @p_pinned, @p_status_id, @p_filter, @p_sort_by) g
                  INNER JOIN public.tab_announcements a
                      ON a.announcement_id = g.announcement_id
                     AND COALESCE(a.delete_flag, 0) = 0
                  ORDER BY g.is_pinned DESC, g.posted_date DESC
                  LIMIT @p_page_size OFFSET @p_offset;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            AddCommonTenant(command, userId, orgId, appId);
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId is > 0 ? fiscalYearId.Value : DBNull.Value;
            command.Parameters.Add("p_page_size", NpgsqlDbType.Integer).Value = pageSize;
            command.Parameters.Add("p_offset", NpgsqlDbType.Integer).Value = (page - 1) * pageSize;
            command.Parameters.Add("p_search", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : query.Search.Trim();
            command.Parameters.Add("p_category_id", NpgsqlDbType.Integer).Value =
                query.CategoryId is > 0 ? query.CategoryId.Value : DBNull.Value;
            command.Parameters.Add("p_department_id", NpgsqlDbType.Integer).Value =
                query.DepartmentId is > 0 ? query.DepartmentId.Value : DBNull.Value;
            command.Parameters.Add("p_location", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(query.Location) ? DBNull.Value : query.Location.Trim();
            command.Parameters.Add("p_important", NpgsqlDbType.Boolean).Value =
                query.Important.HasValue ? query.Important.Value : DBNull.Value;
            command.Parameters.Add("p_pinned", NpgsqlDbType.Boolean).Value =
                query.Pinned.HasValue ? query.Pinned.Value : DBNull.Value;
            command.Parameters.Add("p_status_id", NpgsqlDbType.Integer).Value =
                query.StatusId is > 0 ? query.StatusId.Value : DBNull.Value;
            command.Parameters.Add("p_filter", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(query.Filter) ? "all" : query.Filter.Trim();
            command.Parameters.Add("p_sort_by", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(query.SortBy) ? "newest" : query.SortBy.Trim();

            var items = new List<AnnouncementDto>();
            long total = 0;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapAnnouncement(reader));
                total = reader.IsDBNull(reader.GetOrdinal("total_count"))
                    ? total
                    : reader.GetInt64(reader.GetOrdinal("total_count"));
            }

            return new AnnouncementListResult
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetAnnouncementsAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementDto> GetByIdAsync(
        long announcementId,
        long userId,
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT g.announcement_id, g.title, g.description, g.category_id, g.category_name,
                         g.department_id, g.department_name, g.location, g.is_important, g.is_pinned,
                         g.status_id, g.status_name, g.posted_by, g.posted_by_name, g.posted_date,
                         g.expiry_date, g.read_count, g.recipient_count, g.is_read_by_current_user,
                         g.recipient_user_ids
                  FROM public.fn_chat_announcement_get_by_id(
                      @p_announcement_id, @p_user_id, @p_org_id, @p_app_id) g
                  INNER JOIN public.tab_announcements a
                      ON a.announcement_id = g.announcement_id
                     AND COALESCE(a.delete_flag, 0) = 0;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
            AddCommonTenant(command, userId, orgId, appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Announcement not found.", 404);

            return MapAnnouncement(reader);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetByIdAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementDto> CreateAsync(
        long userId,
        string? postedByName,
        CreateAnnouncementRequest request,
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
                @"SELECT announcement_id, title, description, category_id, category_name,
                         department_id, department_name, location, is_important, is_pinned,
                         status_id, status_name, posted_by, posted_by_name, posted_date,
                         expiry_date, read_count, recipient_count, is_read_by_current_user,
                         recipient_user_ids
                  FROM public.fn_chat_announcement_create(
                      @p_title, @p_description, @p_category_id, @p_department_id, @p_department_name,
                      @p_location, @p_is_important, @p_status_id, @p_expiry_date, @p_posted_by,
                      @p_posted_by_name, @p_org_id, @p_app_id, @p_fiscal_year_id, @p_recipient_user_ids);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_title", NpgsqlDbType.Varchar).Value = request.Title.Trim();
            command.Parameters.Add("p_description", NpgsqlDbType.Text).Value = request.Description.Trim();
            command.Parameters.Add("p_category_id", NpgsqlDbType.Integer).Value =
                request.CategoryId is > 0 ? request.CategoryId.Value : DBNull.Value;
            command.Parameters.Add("p_department_id", NpgsqlDbType.Integer).Value =
                request.DepartmentId is > 0 ? request.DepartmentId.Value : DBNull.Value;
            command.Parameters.Add("p_department_name", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(request.DepartmentName) ? DBNull.Value : request.DepartmentName.Trim();
            command.Parameters.Add("p_location", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(request.Location) ? DBNull.Value : request.Location.Trim();
            command.Parameters.Add("p_is_important", NpgsqlDbType.Boolean).Value = request.IsImportant;
            command.Parameters.Add("p_status_id", NpgsqlDbType.Integer).Value =
                request.StatusId is > 0 ? request.StatusId.Value : DBNull.Value;
            command.Parameters.Add("p_expiry_date", NpgsqlDbType.TimestampTz).Value =
                request.ExpiryDate.HasValue ? request.ExpiryDate.Value : DBNull.Value;
            command.Parameters.Add("p_posted_by", NpgsqlDbType.Bigint).Value = userId;
            command.Parameters.Add("p_posted_by_name", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(postedByName)
                    ? (string.IsNullOrWhiteSpace(request.PostedByName) ? DBNull.Value : request.PostedByName.Trim())
                    : postedByName.Trim();
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_fiscal_year_id", NpgsqlDbType.Integer).Value =
                fiscalYearId is > 0 ? fiscalYearId.Value : DBNull.Value;
            command.Parameters.Add("p_recipient_user_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value =
                (request.RecipientUserIds ?? Array.Empty<long>())
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Announcement was not created.", 500);

            return MapAnnouncement(reader);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in CreateAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementDto> UpdateAsync(
        long announcementId,
        long userId,
        UpdateAnnouncementRequest request,
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT announcement_id, title, description, category_id, category_name,
                         department_id, department_name, location, is_important, is_pinned,
                         status_id, status_name, posted_by, posted_by_name, posted_date,
                         expiry_date, read_count, recipient_count, is_read_by_current_user,
                         recipient_user_ids
                  FROM public.fn_chat_announcement_update(
                      @p_announcement_id, @p_user_id, @p_org_id, @p_app_id,
                      @p_title, @p_description, @p_category_id, @p_department_id, @p_department_name,
                      @p_location, @p_is_important, @p_status_id, @p_expiry_date, @p_recipient_user_ids);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
            AddCommonTenant(command, userId, orgId, appId);
            command.Parameters.Add("p_title", NpgsqlDbType.Varchar).Value = request.Title.Trim();
            command.Parameters.Add("p_description", NpgsqlDbType.Text).Value = request.Description.Trim();
            command.Parameters.Add("p_category_id", NpgsqlDbType.Integer).Value =
                request.CategoryId is > 0 ? request.CategoryId.Value : DBNull.Value;
            command.Parameters.Add("p_department_id", NpgsqlDbType.Integer).Value =
                request.DepartmentId is > 0 ? request.DepartmentId.Value : DBNull.Value;
            command.Parameters.Add("p_department_name", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(request.DepartmentName) ? DBNull.Value : request.DepartmentName.Trim();
            command.Parameters.Add("p_location", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(request.Location) ? DBNull.Value : request.Location.Trim();
            command.Parameters.Add("p_is_important", NpgsqlDbType.Boolean).Value =
                request.IsImportant.HasValue ? request.IsImportant.Value : DBNull.Value;
            command.Parameters.Add("p_status_id", NpgsqlDbType.Integer).Value =
                request.StatusId is > 0 ? request.StatusId.Value : DBNull.Value;
            command.Parameters.Add("p_expiry_date", NpgsqlDbType.TimestampTz).Value =
                request.ExpiryDate.HasValue ? request.ExpiryDate.Value : DBNull.Value;

            if (request.RecipientUserIds is null)
                command.Parameters.Add("p_recipient_user_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = DBNull.Value;
            else
                command.Parameters.Add("p_recipient_user_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value =
                    request.RecipientUserIds.Where(id => id > 0).Distinct().ToArray();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Announcement not found.", 404);

            return MapAnnouncement(reader);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in UpdateAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementDeleteResult> DeleteAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            // Soft-delete ONLY — never DELETE the row.
            // Sets delete_flag = 1 and delete_at = now() on tab_announcements.
            await using var command = new NpgsqlCommand(
                @"UPDATE public.tab_announcements AS a
                  SET delete_flag = 1::SMALLINT,
                      delete_at   = CURRENT_TIMESTAMP,
                      updated_at  = CURRENT_TIMESTAMP
                  WHERE a.announcement_id = @p_announcement_id
                    AND a.org_id = @p_org_id
                    AND a.app_id = @p_app_id
                    AND a.posted_by = @p_user_id
                    AND COALESCE(a.delete_flag, 0) = 0
                  RETURNING a.announcement_id;",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = userId;
            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;

            var deletedIdObj = await command.ExecuteScalarAsync(cancellationToken);
            if (deletedIdObj is null || deletedIdObj is DBNull)
            {
                await using var checkCmd = new NpgsqlCommand(
                    @"SELECT a.posted_by, COALESCE(a.delete_flag, 0)
                      FROM public.tab_announcements a
                      WHERE a.announcement_id = @p_announcement_id
                        AND a.org_id = @p_org_id
                        AND a.app_id = @p_app_id
                      LIMIT 1;",
                    connection)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };
                checkCmd.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
                checkCmd.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
                checkCmd.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;

                await using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new ChatOperationException("Announcement not found.", 404);

                var postedBy = reader.GetInt64(0);
                var deleteFlag = Convert.ToInt32(reader.GetValue(1));
                if (deleteFlag != 0)
                    throw new ChatOperationException("Announcement already deleted.", 404);
                if (postedBy != userId)
                    throw new ChatOperationException("Only the poster can delete this announcement.", 403);

                throw new ChatOperationException("Announcement not found.", 404);
            }

            return new AnnouncementDeleteResult
            {
                AnnouncementId = Convert.ToInt64(deletedIdObj),
                Deleted = true
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in DeleteAsync (soft-delete)");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementMarkReadResult> MarkReadAsync(
        long announcementId, long userId, int orgId, int appId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT announcement_id, is_read, read_at
                  FROM public.fn_chat_announcement_mark_read(
                      @p_announcement_id, @p_user_id, @p_org_id, @p_app_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
            AddCommonTenant(command, userId, orgId, appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Announcement not found.", 404);

            return new AnnouncementMarkReadResult
            {
                AnnouncementId = reader.GetInt64(0),
                IsRead = reader.GetBoolean(1),
                ReadAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in MarkReadAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementPinResult> PinAsync(
        long announcementId, long userId, int orgId, int appId, bool pinned,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            var fn = pinned ? "fn_chat_announcement_pin" : "fn_chat_announcement_unpin";
            await using var command = new NpgsqlCommand(
                pinned
                    ? @"SELECT result_announcement_id AS announcement_id,
                               result_is_pinned AS is_pinned
                        FROM public.fn_chat_announcement_pin(
                            @p_announcement_id, @p_user_id, @p_org_id, @p_app_id, TRUE);"
                    : @"SELECT result_announcement_id AS announcement_id,
                               result_is_pinned AS is_pinned
                        FROM public.fn_chat_announcement_unpin(
                            @p_announcement_id, @p_user_id, @p_org_id, @p_app_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_announcement_id", NpgsqlDbType.Bigint).Value = announcementId;
            AddCommonTenant(command, userId, orgId, appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ChatOperationException("Announcement not found.", 404);

            return new AnnouncementPinResult
            {
                AnnouncementId = reader.GetInt64(0),
                IsPinned = reader.GetBoolean(1)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in PinAsync");
            throw MapPg(ex);
        }
    }

    public async Task<IReadOnlyList<AnnouncementCategoryDto>> GetCategoriesAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT category_id, category_name, is_active, announcement_count
                  FROM public.fn_chat_announcement_categories_get(@p_org_id, @p_app_id, @p_user_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
            command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
            command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = userId;

            var list = new List<AnnouncementCategoryDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new AnnouncementCategoryDto
                {
                    CategoryId = Convert.ToInt32(reader.GetValue(0)),
                    CategoryName = reader.GetString(1),
                    IsActive = reader.GetBoolean(2),
                    AnnouncementCount = Convert.ToInt32(reader.GetValue(3))
                });
            }

            return list;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetCategoriesAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementStatsDto> GetStatsAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT total_announcements, total_reads, unread_by_me
                  FROM public.fn_chat_announcement_get_stats(@p_user_id, @p_org_id, @p_app_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            AddCommonTenant(command, userId, orgId, appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new AnnouncementStatsDto();

            return new AnnouncementStatsDto
            {
                TotalAnnouncements = reader.GetInt32(0),
                TotalReads = reader.GetInt32(1),
                UnreadByMe = reader.GetInt32(2)
            };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetStatsAsync");
            throw MapPg(ex);
        }
    }

    public async Task<AnnouncementUnreadCountDto> GetUnreadCountAsync(
        long userId, int orgId, int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                @"SELECT unread_count
                  FROM public.fn_chat_announcement_unread_count(@p_user_id, @p_org_id, @p_app_id);",
                connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            AddCommonTenant(command, userId, orgId, appId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new AnnouncementUnreadCountDto { UnreadCount = 0 };

            return new AnnouncementUnreadCountDto { UnreadCount = reader.GetInt32(0) };
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error in GetUnreadCountAsync");
            throw MapPg(ex);
        }
    }

    private static void AddCommonTenant(NpgsqlCommand command, long userId, int orgId, int appId)
    {
        command.Parameters.Add("p_user_id", NpgsqlDbType.Bigint).Value = userId;
        command.Parameters.Add("p_org_id", NpgsqlDbType.Integer).Value = orgId;
        command.Parameters.Add("p_app_id", NpgsqlDbType.Integer).Value = appId;
    }

    private static AnnouncementDto MapAnnouncement(NpgsqlDataReader reader)
    {
        DateTimeOffset ReadDto(string name)
        {
            var ord = reader.GetOrdinal(name);
            if (reader.IsDBNull(ord)) return default;
            var value = reader.GetValue(ord);
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => default
            };
        }

        DateTimeOffset? ReadDtoNullable(string name)
        {
            var ord = reader.GetOrdinal(name);
            if (reader.IsDBNull(ord)) return null;
            var value = reader.GetValue(ord);
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => null
            };
        }

        string? ReadString(string name)
        {
            var ord = reader.GetOrdinal(name);
            return reader.IsDBNull(ord) ? null : reader.GetString(ord);
        }

        int? ReadIntNullable(string name)
        {
            var ord = reader.GetOrdinal(name);
            if (reader.IsDBNull(ord)) return null;
            return Convert.ToInt32(reader.GetValue(ord));
        }

        int ReadInt(string name)
        {
            var ord = reader.GetOrdinal(name);
            if (reader.IsDBNull(ord)) return 0;
            return Convert.ToInt32(reader.GetValue(ord));
        }

        return new AnnouncementDto
        {
            AnnouncementId = reader.GetInt64(reader.GetOrdinal("announcement_id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            CategoryId = ReadIntNullable("category_id"),
            CategoryName = ReadString("category_name"),
            DepartmentId = ReadIntNullable("department_id"),
            DepartmentName = ReadString("department_name"),
            Location = ReadString("location"),
            IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
            IsPinned = reader.GetBoolean(reader.GetOrdinal("is_pinned")),
            StatusId = ReadInt("status_id"),
            StatusName = ReadString("status_name"),
            PostedBy = reader.GetInt64(reader.GetOrdinal("posted_by")),
            PostedByName = ReadString("posted_by_name"),
            PostedDate = ReadDto("posted_date"),
            ExpiryDate = ReadDtoNullable("expiry_date"),
            ReadCount = ReadInt("read_count"),
            RecipientCount = ReadInt("recipient_count"),
            IsReadByCurrentUser = reader.GetBoolean(reader.GetOrdinal("is_read_by_current_user")),
            RecipientUserIds = ReadLongArray(reader, "recipient_user_ids")
        };
    }

    private static long[] ReadLongArray(NpgsqlDataReader reader, string name)
    {
        try
        {
            var ord = reader.GetOrdinal(name);
            if (reader.IsDBNull(ord)) return Array.Empty<long>();
            var value = reader.GetValue(ord);
            if (value is long[] longs) return longs;
            if (value is Array arr)
            {
                var list = new List<long>();
                foreach (var item in arr)
                {
                    if (item == null || item is DBNull) continue;
                    list.Add(Convert.ToInt64(item));
                }
                return list.ToArray();
            }
        }
        catch (IndexOutOfRangeException)
        {
            // Column not present on list queries
        }
        return Array.Empty<long>();
    }

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
