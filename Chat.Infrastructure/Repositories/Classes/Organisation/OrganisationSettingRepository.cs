using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Interfaces.Organisation;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Chat.Infrastructure.Repositories.Classes.Organisation;

/// <summary>
/// Reads organisation settings from existing public.tab_organisation_setting (no new table).
/// </summary>
public sealed class OrganisationSettingRepository : IOrganisationSettingRepository
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly ILogger<OrganisationSettingRepository> _logger;

    public OrganisationSettingRepository(
        DatabaseHelper databaseHelper,
        ILogger<OrganisationSettingRepository> logger)
    {
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    public async Task<string?> GetSettingValueAsync(
        int orgId,
        int appId,
        string settingKey,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            @"SELECT setting_value
              FROM public.tab_organisation_setting
              WHERE org_id = @org_id
                AND app_id = @app_id
                AND setting_key = @setting_key
              LIMIT 1;";

        try
        {
            await using var connection = await _databaseHelper
                .GetDefaultConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = _databaseHelper.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("org_id", orgId);
            command.Parameters.AddWithValue("app_id", appId);
            command.Parameters.AddWithValue("setting_key", settingKey);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null or DBNull)
                return null;

            var value = Convert.ToString(result);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed reading organisation setting {SettingKey} for OrgId={OrgId} AppId={AppId}",
                settingKey,
                orgId,
                appId);
            return null;
        }
    }
}
