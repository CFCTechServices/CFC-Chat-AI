using System.Net;
using System.Text.Json;

namespace CFC.ChatAI.Web.Services.Api;

public sealed class ChatApiException(HttpStatusCode statusCode, JsonElement? detail)
    : HttpRequestException(Describe(statusCode, detail), inner: null, statusCode: statusCode)
{
    public JsonElement? Detail { get; } = detail;

    private static string Describe(HttpStatusCode statusCode, JsonElement? detail)
    {
        if (detail is { ValueKind: JsonValueKind.String } message)
            return message.GetString()!;

        if (detail is { ValueKind: JsonValueKind.Array } errors)
        {
            var messages = new List<string>();
            foreach (var error in errors.EnumerateArray())
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("msg", out var text)
                    && text.ValueKind == JsonValueKind.String)
                    messages.Add(text.GetString()!);
            }
            if (messages.Count > 0) return string.Join("; ", messages);
        }

        return $"The API returned HTTP {(int)statusCode} ({statusCode}).";
    }
}
