namespace Chat.Domain.DTOs.Messages;

public sealed class PrivateChatDto
{
    public int ChatId { get; set; }
    public long User1Id { get; set; }
    public long User2Id { get; set; }
}
