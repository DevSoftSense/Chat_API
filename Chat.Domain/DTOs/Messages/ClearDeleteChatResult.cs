namespace Chat.Domain.DTOs.Messages;

public sealed class ClearChatResult
{
    public int ChatId { get; set; }
    public int AffectedCount { get; set; }
    public long? PeerUserId { get; set; }
    public long ClearedBy { get; set; }
    public DateTimeOffset ClearedAt { get; set; }
}

public sealed class DeleteChatResult
{
    public int ChatId { get; set; }
    public int AffectedCount { get; set; }
    public long? PeerUserId { get; set; }
    public long DeletedBy { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
}
