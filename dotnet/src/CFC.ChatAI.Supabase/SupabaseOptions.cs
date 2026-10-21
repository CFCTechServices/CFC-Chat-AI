namespace CFC.ChatAI.Supabase;

/// <summary>Supabase connection and bucket settings; secrets should come from secret storage.</summary>
public sealed class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;
    public string AnonKey { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string DocumentsBucket { get; set; } = "cfc-docs";
    public string VideosBucket { get; set; } = "cfc-videos";

    internal void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Supabase Url must be an absolute HTTP(S) URL.");
        if (string.IsNullOrWhiteSpace(AnonKey))
            throw new InvalidOperationException("Supabase AnonKey is required.");
        if (string.IsNullOrWhiteSpace(ServiceRoleKey))
            throw new InvalidOperationException("Supabase ServiceRoleKey is required for backend repositories.");
        if (string.IsNullOrWhiteSpace(DocumentsBucket) || string.IsNullOrWhiteSpace(VideosBucket))
            throw new InvalidOperationException("Supabase storage bucket names must be configured.");
    }
}
