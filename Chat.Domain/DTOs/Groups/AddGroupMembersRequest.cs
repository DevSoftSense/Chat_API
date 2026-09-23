namespace Chat.Domain.DTOs.Groups;

public sealed class AddGroupMembersRequest
{
    /// <summary>SoftOnCloud userIds to add as non-admin members.</summary>
    public IReadOnlyList<long> MemberUserIds { get; set; } = Array.Empty<long>();

    public int? OrgId { get; set; }
    public int? AppId { get; set; }
    public int? FiscalYearId { get; set; }
}
