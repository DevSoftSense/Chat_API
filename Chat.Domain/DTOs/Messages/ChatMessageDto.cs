namespace Chat.Domain.DTOs.Messages;

public sealed class ChatMessageDto
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long SenderUserId { get; set; }
    public long? ReceiverUserId { get; set; }
    public string MessageBody { get; set; } = string.Empty;
    public short MessageTypeId { get; set; }
    public string? TypeName { get; set; }
    public long? ParentMessageId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? AttachmentPath1 { get; set; }
    public string? AttachmentPath2 { get; set; }
    public string? AttachmentPath3 { get; set; }
    public string? AttachmentPath4 { get; set; }
    public string? AttachmentPath5 { get; set; }
    public bool IsStarredBySender { get; set; }
    public DateTimeOffset? StarredBySenderAt { get; set; }
    public bool IsStarredByReceiver { get; set; }
    public DateTimeOffset? StarredByReceiverAt { get; set; }
    /// <summary>Star state for the authenticated user only (derived from sender/receiver columns).</summary>
    public bool IsStarredByMe { get; set; }
    public DateTimeOffset? StarredAt { get; set; }
    /// <summary>Other participant in the 1-to-1 chat (for starred list navigation).</summary>
    public long? PeerUserId { get; set; }
    public long? ForwardedFromMessageId { get; set; }
    public long? ForwardedBy { get; set; }
    /// <summary>Current user's reaction code (LIKE/LOVE/...) from tab_message_reactions.</summary>
    public string? MyReactionCode { get; set; }
    /// <summary>Current user's reaction emoji from tab_message_reactions.reaction.</summary>
    public string? MyReaction { get; set; }
    /// <summary>Other participant's reaction code (1-to-1 WhatsApp-style badge).</summary>
    public string? PeerReactionCode { get; set; }
    /// <summary>Other participant's reaction emoji.</summary>
    public string? PeerReaction { get; set; }

    /// <summary>Set for Group messages; null for One-to-One.</summary>
    public long? GroupId { get; set; }
}

public sealed class MarkMessagesReadResult
{
    public int ChatId { get; set; }
    public long ReaderUserId { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public IReadOnlyList<long> MessageIds { get; set; } = Array.Empty<long>();
    public IReadOnlyList<long> SenderUserIds { get; set; } = Array.Empty<long>();
}
