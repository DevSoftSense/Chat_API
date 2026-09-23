namespace Chat.Domain.DTOs.Messages;

public sealed class DeleteMessageResult
{
    public long MessageId { get; set; }
    public int ChatId { get; set; }
    public long SenderUserId { get; set; }
    public long? ReceiverUserId { get; set; }
    public long DeletedBy { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public short DeleteFlag { get; set; }
}
