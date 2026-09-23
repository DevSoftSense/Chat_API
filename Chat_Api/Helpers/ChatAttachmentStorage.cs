using Chat.Application.Services.Classes.Organisation;
using Chat.Domain.DTOs.Messages;
using Chat.Domain.Exceptions.Messages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Chat_Api.Helpers;

/// <summary>
/// Saves chat attachment files under wwwroot (default: chat-attachments).
/// Returns web-relative paths for tab_messages.attachment_path_1..5 — never stores binary in PostgreSQL.
/// </summary>
public sealed class ChatAttachmentStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".rtf", ".odt",
        // images
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg",
        // video
        ".mp4", ".webm", ".mov", ".avi", ".mkv",
        // audio
        ".mp3", ".wav", ".ogg", ".m4a", ".aac",
        // archives / other common chat attachments
        ".zip", ".rar", ".7z"
    };

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public ChatAttachmentStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<UploadedAttachmentDto>> SaveAsync(
        IEnumerable<IFormFile> files,
        HttpRequest? request,
        int maxFileSizeMB = OrganisationSettingService.DefaultChatMaxFileSizeMB,
        CancellationToken cancellationToken = default)
    {
        var fileList = files?.Where(f => f is { Length: > 0 }).ToList()
            ?? throw new ChatOperationException("At least one file is required.");

        if (fileList.Count == 0)
            throw new ChatOperationException("At least one file is required.");

        if (fileList.Count > 5)
            throw new ChatOperationException("A maximum of 5 attachments is allowed per upload.");

        if (maxFileSizeMB <= 0)
            maxFileSizeMB = OrganisationSettingService.DefaultChatMaxFileSizeMB;

        // MB → bytes (organisation setting ChatMaxFileSizeMB).
        var maxBytes = (long)maxFileSizeMB * 1024L * 1024L;

        // Validate all files before writing any to disk.
        foreach (var file in fileList)
        {
            if (file.Length > maxBytes)
                throw new ChatOperationException(
                    $"File size cannot exceed {maxFileSizeMB} MB.");

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
                throw new ChatOperationException(
                    $"File type '{extension}' is not allowed for chat attachments.");
        }

        var folderName = _configuration["Chat:AttachmentRoot"] ?? "chat-attachments";
        folderName = folderName.Trim().Trim('/', '\\');
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = "chat-attachments";

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
            Directory.CreateDirectory(webRoot);
        }

        var physicalRoot = Path.Combine(webRoot, folderName);
        Directory.CreateDirectory(physicalRoot);

        var results = new List<UploadedAttachmentDto>(fileList.Count);
        var baseUrl = request is null
            ? null
            : $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');

        foreach (var file in fileList)
        {
            var extension = Path.GetExtension(file.FileName);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));
            var storedName = $"{Guid.NewGuid():N}_{safeName}{extension.ToLowerInvariant()}";
            var physicalPath = Path.Combine(physicalRoot, storedName);

            await using (var stream = new FileStream(
                physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var relativePath = $"/{folderName}/{storedName}".Replace('\\', '/');
            results.Add(new UploadedAttachmentDto
            {
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                SizeBytes = file.Length,
                FilePath = relativePath,
                FileUrl = baseUrl is null ? null : $"{baseUrl}{relativePath}"
            });
        }

        return results;
    }

    /// <summary>
    /// Resolves a web-relative attachment path (e.g. /chat-attachments/abc.jpg) to a safe physical file.
    /// Rejects path traversal and files outside the configured attachment root.
    /// </summary>
    public bool TryResolvePhysicalPath(
        string? relativePath,
        out string physicalPath,
        out string downloadFileName)
    {
        physicalPath = string.Empty;
        downloadFileName = "attachment";

        var raw = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Allow absolute URLs that point at our attachment folder.
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                return false;
            raw = uri.AbsolutePath;
        }

        if (!raw.StartsWith('/'))
            raw = "/" + raw;

        var folderName = _configuration["Chat:AttachmentRoot"] ?? "chat-attachments";
        folderName = folderName.Trim().Trim('/', '\\');
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = "chat-attachments";

        var prefix = $"/{folderName}/";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileName(raw);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");

        var physicalRoot = Path.GetFullPath(Path.Combine(webRoot, folderName));
        var candidate = Path.GetFullPath(Path.Combine(physicalRoot, fileName));

        if (!candidate.StartsWith(physicalRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!System.IO.File.Exists(candidate))
            return false;

        physicalPath = candidate;
        // Strip leading GUID_ prefix for a nicer download name when present.
        var display = fileName;
        var underscore = display.IndexOf('_');
        if (underscore is > 0 and <= 32 && display.Length > underscore + 1)
            display = display[(underscore + 1)..];
        downloadFileName = display;
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "file";

        var cleaned = new string(name
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')
            .ToArray());

        cleaned = cleaned.Trim('_');
        if (cleaned.Length == 0)
            cleaned = "file";
        if (cleaned.Length > 80)
            cleaned = cleaned[..80];

        return cleaned;
    }
}
