using System.Text.Json;
using CFC.ChatAI.Rag.Configuration;

namespace CFC.ChatAI.Rag.Internal;

internal sealed record PineconeRecord(string Id, float[] Values, JsonElement Metadata);
internal sealed record PineconeMatch(string Id, double Score, JsonElement Metadata);
internal interface IPineconeTransport
{
    Task EnsureReadyAsync(PineconeTargetOptions target, bool refresh, CancellationToken cancellationToken);
    Task UpsertAsync(PineconeTargetOptions target, IReadOnlyList<PineconeRecord> records, CancellationToken cancellationToken);
    Task<IReadOnlyList<PineconeMatch>> QueryAsync(PineconeTargetOptions target, float[] vector, int topK, JsonElement filter, CancellationToken cancellationToken);
    Task DeleteAsync(PineconeTargetOptions target, JsonElement filter, CancellationToken cancellationToken);
}

internal static class PineconePayload
{
    internal static byte[] Upsert(PineconeTargetOptions target, IReadOnlyList<PineconeRecord> records) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        @namespace = target.Namespace,
        vectors = records.Select(r => new { id = r.Id, values = r.Values, metadata = r.Metadata })
    });
}
