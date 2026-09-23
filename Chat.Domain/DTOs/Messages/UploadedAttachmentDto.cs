namespace Chat.Domain.DTOs.Messages;

public sealed class UploadedAttachmentDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Web-relative path suitable for tab_messages.attachment_path_*.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Absolute URL when a request base address is available.</summary>
    public string? FileUrl { get; set; }
}
