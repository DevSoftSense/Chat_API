namespace Chat.Infrastructure.Repositories.Interfaces.Organisation;

public interface IOrganisationSettingRepository
{
    /// <summary>
    /// Reads setting_value from public.tab_organisation_setting for (org_id, app_id, setting_key).
    /// Returns null when the row is missing.
    /// </summary>
    Task<string?> GetSettingValueAsync(
        int orgId,
        int appId,
        string settingKey,
        CancellationToken cancellationToken = default);
}
