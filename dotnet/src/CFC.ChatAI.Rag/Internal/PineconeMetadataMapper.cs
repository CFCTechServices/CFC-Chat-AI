using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag.Internal;

internal static class PineconeMetadataMapper
{
    internal const int SchemaVersion = 1;
    internal static string SourceType(ChunkCollection collection) => collection switch
    {
        ChunkCollection.Documents => "document", ChunkCollection.Videos => "video",
        _ => throw Invalid("Unknown collection.")
    };
    internal static string RecordId(ChunkCollection collection, string chunkId) =>
        SourceType(collection) + ":" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chunkId)));

    internal static JsonElement Encode(ChunkCollection collection, Chunk chunk, string modelIdentity, int maxBytes)
    {
        Validate(chunk, collection);
        var metadata = new Dictionary<string, object>
        {
            ["schema_version"] = SchemaVersion, ["model_identity"] = modelIdentity,
            ["collection"] = SourceType(collection), ["source_type"] = SourceType(collection),
            ["chunk_id"] = chunk.ChunkId, ["doc_id"] = chunk.DocumentId, ["text"] = chunk.Text, ["source"] = chunk.Source
        };
        Add("section_id", chunk.SectionId); Add("section_title", chunk.SectionTitle); Add("section_path", chunk.SectionPath);
        Add("image_paths", chunk.ImagePaths.Count == 0 ? null : chunk.ImagePaths.ToArray());
        Add("block_ids", chunk.BlockIds.Count == 0 ? null : chunk.BlockIds.ToArray());
        Add("start_seconds", chunk.StartSeconds); Add("end_seconds", chunk.EndSeconds);
        Add("artifact_url", chunk.ArtifactUrl); Add("video_url", chunk.VideoUrl);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
        if (bytes.Length > maxBytes)
            throw new PineconeServiceException(PineconeErrorCategory.OversizedChunk, "Full chunk metadata exceeds the configured UTF-8 size limit; resize upstream.");
        return JsonSerializer.Deserialize<JsonElement>(bytes);
        void Add(string key, object? value) { if (value is not null) metadata[key] = value; }
    }

    internal static Chunk? Decode(ChunkCollection collection, string recordId, JsonElement metadata, string modelIdentity)
    {
        try
        {
            if (metadata.GetProperty("schema_version").GetInt32() != SchemaVersion ||
                Required("model_identity") != modelIdentity || Required("collection") != SourceType(collection) ||
                Required("source_type") != SourceType(collection)) return null;
            var chunk = new Chunk
            {
                ChunkId = Required("chunk_id"), DocumentId = Required("doc_id"), Text = Required("text"), Source = Required("source"),
                SourceType = collection == ChunkCollection.Documents ? ChunkSourceType.Document : ChunkSourceType.Video,
                SectionId = Optional("section_id"), SectionTitle = Optional("section_title"), SectionPath = Optional("section_path"),
                ImagePaths = List("image_paths"), BlockIds = List("block_ids"),
                StartSeconds = Number("start_seconds"), EndSeconds = Number("end_seconds"),
                ArtifactUrl = Optional("artifact_url"), VideoUrl = Optional("video_url")
            };
            Validate(chunk, collection);
            return recordId == RecordId(collection, chunk.ChunkId) ? chunk : null;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or PineconeServiceException or OverflowException)
        { return null; }
        string Required(string key) => metadata.GetProperty(key).GetString() ?? throw Invalid("Missing required metadata.");
        string? Optional(string key) => metadata.TryGetProperty(key, out var value) ? value.GetString() ?? throw Invalid("Invalid metadata.") : null;
        double? Number(string key) => metadata.TryGetProperty(key, out var value) ? value.GetDouble() : null;
        IReadOnlyList<string> List(string key) => metadata.TryGetProperty(key, out var value)
            ? Array.AsReadOnly(value.EnumerateArray().Select(x => x.GetString() ?? throw Invalid("Invalid list.")).ToArray())
            : Array.Empty<string>();
    }

    internal static void Validate(Chunk chunk, ChunkCollection collection)
    {
        var expected = SourceType(collection);
        if (chunk is null || string.IsNullOrWhiteSpace(chunk.ChunkId) || string.IsNullOrWhiteSpace(chunk.DocumentId) ||
            string.IsNullOrWhiteSpace(chunk.Text) || string.IsNullOrWhiteSpace(chunk.Source) ||
            (expected == "document" ? chunk.SourceType != ChunkSourceType.Document : chunk.SourceType != ChunkSourceType.Video) ||
            chunk.ImagePaths is null || chunk.BlockIds is null || chunk.ImagePaths.Any(string.IsNullOrWhiteSpace) || chunk.BlockIds.Any(string.IsNullOrWhiteSpace) ||
            InvalidTime(chunk.StartSeconds) || InvalidTime(chunk.EndSeconds) ||
            (chunk.StartSeconds.HasValue && chunk.EndSeconds.HasValue && chunk.EndSeconds < chunk.StartSeconds))
            throw Invalid("Invalid required chunk content, source type, lists or timestamps.");
        static bool InvalidTime(double? value) => value.HasValue && (!double.IsFinite(value.Value) || value < 0);
    }
    internal static PineconeServiceException Invalid(string message) => new(PineconeErrorCategory.InvalidChunk, message);
}
