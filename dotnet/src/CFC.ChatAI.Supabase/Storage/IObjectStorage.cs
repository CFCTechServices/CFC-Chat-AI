using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Storage;

/// <summary>Object operations only. It never reads or deletes database rows or vector records.</summary>
public interface IObjectStorage
{
    /// <summary>The caller retains ownership of <paramref name="content"/>.</summary>
    Task<StoredObject> UploadAsync(
        ObjectKey key,
        Stream content,
        UploadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>The caller owns the returned stream and must dispose it.</summary>
    Task<DownloadedObject> DownloadAsync(
        ObjectKey key,
        CancellationToken cancellationToken = default);

    Task<Page<StorageEntry>> ListAsync(
        ObjectPrefix prefix,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IReadOnlyList<ObjectKey> keys,
        CancellationToken cancellationToken = default);

    Task<Uri> CreateSignedUrlAsync(
        ObjectKey key,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>Constructs a public URL only; it does not assert that the object exists/is public.</summary>
    Uri GetPublicUrl(ObjectKey key);
}
