using System.Text.Json;
using System.Text.Json.Serialization;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Serialization;

// A malformed optional image must not discard a saved answer or its valid sibling images.
internal sealed class ChatImageListConverter : JsonConverter<IReadOnlyList<ChatImage>>
{
    public override IReadOnlyList<ChatImage> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var images = new List<ChatImage>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return images;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || StringField(item, "path") is not { } path) continue;
            int? position = item.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.Number &&
                p.TryGetInt32(out var index) ? index : null;
            double? score = item.TryGetProperty("relevance_score", out var s) && s.ValueKind == JsonValueKind.Number &&
                s.TryGetDouble(out var value) ? value : null;
            var image = new ChatImage(path, position, StringField(item, "alt_text"), score, StringField(item, "context_text"));
            Dictionary<string, JsonElement>? extra = null;
            foreach (var field in item.EnumerateObject())
            {
                if (field.Name is "path" or "position" or "alt_text" or "relevance_score" or "context_text") continue;
                (extra ??= [])[field.Name] = field.Value.Clone();
            }
            images.Add(image with { AdditionalFields = extra });
        }
        return images;
    }

    private static string? StringField(JsonElement item, string key) =>
        item.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<ChatImage> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

internal sealed class StoredChatMetadataConverter : JsonConverter<ChatMessageMetadata>
{
    public override ChatMessageMetadata? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.Deserialize<ChatMessageMetadata>(options) : null;
    }

    public override void Write(Utf8JsonWriter writer, ChatMessageMetadata value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

internal sealed class ChatCitationsConverter : JsonConverter<IReadOnlyList<Dictionary<string, JsonElement>>>
{
    public override IReadOnlyList<Dictionary<string, JsonElement>> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        return document.RootElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => item.Deserialize<Dictionary<string, JsonElement>>(options)!)
            .ToArray();
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<Dictionary<string, JsonElement>> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
