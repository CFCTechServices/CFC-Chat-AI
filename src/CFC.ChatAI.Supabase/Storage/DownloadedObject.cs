namespace CFC.ChatAI.Supabase.Storage;

/// <summary>
/// The caller owns and must dispose <see cref="Content"/>. Disposing this result disposes the
/// stream and the underlying HTTP response that keeps it open.
/// </summary>
public sealed class DownloadedObject : IAsyncDisposable, IDisposable
{
    public DownloadedObject(ObjectKey key, Stream content, string? contentType, long? size)
    {
        Key = key;
        Content = content;
        ContentType = contentType;
        Size = size;
    }

    public ObjectKey Key { get; }
    public Stream Content { get; }
    public string? ContentType { get; }
    public long? Size { get; }

    public void Dispose() => Content.Dispose();
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
