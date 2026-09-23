namespace Chat.Domain.DTOs.Groups;

/// <summary>WhatsApp-style Message Info for a group message.</summary>
public sealed class MessageInfoResult
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long? GroupId { get; set; }
    public long SenderUserId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public IReadOnlyList<MessageInfoMemberDto> Members { get; set; } = Array.Empty<MessageInfoMemberDto>();
}

public sealed class MessageInfoMemberDto
{
    public long UserId { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
