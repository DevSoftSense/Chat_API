namespace Chat.Domain.DTOs.Messages;

public sealed class ToggleMessageReactionRequest
{
    /// <summary>
    /// Allowed: LIKE, LOVE, LAUGH, WOW, SAD, THANKS. Null/empty clears the reaction.
    /// </summary>
    public string? ReactionCode { get; set; }
}

public sealed class ToggleMessageReactionResult
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long SenderUserId { get; set; }
    public long? ReceiverUserId { get; set; }
    public long? ReactionId { get; set; }
    public string? ReactionCode { get; set; }
    public string? Reaction { get; set; }
    public DateTimeOffset? ReactedOn { get; set; }
}
