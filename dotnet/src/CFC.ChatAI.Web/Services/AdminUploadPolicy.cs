using System.Text.Json;
using System.Text.RegularExpressions;

namespace CFC.ChatAI.Web.Services;

public enum UploadArea { Single, Bulk, Email, EmailPdf }

public sealed record AdminUploadPolicy(string? VerifiedBackendUrl, long MaxFileBytes = AdminUploadPolicy.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 50L * 1024 * 1024;
    internal bool Approves(Uri? target) => MaxFileBytes > 0 && target is not null &&
        Uri.TryCreate(VerifiedBackendUrl, UriKind.Absolute, out var approved) &&
        (approved.Scheme == "https" || approved.Scheme == "http" && approved.IsLoopback) &&
        approved.UserInfo.Length == 0 && approved.Fragment.Length == 0 && approved.Query.Length == 0 &&
        approved.AbsoluteUri.TrimEnd('/') == target.AbsoluteUri.TrimEnd('/');

    public static string Accept(UploadArea area) => area switch
    {
        UploadArea.Single or UploadArea.Bulk => ".doc,.docx,.mp4,.mov,.m4v,.mkv,.webm",
        UploadArea.Email => ".eml",
        UploadArea.EmailPdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };

    internal static bool IsVideo(string filename) => Path.GetExtension(filename).ToLowerInvariant()
        is ".mp4" or ".mov" or ".m4v" or ".mkv" or ".webm";
    internal static string VideoSlug(string filename) =>
        Regex.Replace(Path.GetFileNameWithoutExtension(filename).Trim().ToLowerInvariant(), @"\s+", "-");

    internal static string? Validate(UploadArea area, string name, long size, long limit)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ||
            name.IndexOfAny(['/', '\\', ':', '"', '<', '>', '|', '?', '*']) >= 0 ||
            !Path.GetFileNameWithoutExtension(name).Any(c => c != '.' && !char.IsWhiteSpace(c)) ||
            Regex.IsMatch(name, @"^\s*(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "Choose a file with a valid filename.";
        if (!Accept(area).Split(',').Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            return $"Choose one of these formats: {Accept(area)}.";
        if (size <= 0 || size > limit) return $"Choose a nonempty file within the {limit:N0}-byte transfer limit.";
        return null;
    }

    // Fail closed on the current Python contract, even if an operator configured its URL.
    // OpenAPI proves only a bearer declaration. Deployment approval covers actual role enforcement.
    internal static bool DeclaresBearerOnUploads(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object ||
            !components.TryGetProperty("securitySchemes", out var schemes) || schemes.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) return false;

        var bearer = schemes.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.Object &&
            s.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "http" &&
            s.Value.TryGetProperty("scheme", out var scheme) && scheme.ValueKind == JsonValueKind.String &&
            string.Equals(scheme.GetString(), "bearer", StringComparison.OrdinalIgnoreCase)).Select(s => s.Name).ToHashSet();
        foreach (var path in new[] { "/api/files/upload", "/api/files/bulk", "/api/videos/upload" })
        {
            if (!paths.TryGetProperty(path, out var route) || route.ValueKind != JsonValueKind.Object ||
                !route.TryGetProperty("post", out var post) || post.ValueKind != JsonValueKind.Object) return false;
            if (!post.TryGetProperty("security", out var security) && !schema.TryGetProperty("security", out security)) return false;
            // Entries are alternatives. Every allowed alternative must require a bearer token.
            if (security.ValueKind != JsonValueKind.Array || security.GetArrayLength() == 0 ||
                security.EnumerateArray().Any(option => option.ValueKind != JsonValueKind.Object ||
                    !option.EnumerateObject().Any(s => bearer.Contains(s.Name) && s.Value.ValueKind == JsonValueKind.Array))) return false;
        }
        return true;
    }
}
