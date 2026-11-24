using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Api;

namespace CFC.ChatAI.Web.Services.Chat;

public sealed record AnswerImage(Uri Url, string AltText, int Offset);

public static class AnswerMedia
{
    public static IReadOnlyList<AnswerImage> Images(string text, ChatMessageMetadata? metadata)
    {
        if (metadata is null) return [];
        // An explicit empty list means the backend selected no images. Only older rows
        // without that field can fall back to the image paths stored in citations.
        var images = metadata.RelevantImages ?? LegacyImages(metadata.Citations);
        var placed = new List<AnswerImage>();
        foreach (var image in images)
        {
            if (ImageUrl(image.Path) is not { } url) continue;
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

    private static Uri? ImageUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // A relative route is resolved by the server with the current user's token, not rendered as an img src.
        return ChatImagePaths.ApiRoute(path) is { } route ? new Uri(route, UriKind.Relative) : ChatImagePaths.BrowserUrl(path);
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
