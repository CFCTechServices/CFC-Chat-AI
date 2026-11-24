using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public sealed record AnswerImage(Uri Url, string AltText, int Offset);

public static class AnswerMedia
{
    private const string ImageRoute = "api/chat/content/images/";

    public static IReadOnlyList<AnswerImage> Images(string text, ChatMessageMetadata? metadata, Uri? backend)
    {
        if (metadata is null) return [];
        // An explicit empty list means Python selected no images. Only older rows
        // without that field can fall back to the image paths stored in citations.
        var images = metadata.RelevantImages ?? LegacyImages(metadata.Citations);
        var placed = new List<AnswerImage>();
        foreach (var image in images)
        {
            if (ImageUrl(image.Path, backend) is not { } url) continue;
            placed.Add(new(url, string.IsNullOrWhiteSpace(image.AltText) ? "Document image" : image.AltText,
                TextOffset(text, image.Position)));
        }
        return placed.OrderBy(image => image.Offset).ToArray();
    }

    private static IEnumerable<ChatImage> LegacyImages(IReadOnlyList<Dictionary<string, JsonElement>>? citations)
    {
        if (citations is null) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var citation in citations)
        {
            if (citation is null || !citation.TryGetValue("image_paths", out var paths) || paths.ValueKind != JsonValueKind.Array) continue;
            foreach (var path in paths.EnumerateArray())
                if (path.ValueKind == JsonValueKind.String && path.GetString() is { } key && seen.Add(key))
                    yield return new ChatImage(key);
        }
    }

    private static Uri? ImageUrl(string? path, Uri? backend)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.Contains('\\')) return null;
        path = path.Trim();
        if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(path, UriKind.Absolute, out var url) && url.Host.Length > 0 && url.UserInfo.Length == 0 ? url : null;

        if (backend is not { IsAbsoluteUri: true } || backend.Scheme is not ("http" or "https")) return null;
        // Raw storage keys are opaque. Already-routed URLs have URL-encoded segments.
        bool routed = path.StartsWith("/" + ImageRoute, StringComparison.Ordinal) || path.StartsWith(ImageRoute, StringComparison.Ordinal);
        if (routed) path = path.TrimStart('/')[ImageRoute.Length..];
        if (!path.StartsWith("docs/", StringComparison.Ordinal) && !path.StartsWith("images/", StringComparison.Ordinal)) return null;
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var decoded = Uri.UnescapeDataString(segments[i]);
            if (decoded is "" or "." or ".." || decoded.Contains('/') || decoded.Contains('\\') || decoded.Any(char.IsControl)) return null;
            segments[i] = Uri.EscapeDataString(routed ? decoded : segments[i]);
        }
        return Uri.TryCreate(backend, ImageRoute + string.Join('/', segments), out var resolved) ? resolved : null;
    }

    private static int TextOffset(string text, int? position)
    {
        if (position is null or < 0) return text.Length;
        // Python counts Unicode code points. A .NET string indexes UTF-16 code units.
        var offset = 0;
        for (int i = 0; i < position && offset < text.Length; i++)
            offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
        if (offset == 0 || offset == text.Length || text[offset - 1] == '\n') return offset;

        // Keep list items and inline emphasis intact when a marker lands inside them.
        var start = text.LastIndexOf('\n', offset - 1) + 1;
        var end = text.IndexOf('\n', offset);
        if (end < 0) end = text.Length;
        var line = text.AsSpan(start, end - start);
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) return end;
        if (line.Contains('*'))
        {
            foreach (var emphasis in Regex.EnumerateMatches(line, @"\*\*(.+?)\*\*|\*(.+?)\*"))
            {
                var opening = start + emphasis.Index;
                var closing = opening + emphasis.Length;
                if (opening < offset && offset < closing) return closing;
            }
        }
        return offset;
    }

    public static IEnumerable<string> Sources(IReadOnlyList<Dictionary<string, JsonElement>>? citations)
    {
        if (citations is null) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var citation in citations)
        {
            if (citation is null) continue;
            string? Field(string key) => citation.TryGetValue(key, out var field) && field.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(field.GetString()) ? field.GetString() : null;
            var source = Field("source") ?? Field("doc_id");
            var section = Field("section_title") ?? Field("section_path");
            var label = source is not null && section is not null ? $"{source} — {section}" : source ?? section ?? Field("chunk_id");
            if (label is not null && seen.Add(label)) yield return label;
        }
    }
}
