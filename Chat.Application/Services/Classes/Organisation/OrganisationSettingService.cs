using Chat.Application.Services.Interfaces.Organisation;
using Chat.Infrastructure.Repositories.Interfaces.Organisation;

namespace Chat.Application.Services.Classes.Organisation;

public sealed class OrganisationSettingService : IOrganisationSettingService
{
    public const string ChatMaxFileSizeMBKey = "ChatMaxFileSizeMB";
    public const int DefaultChatMaxFileSizeMB = 60;

    /// <summary>Existing tab_organisation_setting key — no new table/column.</summary>
    public const string AllUserCommunicationUserTypeKey = "AllUserCommunicationUserType";
    public const string DefaultAllUserCommunicationUserType = "PRIME";

    private readonly IOrganisationSettingRepository _organisationSettingRepository;

    public OrganisationSettingService(IOrganisationSettingRepository organisationSettingRepository)
    {
        _organisationSettingRepository = organisationSettingRepository;
    }

    public async Task<int> GetChatMaxFileSizeMBAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        if (orgId <= 0 || appId <= 0)
            return DefaultChatMaxFileSizeMB;

        var raw = await _organisationSettingRepository
            .GetSettingValueAsync(orgId, appId, ChatMaxFileSizeMBKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
            return DefaultChatMaxFileSizeMB;

        if (!int.TryParse(raw.Trim(), out var mb) || mb <= 0)
            return DefaultChatMaxFileSizeMB;

        // Guard against absurd values that would overflow when converting to bytes.
        if (mb > 1024)
            return DefaultChatMaxFileSizeMB;

        return mb;
    }

    public async Task<IReadOnlyList<string>> GetCommFromUserTypesAsync(
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        if (orgId <= 0 || appId <= 0)
            return new[] { DefaultAllUserCommunicationUserType };

        var raw = await _organisationSettingRepository
            .GetSettingValueAsync(orgId, appId, AllUserCommunicationUserTypeKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
            return new[] { DefaultAllUserCommunicationUserType };

        var list = raw
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return list.Count > 0 ? list : new[] { DefaultAllUserCommunicationUserType };
    }
}
