namespace Chat.Domain.DTOs.Messages;

public sealed class ForwardMessageRequest
{
    /// <summary>Destination chat id (private or group chat_id).</summary>
    public int DestinationChatId { get; set; }

    /// <summary>Required for private forward; omit/0 when forwarding into a group chat.</summary>
    public long? ReceiverUserId { get; set; }

    /// <summary>Optional group id hint; server also resolves group from destinationChatId.</summary>
    public long? GroupId { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? OrgId { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? AppId { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? FiscalYearId { get; set; }
}
