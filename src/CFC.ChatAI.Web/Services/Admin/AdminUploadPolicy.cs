using System.Text.RegularExpressions;

namespace CFC.ChatAI.Web.Services.Admin;

public enum UploadArea { Single, Bulk, Email, EmailPdf }

public sealed record AdminUploadPolicy(long MaxFileBytes = AdminUploadPolicy.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 50L * 1024 * 1024;
    internal const string DocumentFormats = ".doc,.docx,.pdf,.txt,.md,.eml";

    public static string Accept(UploadArea area) => area switch
    {
        UploadArea.Single or UploadArea.Bulk => DocumentFormats,
        UploadArea.Email => ".eml",
        UploadArea.EmailPdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };

    internal static string? Validate(UploadArea area, string name, long size, long limit)
    {
        if (!IsSafeFilename(name)) return "Choose a file with a valid filename.";
        if (!Accept(area).Split(',').Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            return $"Choose one of these formats: {Accept(area)}.";
        if (size <= 0 || size > limit) return $"Choose a nonempty file within the {limit:N0}-byte transfer limit.";
        return null;
    }

    private static bool IsSafeFilename(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 255 &&
        !name.EndsWith(' ') && !name.EndsWith('.') && !name.Any(char.IsControl) &&
        name.IndexOfAny(['/', '\\', ':', '"', '<', '>', '|', '?', '*']) < 0 &&
        Path.GetFileNameWithoutExtension(name).Any(c => c != '.' && !char.IsWhiteSpace(c)) &&
        !Regex.IsMatch(name, @"%[0-9A-Fa-f]{2}|^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])\s*\.",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
