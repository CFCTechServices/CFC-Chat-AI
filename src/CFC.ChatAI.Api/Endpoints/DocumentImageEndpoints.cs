using System.Buffers.Text;
using System.Text;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Storage;
using Microsoft.AspNetCore.Http.Features;

namespace CFC.ChatAI.Api.Endpoints;

public static class DocumentImageEndpoints
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void MapDocumentImages(this WebApplication app)
    {
        app.MapGet("/api/chat/content/images/{**path}", ResolveAsync).RequireAuthorization("current-user");
        app.MapGet("/content/images/{**path}", ResolveAsync).RequireAuthorization("current-user");
    }

    private static async Task<IResult> ResolveAsync(string? path, HttpContext context, IObjectStorage storage,
        TimeProvider clock, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var rawPath = context.Features.Get<IHttpRequestFeature>()?.RawTarget?.Split('?', 2)[0];
        if (rawPath is not null && (rawPath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            rawPath.Contains("%5c", StringComparison.OrdinalIgnoreCase))) return Results.NotFound();
        if (path is not { Length: > 0 and <= 760 }) return Results.NotFound();
        var parts = path.Split('/');
        if (parts.Length != 4 || parts[0] != "docs" || parts[2] != "images" || !ValidRoot(parts[1]))
            return Results.NotFound();
        var file = parts[3];
        if (file.Length is < 1 or > 160 || !char.IsAsciiLetterOrDigit(file[0]) ||
            file.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            return Results.NotFound();
        var contentType = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => null
        };
        if (contentType is null) return Results.NotFound();

        var key = new ObjectKey(StorageArea.Documents, path);
        var prefix = new ObjectPrefix(key.Area, $"docs/{parts[1]}/images");
        for (var offset = 0; offset < 5000;)
        {
            var page = await storage.ListAsync(prefix, new PageRequest(offset, 500), ct);
            var entry = page.Items.FirstOrDefault(item => !item.IsFolder && item.Key == key);
            if (entry is not null)
            {
                if (!string.Equals(entry.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
                    return Results.NotFound();
                var expiresAt = clock.GetUtcNow() + LinkLifetime;
                var url = await storage.CreateSignedUrlAsync(key, LinkLifetime, ct);
                return Results.Ok(new { url, expires_at = expiresAt });
            }
            if (!page.HasMore || page.Items.Count == 0) break;
            offset += page.Items.Count;
        }
        return Results.NotFound();
    }

    private static bool ValidRoot(string root)
    {
        if (!root.StartsWith("!u-", StringComparison.Ordinal)) return DocumentId.IsValid(root);
        try
        {
            var id = StrictUtf8.GetString(Base64Url.DecodeFromChars(root.AsSpan(3)));
            return DocumentId.IsValid(id) && DocumentStoragePaths.GetRoot(id) == "docs/" + root;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
    }
}
