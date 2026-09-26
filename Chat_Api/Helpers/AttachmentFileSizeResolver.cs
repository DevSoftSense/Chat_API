using Chat.Application.Services.Interfaces.Settings;
using Chat_Api.Helpers;

namespace Chat_Api.Helpers;

/// <summary>
/// Sums on-disk sizes for chat attachment paths via existing ChatAttachmentStorage.
/// </summary>
public sealed class AttachmentFileSizeResolver : IAttachmentFileSizeResolver
{
    private readonly ChatAttachmentStorage _storage;

    public AttachmentFileSizeResolver(ChatAttachmentStorage storage)
    {
        _storage = storage;
    }

    public long SumExistingFileBytes(IEnumerable<string?> relativePaths)
    {
        long total = 0;
        foreach (var path in relativePaths)
        {
            if (!_storage.TryResolvePhysicalPath(path, out var physicalPath, out _))
                continue;
            try
            {
                var info = new FileInfo(physicalPath);
                if (info.Exists)
                    total += info.Length;
            }
            catch
            {
                // Skip unreadable files; used storage should not fail the whole Settings page.
            }
        }

        return total;
    }
}
