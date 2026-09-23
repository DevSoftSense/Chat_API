namespace Chat.Domain.DTOs.Groups;

public sealed class CreateGroupRequest
{
    public string GroupName { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>SoftOnCloud userIds selected from the Product Users API (creator is added from JWT).</summary>
    public IReadOnlyList<long>? MemberUserIds { get; set; }

    public int? OrgId { get; set; }
    public int? AppId { get; set; }
    public int? FiscalYearId { get; set; }
}
