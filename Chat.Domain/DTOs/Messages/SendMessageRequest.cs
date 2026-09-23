namespace Chat.Domain.DTOs.Messages;

public sealed class SendMessageRequest
{
    public long SenderUserId { get; set; }

    /// <summary>Required for One-to-One. Leave null/0 when sending to a Group (GroupId or group chat_id).</summary>
    public long ReceiverUserId { get; set; }

    public string MessageBody { get; set; } = string.Empty;
    public short MessageTypeId { get; set; } = 1;

    /// <summary>Web-relative path from the upload API (e.g. /chat-attachments/...).</summary>
    public string? AttachmentPath1 { get; set; }
    public string? AttachmentPath2 { get; set; }
    public string? AttachmentPath3 { get; set; }
    public string? AttachmentPath4 { get; set; }
    public string? AttachmentPath5 { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? OrgId { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? AppId { get; set; }

    /// <summary>Optional when provided in the JSON body; query string takes precedence.</summary>
    public int? FiscalYearId { get; set; }

    /// <summary>Optional reply target — existing tab_messages.message_id in the same chat.</summary>
    public long? ParentMessageId { get; set; }

    /// <summary>
    /// Optional Group Chat target. When set, One-to-One receiver rules are skipped
    /// and a single tab_messages row is stored with group_id (receiver_user_id NULL).
    /// </summary>
    public long? GroupId { get; set; }
}
