using System.Diagnostics;
using System.Text.Json;
using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Rag;

/// <summary>Only production entry point for Pinecone. Writes/deletes are not atomic or immediately visible.</summary>
public sealed class PineconeService : IPineconeService
{
    private readonly PineconeServiceOptions _options;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly IEmbeddingEncoder _encoder;
    private readonly IPineconeTransport _transport;
    private readonly ILogger<PineconeService> _logger;
    private readonly HashSet<string>? _scope;

    internal PineconeService(PineconeServiceOptions options, EmbeddingOptions embeddingOptions,
        IEmbeddingEncoder encoder, IPineconeTransport transport, ILogger<PineconeService> logger)
    {
        options.Validate(embeddingOptions);
        _options = options;
        _embeddingOptions = embeddingOptions;
        _encoder = encoder;
        _transport = transport;
        _logger = logger;
        _scope = options.AllowedDocumentIds is null ? null : new(options.AllowedDocumentIds, StringComparer.Ordinal);
    }

    public async Task<ChunkWriteResult> UpsertChunksAsync(ChunkCollection collection, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        var target = Target(collection);
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot caller-owned content/lists through the metadata contract before any side effects.
        var prepared = chunks.Select(chunk =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = PineconeMetadataMapper.Encode(collection, chunk, _embeddingOptions.ModelIdentity, _options.MaxMetadataBytes);
            if (!InScope(chunk.DocumentId)) throw PineconeMetadataMapper.Invalid("Chunk document is outside the trusted scope.");
            return (Chunk: PineconeMetadataMapper.Decode(collection, PineconeMetadataMapper.RecordId(collection, chunk.ChunkId), metadata, _embeddingOptions.ModelIdentity)!, Metadata: metadata);
        }).ToArray();
        var ids = ValidateIds(prepared.Select(x => x.Chunk.ChunkId).ToArray());
        if (ids.Length == 0) return new(ChunkOperationStatus.NoOp, ids);
        // Conservative upper bound for finite float serialization, including all metadata and namespace overhead.
        // Reject records which cannot safely fit before sending any batch, not midway through the operation.
        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shell = new PineconeRecord(PineconeMetadataMapper.RecordId(collection, item.Chunk.ChunkId), [], item.Metadata);
            if (PineconePayload.Upsert(target, [shell]).Length + OnnxEmbeddingModel.Dimension * 20 > _options.MaxPayloadBytes)
                throw PineconeMetadataMapper.Invalid("Chunk plus embedding exceeds the configured request payload budget.");
        }
        await _transport.EnsureReadyAsync(target, false, cancellationToken);
        var completed = new List<string>();
        var operationId = Guid.NewGuid().ToString("N");
        var timer = Stopwatch.StartNew();
        var dispatching = false;
        try
        {
            foreach (var group in prepared.Chunk(_options.BatchSize))
            {
                var batch = new List<PineconeRecord>();
                foreach (var item in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vector = await _encoder.EncodeAsync(item.Chunk.Text, cancellationToken);
                    EmbeddingValidation.Validate(vector);
                    var record = new PineconeRecord(PineconeMetadataMapper.RecordId(collection, item.Chunk.ChunkId), vector, item.Metadata);
                    if (batch.Count > 0 && PineconePayload.Upsert(target, batch.Append(record).ToArray()).Length > _options.MaxPayloadBytes)
                        await Flush();
                    batch.Add(record);
                }
                await Flush();
                async Task Flush()
                {
                    if (batch.Count == 0) return;
                    cancellationToken.ThrowIfCancellationRequested();
                    dispatching = true;
                    await _transport.UpsertAsync(target, batch, cancellationToken);
                    completed.AddRange(batch.Select(r => r.Metadata.GetProperty("chunk_id").GetString()!));
                    dispatching = false;
                    batch.Clear();
                }
            }
            Log("upsert", operationId, completed.Count, timer.ElapsedMilliseconds);
            return new(ChunkOperationStatus.Acknowledged, Array.AsReadOnly(completed.ToArray()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is OperationCanceledException && completed.Count == 0 && !dispatching) throw;
            Log("upsert_failed", operationId, completed.Count, timer.ElapsedMilliseconds);
            throw Partial(PineconeErrorCategory.PartialWrite, completed, ids, dispatching, ex, operationId);
        }
    }

    public async Task<IReadOnlyList<ChunkSearchMatch>> SearchAsync(ChunkCollection collection, string query, ChunkSearchOptions options, CancellationToken cancellationToken = default)
    {
        var target = Target(collection);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(query) || options.TopK < 1 || options.TopK > _options.MaxTopK ||
            (options.MinimumSimilarity is { } minimum && (!double.IsFinite(minimum) || minimum is < -1 or > 1)) ||
            (options.SourceType.HasValue && options.SourceType != ExpectedSource(collection)) ||
            (options.SectionId is not null && string.IsNullOrWhiteSpace(options.SectionId)))
            throw PineconeMetadataMapper.Invalid("Invalid query, limits or collection filters.");
        var documents = options.DocumentIds is null ? null : ValidateIds(options.DocumentIds);
        if (documents?.Length > 1000) throw PineconeMetadataMapper.Invalid("Too many document filters.");
        cancellationToken.ThrowIfCancellationRequested();
        if (_scope?.Count == 0 || documents?.Length == 0) return Array.Empty<ChunkSearchMatch>();
        var filter = Filter(collection, documents, options.SectionId, contentCompatible: true);
        if (JsonSerializer.SerializeToUtf8Bytes(filter).Length + 8192 > _options.MaxPayloadBytes)
            throw PineconeMetadataMapper.Invalid("Search filter exceeds payload budget.");
        await _transport.EnsureReadyAsync(target, false, cancellationToken);
        var vector = await _encoder.EncodeAsync(query, cancellationToken);
        EmbeddingValidation.Validate(vector);
        var timer = Stopwatch.StartNew();
        var matches = await _transport.QueryAsync(target, vector, options.TopK, filter, cancellationToken);
        var results = new List<ChunkSearchMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = PineconeMetadataMapper.Decode(collection, match.Id, match.Metadata, _embeddingOptions.ModelIdentity);
            if (chunk is null || !double.IsFinite(match.Score))
            {
                PineconeDiagnostics.InvalidMetadata.Add(1, new KeyValuePair<string, object?>("collection", collection.ToString()));
                _logger.LogWarning("Skipping invalid/legacy Pinecone chunk metadata for {Collection}", collection);
                continue;
            }
            if (!InScope(chunk.DocumentId) || (documents is not null && !documents.Contains(chunk.DocumentId, StringComparer.Ordinal)) ||
                (options.SectionId is not null && chunk.SectionId != options.SectionId) || !seen.Add(chunk.ChunkId)) continue;
            if (options.MinimumSimilarity.HasValue && match.Score < options.MinimumSimilarity) continue;
            results.Add(new(chunk, match.Score, results.Count + 1));
            if (results.Count == options.TopK) break;
        }
        Log("search", Guid.NewGuid().ToString("N"), results.Count, timer.ElapsedMilliseconds);
        return results.AsReadOnly();
    }

    public async Task<ChunkDeleteResult> DeleteChunksAsync(ChunkCollection collection, IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
    {
        var target = Target(collection);
        var ids = ValidateIds(chunkIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (ids.Length == 0 || _scope?.Count == 0) return new(ChunkOperationStatus.NoOp, Array.Empty<string>());
        // Filter-based ID deletion avoids unsafe unscoped deletes and fetch responses containing vectors.
        var batches = new List<(string[] Ids, JsonElement Filter)>();
        foreach (var group in ids.Chunk(_options.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = new List<string>();
            foreach (var id in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(id);
                if (DeletePayloadSize(target, Filter(collection, chunkIds: batch)) > _options.MaxPayloadBytes)
                {
                    batch.RemoveAt(batch.Count - 1);
                    if (batch.Count == 0) throw PineconeMetadataMapper.Invalid("Delete filter exceeds payload budget.");
                    batches.Add((batch.ToArray(), Filter(collection, chunkIds: batch)));
                    batch = [id];
                    if (DeletePayloadSize(target, Filter(collection, chunkIds: batch)) > _options.MaxPayloadBytes)
                        throw PineconeMetadataMapper.Invalid("Delete filter exceeds payload budget.");
                }
            }
            if (batch.Count > 0) batches.Add((batch.ToArray(), Filter(collection, chunkIds: batch)));
        }
        await _transport.EnsureReadyAsync(target, false, cancellationToken);
        var completed = new List<string>();
        var operationId = Guid.NewGuid().ToString("N");
        var timer = Stopwatch.StartNew();
        var dispatching = false;
        try
        {
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dispatching = true;
                await _transport.DeleteAsync(target, batch.Filter, cancellationToken);
                completed.AddRange(batch.Ids);
                dispatching = false;
            }
            Log("delete_chunks", operationId, completed.Count, timer.ElapsedMilliseconds);
            return new(ChunkOperationStatus.Acknowledged, completed.AsReadOnly());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is OperationCanceledException && completed.Count == 0 && !dispatching) throw;
            Log("delete_chunks_failed", operationId, completed.Count, timer.ElapsedMilliseconds);
            throw Partial(PineconeErrorCategory.PartialDelete, completed, ids, dispatching, ex, operationId);
        }
    }

    public async Task<ChunkDeleteResult> DeleteDocumentAsync(ChunkCollection collection, string documentId, CancellationToken cancellationToken = default)
    {
        var target = Target(collection);
        ValidateIds([documentId]);
        if (!InScope(documentId)) throw PineconeMetadataMapper.Invalid("Document is outside the trusted scope.");
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Filter(collection, [documentId]);
        if (DeletePayloadSize(target, filter) > _options.MaxPayloadBytes) throw PineconeMetadataMapper.Invalid("Delete filter exceeds payload budget.");
        await _transport.EnsureReadyAsync(target, false, cancellationToken);
        var operationId = Guid.NewGuid().ToString("N");
        var timer = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        try { await _transport.DeleteAsync(target, filter, cancellationToken); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("delete_document_failed", operationId, 0, timer.ElapsedMilliseconds);
            throw new ChunkOperationException(PineconeErrorCategory.PartialDelete, [], [], true, ex is OperationCanceledException, operationId, documentId,
                ex is PineconeServiceException domainError ? domainError.Category : null);
        }
        // Zero below is acknowledged chunk IDs, not an affected-record count.
        Log("delete_document", operationId, 0, timer.ElapsedMilliseconds);
        // Filter deletion cannot provide an exact affected count.
        return new(ChunkOperationStatus.Acknowledged, Array.Empty<string>(), null);
    }

    public async Task<PineconeHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var modelReady = await _encoder.IsReadyAsync(cancellationToken);
        var statuses = new Dictionary<PineconeTargetOptions, (bool Ready, string Diagnostic)>();
        var targets = new List<PineconeTargetHealth>();
        foreach (var collection in Enum.GetValues<ChunkCollection>())
        {
            var target = Target(collection);
            if (!statuses.TryGetValue(target, out var status))
            {
                try
                {
                    await _transport.EnsureReadyAsync(target, true, cancellationToken);
                    status = (true, "Target available; dimension/metric verified. This is not an end-to-end retrieval check.");
                }
                catch (OperationCanceledException) { throw; }
                catch (PineconeServiceException ex) { status = (false, ex.Category.ToString()); }
                catch { status = (false, "DependencyUnavailable"); }
                statuses[target] = status;
            }
            targets.Add(new(collection, status.Ready, status.Diagnostic));
        }
        return new(modelReady, targets.AsReadOnly(), DateTimeOffset.UtcNow);
    }

    private PineconeTargetOptions Target(ChunkCollection collection) => collection switch
    {
        ChunkCollection.Documents => _options.Documents, ChunkCollection.Videos => _options.Videos,
        _ => throw PineconeMetadataMapper.Invalid("Unknown collection.")
    };
    private static ChunkSourceType ExpectedSource(ChunkCollection collection) => collection == ChunkCollection.Documents ? ChunkSourceType.Document : ChunkSourceType.Video;
    private bool InScope(string documentId) => _scope is null || _scope.Contains(documentId);
    private JsonElement Filter(ChunkCollection collection, IReadOnlyList<string>? documents = null, string? section = null, IReadOnlyList<string>? chunkIds = null, bool contentCompatible = false)
    {
        var constraints = new List<object>();
        Equal("collection", PineconeMetadataMapper.SourceType(collection));
        Equal("source_type", PineconeMetadataMapper.SourceType(collection));
        if (contentCompatible)
        {
            Equal("schema_version", PineconeMetadataMapper.SchemaVersion);
            Equal("model_identity", _embeddingOptions.ModelIdentity);
        }
        if (_scope is not null) In("doc_id", _scope.ToArray());
        if (documents is not null) In("doc_id", documents);
        if (section is not null) Equal("section_id", section);
        if (chunkIds is not null) In("chunk_id", chunkIds);
        return JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["$and"] = constraints });
        void Equal(string field, object value) => constraints.Add(new Dictionary<string, object> { [field] = new Dictionary<string, object> { ["$eq"] = value } });
        void In(string field, IReadOnlyList<string> values) => constraints.Add(new Dictionary<string, object> { [field] = new Dictionary<string, object> { ["$in"] = values } });
    }
    private static int DeletePayloadSize(PineconeTargetOptions target, JsonElement filter) => JsonSerializer.SerializeToUtf8Bytes(new { @namespace = target.Namespace, filter }).Length;
    private static string[] ValidateIds(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var snapshot = ids.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace) || snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw PineconeMetadataMapper.Invalid("IDs must be nonblank and unique.");
        return snapshot;
    }
    private static ChunkOperationException Partial(PineconeErrorCategory category, List<string> completed, string[] ids, bool indeterminate, Exception ex, string operationId)
        => new(category, completed, ids.Except(completed, StringComparer.Ordinal).ToArray(), indeterminate, ex is OperationCanceledException, operationId,
            failureCategory: ex is PineconeServiceException domainError ? domainError.Category : null);
    private void Log(string operation, string operationId, int count, long milliseconds)
    {
        PineconeDiagnostics.OperationLatency.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));
        _logger.LogInformation("Pinecone {Operation} {OperationId}: {Count} chunks, {ElapsedMs}ms", operation, operationId, count, milliseconds);
    }
}
