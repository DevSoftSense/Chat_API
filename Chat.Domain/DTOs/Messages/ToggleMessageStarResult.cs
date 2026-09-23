namespace Chat.Domain.DTOs.Messages;

public sealed class ToggleMessageStarRequest
{
    public bool IsStarred { get; set; }
}

public sealed class ToggleMessageStarResult
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long SenderUserId { get; set; }
    public long? ReceiverUserId { get; set; }
    public bool IsStarredByMe { get; set; }
    public DateTimeOffset? StarredAt { get; set; }
}
