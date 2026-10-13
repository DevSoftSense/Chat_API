using Chat.Domain.DTOs.Permissions;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Permissions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Chat.Infrastructure.Repositories.Classes.Permissions;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<PermissionRepository> _logger;

    public PermissionRepository(
        DatabaseHelper databaseHelper,
        ILogger<PermissionRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PermissionRoleDto>> GetRolesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        // Prefer DB function when deployed; fall back to inline SQL.
        const string sqlFn =
            @"SELECT role_id, role_code
              FROM public.fn_chat_role_permission_roles(@org_id, @app_id);";

        const string sqlInline =
            @"SELECT DISTINCT role_id, COALESCE(role_code, '') AS role_code
              FROM public.tab_role_permission
              WHERE org_id = @org_id
                AND app_id = @app_id
              ORDER BY role_id;";

        try
        {
            return await QueryRolesAsync(sqlFn, orgId, appId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "42883")
        {
            // 42883 = undefined_function — functions not created yet
            _logger.LogWarning(
                "fn_chat_role_permission_roles missing; using inline SQL. Deploy Database/Permissions/*.sql");
            return await QueryRolesAsync(sqlInline, orgId, appId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<PermissionRoleDto>> QueryRolesAsync(
        string sql,
        int orgId,
        int appId,
        CancellationToken cancellationToken)
    {
        var rows = await _databaseHelper.ExecuteRawQueryAsync(
            sql,
            cmd =>
            {
                cmd.Parameters.AddWithValue("org_id", orgId);
                cmd.Parameters.AddWithValue("app_id", appId);
            },
            reader =>
            {
                var roleCode = reader.IsDBNull(1) ? "" : reader.GetString(1);
                return new PermissionRoleDto
                {
                    RoleId = reader.GetInt32(0),
                    RoleCode = roleCode,
                    RoleName = PermissionRoleNames.Friendly(roleCode)
                };
            },
            cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<PermissionTabDto>> GetByRoleAsync(
        int orgId,
        int appId,
        int? roleId,
        string? roleCode,
        CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(roleCode) ? null : roleCode.Trim();
        var useRoleId = roleId is > 0;

        try
        {
            return await QueryMatrixViaFunctionAsync(orgId, appId, useRoleId, roleId, code, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "42883")
        {
            _logger.LogWarning(
                "fn_chat_role_permission_get missing; using inline SQL. Deploy Database/Permissions/*.sql");
            return await QueryMatrixInlineAsync(orgId, appId, useRoleId, roleId, code, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<PermissionTabDto>> QueryMatrixViaFunctionAsync(
        int orgId,
        int appId,
        bool useRoleId,
        int? roleId,
        string? code,
        CancellationToken cancellationToken)
    {
        const string sql =
            @"SELECT
                  permission_id,
                  menu_id,
                  role_id,
                  role_code,
                  can_view,
                  can_add,
                  can_edit,
                  can_delete,
                  is_active,
                  menu_code,
                  menu_name,
                  menu_url,
                  icon_name,
                  parent_menu_id,
                  sort_order
              FROM public.fn_chat_role_permission_get(
                  @org_id,
                  @app_id,
                  @role_id,
                  @role_code
              );";

        return await _databaseHelper.ExecuteRawQueryAsync(
            sql,
            cmd =>
            {
                cmd.Parameters.AddWithValue("org_id", orgId);
                cmd.Parameters.AddWithValue("app_id", appId);
                cmd.Parameters.Add(new NpgsqlParameter("role_id", NpgsqlDbType.Integer)
                {
                    Value = useRoleId ? roleId!.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("role_code", NpgsqlDbType.Varchar)
                {
                    Value = useRoleId ? DBNull.Value : (object)(code ?? string.Empty)
                });
            },
            MapPermissionRow,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PermissionTabDto>> QueryMatrixInlineAsync(
        int orgId,
        int appId,
        bool useRoleId,
        int? roleId,
        string? code,
        CancellationToken cancellationToken)
    {
        var roleJoinFilter = useRoleId
            ? "AND p.role_id = @role_id"
            : "AND UPPER(TRIM(p.role_code)) = UPPER(TRIM(@role_code))";

        var sql =
            $@"SELECT
                  COALESCE(p.permission_id, 0) AS permission_id,
                  m.menu_id,
                  COALESCE(p.role_id, @resolved_role_id) AS role_id,
                  COALESCE(NULLIF(TRIM(p.role_code), ''), @resolved_role_code) AS role_code,
                  COALESCE(p.can_view, false),
                  COALESCE(p.can_add, false),
                  COALESCE(p.can_edit, false),
                  COALESCE(p.can_delete, false),
                  COALESCE(p.is_active, false),
                  m.menu_code,
                  m.menu_name,
                  m.menu_url,
                  m.icon_name,
                  m.parent_menu_id,
                  m.sort_order
              FROM public.tab_menu_master m
              LEFT JOIN public.tab_role_permission p
                     ON p.tab_id = m.menu_id
                    AND p.org_id = @org_id
                    AND p.app_id = @app_id
                    {roleJoinFilter}
              WHERE m.app_id = @app_id
                AND COALESCE(m.is_deleted, false) = false
                AND COALESCE(m.is_active, true) = true
              ORDER BY m.sort_order, m.menu_id;";

        var resolvedRoleId = useRoleId ? roleId!.Value : 0;
        var resolvedRoleCode = code ?? "";

        return await _databaseHelper.ExecuteRawQueryAsync(
            sql,
            cmd =>
            {
                cmd.Parameters.AddWithValue("org_id", orgId);
                cmd.Parameters.AddWithValue("app_id", appId);
                cmd.Parameters.AddWithValue("resolved_role_id", resolvedRoleId);
                cmd.Parameters.AddWithValue("resolved_role_code", resolvedRoleCode);

                if (useRoleId)
                    cmd.Parameters.AddWithValue("role_id", roleId!.Value);
                else
                    cmd.Parameters.Add(new NpgsqlParameter("role_code", NpgsqlDbType.Varchar)
                    {
                        Value = code ?? string.Empty
                    });
            },
            MapPermissionRow,
            cancellationToken).ConfigureAwait(false);
    }

    private static PermissionTabDto MapPermissionRow(NpgsqlDataReader reader)
    {
        var menuCode = reader.IsDBNull(9) ? "" : reader.GetString(9);
        int? parentId = reader.IsDBNull(13) ? null : reader.GetInt32(13);
        return new PermissionTabDto
        {
            PermissionId = reader.GetInt32(0),
            TabId = reader.GetInt32(1),
            RoleId = reader.GetInt32(2),
            RoleCode = reader.IsDBNull(3) ? "" : reader.GetString(3),
            CanView = reader.GetBoolean(4),
            CanAdd = reader.GetBoolean(5),
            CanEdit = reader.GetBoolean(6),
            CanDelete = reader.GetBoolean(7),
            IsActive = reader.GetBoolean(8),
            TabCode = menuCode,
            TabName = reader.IsDBNull(10) ? $"Menu {reader.GetInt32(1)}" : reader.GetString(10),
            RoutePath = reader.IsDBNull(11) ? null : reader.GetString(11),
            IconClass = reader.IsDBNull(12) ? null : reader.GetString(12),
            ParentTabId = parentId,
            SortOrder = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
            SectionCode = PermissionRoleNames.ResolveSectionCode(menuCode, parentId)
        };
    }

    public async Task SaveMatrixAsync(
        int orgId,
        int appId,
        int roleId,
        string roleCode,
        IReadOnlyList<PermissionMatrixSaveItem> rows,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                await using var update = new NpgsqlCommand(
                    @"UPDATE public.tab_role_permission
                      SET can_add = @can_add,
                          can_delete = @can_delete,
                          can_edit = @can_edit,
                          can_view = @can_view,
                          is_active = @is_active,
                          role_code = @role_code
                      WHERE org_id = @org_id
                        AND app_id = @app_id
                        AND role_id = @role_id
                        AND tab_id = @tab_id;",
                    connection, tx)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };

                update.Parameters.AddWithValue("can_add", row.CanAdd);
                update.Parameters.AddWithValue("can_delete", row.CanDelete);
                update.Parameters.AddWithValue("can_edit", row.CanEdit);
                update.Parameters.AddWithValue("can_view", row.CanView);
                update.Parameters.AddWithValue("is_active", row.IsActive);
                update.Parameters.AddWithValue("role_code", roleCode);
                update.Parameters.AddWithValue("org_id", orgId);
                update.Parameters.AddWithValue("app_id", appId);
                update.Parameters.AddWithValue("role_id", roleId);
                update.Parameters.AddWithValue("tab_id", row.TabId);

                var updated = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (updated > 0)
                    continue;

                await using var insert = new NpgsqlCommand(
                    @"INSERT INTO public.tab_role_permission
                        (can_add, can_delete, can_edit, can_view, org_id, is_active,
                         role_id, tab_id, fiscal_year_id, app_id, role_code)
                      VALUES
                        (@can_add, @can_delete, @can_edit, @can_view, @org_id, @is_active,
                         @role_id, @tab_id, NULL, @app_id, @role_code);",
                    connection, tx)
                {
                    CommandTimeout = _databaseHelper.CommandTimeoutSeconds
                };

                insert.Parameters.AddWithValue("can_add", row.CanAdd);
                insert.Parameters.AddWithValue("can_delete", row.CanDelete);
                insert.Parameters.AddWithValue("can_edit", row.CanEdit);
                insert.Parameters.AddWithValue("can_view", row.CanView);
                insert.Parameters.AddWithValue("is_active", row.IsActive);
                insert.Parameters.AddWithValue("org_id", orgId);
                insert.Parameters.AddWithValue("app_id", appId);
                insert.Parameters.AddWithValue("role_id", roleId);
                insert.Parameters.AddWithValue("tab_id", row.TabId);
                insert.Parameters.AddWithValue("role_code", roleCode);

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed saving permission matrix OrgId={OrgId} AppId={AppId} RoleId={RoleId}",
                orgId, appId, roleId);
            throw;
        }
    }
}
