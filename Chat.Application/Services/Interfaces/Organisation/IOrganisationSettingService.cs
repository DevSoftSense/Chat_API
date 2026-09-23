namespace Chat.Application.Services.Interfaces.Organisation;

public interface IOrganisationSettingService
{
    /// <summary>
    /// ChatMaxFileSizeMB from tab_organisation_setting, or 60 when missing/invalid.
    /// </summary>
    Task<int> GetChatMaxFileSizeMBAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default);
}
