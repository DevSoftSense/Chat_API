namespace Chat.Domain.DTOs.Messages;

public sealed class SentMessageDto
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long SenderUserId { get; set; }
    /// <summary>Null for Group messages (single row shared by all members).</summary>
    public long? ReceiverUserId { get; set; }
    public string MessageBody { get; set; } = string.Empty;
    public short MessageTypeId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? AttachmentPath1 { get; set; }
    public string? AttachmentPath2 { get; set; }
    public string? AttachmentPath3 { get; set; }
    public string? AttachmentPath4 { get; set; }
    public string? AttachmentPath5 { get; set; }
    public long? ParentMessageId { get; set; }
    public long? ForwardedFromMessageId { get; set; }
    public long? ForwardedBy { get; set; }
    /// <summary>Set for Group messages; null for One-to-One.</summary>
    public long? GroupId { get; set; }
}
