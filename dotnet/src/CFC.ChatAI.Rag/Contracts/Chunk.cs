namespace CFC.ChatAI.Rag.Contracts;

public enum ChunkCollection { Documents, Videos }
public enum ChunkSourceType { Document, Video }

/// <summary>Full content; artifact references are never fetched by the service.</summary>
public sealed record Chunk
{
    public required string ChunkId { get; init; }
    public required string DocumentId { get; init; }
    public required string Text { get; init; }
    public required string Source { get; init; }
    public required ChunkSourceType SourceType { get; init; }
    public string? SectionId { get; init; }
    public string? SectionTitle { get; init; }
    public string? SectionPath { get; init; }
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockIds { get; init; } = Array.Empty<string>();
    public double? StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public string? ArtifactUrl { get; init; }
    public string? VideoUrl { get; init; }
}

public sealed record ChunkSearchOptions
{
    public int TopK { get; init; } = 5;
    public double? MinimumSimilarity { get; init; }
    public IReadOnlyList<string>? DocumentIds { get; init; }
    public ChunkSourceType? SourceType { get; init; }
    public string? SectionId { get; init; }
}

public sealed record ChunkSearchMatch(Chunk Chunk, double SimilarityScore, int Rank, double? RerankScore = null);
public enum ChunkOperationStatus { Acknowledged, NoOp }
public sealed record ChunkWriteResult(ChunkOperationStatus Status, IReadOnlyList<string> AcknowledgedIds);
/// <summary>Acknowledgement is not evidence of prior existence or immediate visibility.</summary>
public sealed record ChunkDeleteResult(ChunkOperationStatus Status, IReadOnlyList<string> AcknowledgedIds, int? AffectedCount = null);
public enum PineconeErrorCategory { Configuration, IncompatibleIndex, InvalidChunk, OversizedChunk, DependencyUnavailable, PartialWrite, PartialDelete }

public class PineconeServiceException : Exception
{
    public PineconeErrorCategory Category { get; }
    public PineconeServiceException(PineconeErrorCategory category, string message) : base(message) => Category = category;
}

/// <summary>Retry unresolved IDs deterministically. Cancellation after dispatch can also be indeterminate.</summary>
public sealed class ChunkOperationException : PineconeServiceException
{
    public IReadOnlyList<string> CompletedIds { get; }
    public IReadOnlyList<string> UnresolvedIds { get; }
    public bool IsIndeterminate { get; }
    public bool WasCanceled { get; }
    public string OperationId { get; }
    public string? DocumentId { get; }
    public PineconeErrorCategory? FailureCategory { get; }
    public ChunkOperationException(PineconeErrorCategory category, IReadOnlyList<string> completedIds,
        IReadOnlyList<string> unresolvedIds, bool indeterminate, bool wasCanceled, string operationId, string? documentId = null, PineconeErrorCategory? failureCategory = null)
        : base(category, $"Chunk operation did not complete ({failureCategory?.ToString() ?? "cancellation or dependency failure"}); reconcile unresolved inputs before retrying.")
    {
        CompletedIds = Array.AsReadOnly(completedIds.ToArray());
        UnresolvedIds = Array.AsReadOnly(unresolvedIds.ToArray());
        IsIndeterminate = indeterminate;
        WasCanceled = wasCanceled;
        OperationId = operationId;
        DocumentId = documentId;
        FailureCategory = failureCategory;
    }
}

public sealed record PineconeTargetHealth(ChunkCollection Collection, bool Ready, string Diagnostic);
public sealed record PineconeHealth(bool ModelReady, IReadOnlyList<PineconeTargetHealth> Targets, DateTimeOffset CheckedAt)
{
    public bool Ready => ModelReady && Targets.All(t => t.Ready);
}
