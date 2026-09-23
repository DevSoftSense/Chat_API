namespace Chat.Domain.DTOs.Messages;

public sealed class ChatNotificationDto
{
    public long NotificationId { get; set; }
    public long UserId { get; set; }
    public long? SenderUserId { get; set; }
    public string? NotificationType { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
    public long? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceEntity { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedDate { get; set; }
    public long OrgId { get; set; }
    public int AppId { get; set; }
    public int? FiscalYearId { get; set; }
}

public sealed class ChatNotificationListResult
{
    public IReadOnlyList<ChatNotificationDto> Notifications { get; set; } = Array.Empty<ChatNotificationDto>();
    public int UnreadCount { get; set; }
}

public sealed class PeerUnreadCountDto
{
    public long PeerUserId { get; set; }
    public int UnreadCount { get; set; }
}

public sealed class PeerUnreadCountListResult
{
    public IReadOnlyList<PeerUnreadCountDto> Items { get; set; } = Array.Empty<PeerUnreadCountDto>();
    public int TotalUnread { get; set; }
}
