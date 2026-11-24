namespace CFC.ChatAI.Web.Services.Api;

internal static class ChatImagePaths
{
    private const string ImageRoute = "api/chat/content/images/";
    private const string LegacyImageRoute = "content/images/";

    internal static string? ApiRoute(string path)
    {
        if (path.StartsWith("//", StringComparison.Ordinal) || path.Any(char.IsControl) || path.Contains('\\')) return null;
        var key = path.TrimStart('/');
        var prefixLength = key.StartsWith(ImageRoute, StringComparison.OrdinalIgnoreCase) ? ImageRoute.Length :
            key.StartsWith(LegacyImageRoute, StringComparison.OrdinalIgnoreCase) ? LegacyImageRoute.Length : 0;
        var segments = key[prefixLength..].Split('/');
        if (segments.Length != 4 || segments[0] != "docs" || segments[2] != "images") return null;

        // API URLs are already escaped; raw Storage keys aren't. Reject ambiguous separators and traversal in both.
        for (var i = 1; i < segments.Length; i++)
        {
            var decoded = Uri.UnescapeDataString(segments[i]);
            if (decoded is "" or "." or ".." || decoded.Contains('/') || decoded.Contains('\\') || decoded.Any(char.IsControl))
                return null;
            segments[i] = Uri.EscapeDataString(prefixLength > 0 ? decoded : segments[i]);
        }
        return ImageRoute + string.Join('/', segments);
    }

    internal static Uri? BrowserUrl(string path) => !path.Any(char.IsControl) && !path.Contains('\\') &&
        Uri.TryCreate(path, UriKind.Absolute, out var url) && url.Host.Length > 0 && url.UserInfo.Length == 0 &&
        (url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback)) ? url : null;
}
