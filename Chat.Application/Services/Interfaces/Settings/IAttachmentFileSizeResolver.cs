namespace Chat.Application.Services.Interfaces.Settings;

/// <summary>
/// Resolves on-disk file sizes for chat attachment relative paths (no new DB table).
/// </summary>
public interface IAttachmentFileSizeResolver
{
    long SumExistingFileBytes(IEnumerable<string?> relativePaths);
}
