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

    /// <summary>
    /// Required SoftOnCloud userType(s) for All User Communication
    /// (setting_key=AllUserCommunicationUserType). Default PRIME when missing.
    /// Change DB value to ADMIN for admin-only mode — no .env change.
    /// </summary>
    Task<IReadOnlyList<string>> GetCommFromUserTypesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default);
}
