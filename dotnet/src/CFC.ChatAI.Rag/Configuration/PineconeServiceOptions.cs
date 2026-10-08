namespace CFC.ChatAI.Rag.Configuration;

public sealed record PineconeTargetOptions
{
    public required string IndexName { get; init; }
    public string? Host { get; init; }
    public string Namespace { get; init; } = "";
    /// <summary>Operator attestation: this target was rebuilt or verified for this exact model/export.</summary>
    public required string EmbeddingModelIdentity { get; init; }
}

public sealed record EmbeddingOptions
{
    public required string ModelPath { get; init; }
    public required string VocabularyPath { get; init; }
    /// <summary>Use an immutable export identifier, preferably model/vocabulary SHA256 digests.</summary>
    public required string ModelIdentity { get; init; }
}

public sealed record PineconeServiceOptions
{
    public required string ApiKey { get; init; }
    public required PineconeTargetOptions Documents { get; init; }
    public required PineconeTargetOptions Videos { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int BatchSize { get; init; } = 100;
    public int MaxPayloadBytes { get; init; } = 1_900_000;
    public int MaxMetadataBytes { get; init; } = 40_000;
    public int MaxTopK { get; init; } = 100;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    /// <summary>Trusted authorization scope established at registration, never widened by caller filters.
    /// Null means unrestricted; empty means no documents. Use an isolated service instance per scope.
    /// Do not register a tenant-scoped instance as a cross-tenant singleton.</summary>
    public IReadOnlyList<string>? AllowedDocumentIds { get; init; }

    internal void Validate(EmbeddingOptions embedding)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.StartsWith("your-", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(embedding.ModelPath) || string.IsNullOrWhiteSpace(embedding.VocabularyPath) ||
            string.IsNullOrWhiteSpace(embedding.ModelIdentity) || BatchSize is < 1 or > 1000 ||
            MaxPayloadBytes is < 4096 or > 2_000_000 || MaxMetadataBytes is < 1 or > 40_000 ||
            MaxTopK is < 1 or > 1000 || MaxRetries is < 0 or > 10 || RequestTimeout <= TimeSpan.Zero ||
            RequestTimeout > TimeSpan.FromMinutes(5) || RetryBaseDelay < TimeSpan.Zero || RetryBaseDelay > TimeSpan.FromSeconds(10))
            throw new Contracts.PineconeServiceException(Contracts.PineconeErrorCategory.Configuration, "Invalid Pinecone or embedding settings.");
        foreach (var target in new[] { Documents, Videos })
        {
            if (target is null || string.IsNullOrWhiteSpace(target.IndexName) || target.Namespace is null ||
                target.EmbeddingModelIdentity != embedding.ModelIdentity ||
                (target.Host is not null && !Internal.PineconeTransport.IsSafeHost(target.Host)))
                throw new Contracts.PineconeServiceException(Contracts.PineconeErrorCategory.Configuration,
                    "A target needs a valid name/host and an explicit matching model/export identity; rebuild or verify compatibility before cutover.");
        }
        if (AllowedDocumentIds is not null && (AllowedDocumentIds.Count > 1000 || AllowedDocumentIds.Any(string.IsNullOrWhiteSpace)))
            throw new Contracts.PineconeServiceException(Contracts.PineconeErrorCategory.Configuration, "Invalid trusted document scope.");
    }
}
