namespace Chat.Domain.DTOs.Messages;

/// <summary>
/// Allowed WhatsApp-style reaction codes and their emoji values.
/// </summary>
public static class MessageReactionCodes
{
    public const string Like = "LIKE";
    public const string Love = "LOVE";
    public const string Laugh = "LAUGH";
    public const string Wow = "WOW";
    public const string Sad = "SAD";
    public const string Thanks = "THANKS";

    public static readonly IReadOnlyDictionary<string, string> CodeToEmoji =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Like] = "👍",
            [Love] = "❤️",
            [Laugh] = "😂",
            [Wow] = "😮",
            [Sad] = "😢",
            [Thanks] = "🙏",
        };

    public static bool TryNormalize(string? reactionCode, out string? normalizedCode, out string? emoji)
    {
        normalizedCode = null;
        emoji = null;
        if (string.IsNullOrWhiteSpace(reactionCode))
            return true; // clear

        var code = reactionCode.Trim().ToUpperInvariant();
        if (!CodeToEmoji.TryGetValue(code, out var mapped))
            return false;

        normalizedCode = code;
        emoji = mapped;
        return true;
    }

    public static string AllowedList => "LIKE, LOVE, LAUGH, WOW, SAD, THANKS";
}
