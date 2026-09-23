using System.Text.Json.Serialization;

namespace Chat.Domain.DTOs.Groups;

public sealed class UpdateGroupRequest
{
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Optional group avatar path/URL for tab_groups.profile_pic.
    /// Null = leave unchanged; empty string = clear; otherwise set.
    /// </summary>
    [JsonPropertyName("profilePic")]
    public string? ProfilePic { get; set; }

    public int? OrgId { get; set; }
    public int? AppId { get; set; }
    public int? FiscalYearId { get; set; }
}
