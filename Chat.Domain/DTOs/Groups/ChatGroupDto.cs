namespace Chat.Domain.DTOs.Groups;

public sealed class ChatGroupDto
{
    public long GroupId { get; set; }
    public int? ChatId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ProfilePic { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; }
    /// <summary>Unread inbound group messages for the current user (receipt-based).</summary>
    public int UnreadCount { get; set; }
}

public sealed class ChatGroupMemberDto
{
    public long UserId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ChatGroupDetailsDto
{
    public long GroupId { get; set; }
    public int? ChatId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ProfilePic { get; set; }
    public long CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? CreatedAt { get; set; }
    public IReadOnlyList<ChatGroupMemberDto> Members { get; set; } = Array.Empty<ChatGroupMemberDto>();
}

public sealed class GroupMemberChangeResult
{
    public long GroupId { get; set; }
    public long UserId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public bool IsActive { get; set; }
    public bool Reactivated { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}
