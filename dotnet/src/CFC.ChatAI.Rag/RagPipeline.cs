namespace CFC.ChatAI.Rag;

/// <summary>
/// C# port of app/core/rag.py RAGPipeline
/// </summary>

public sealed class RagPipeline
{
    /// <summary> Mirrors settings.MAX_CONTEXT_LENGTH in app/config.py </summary>
    public const int DefaultMaxContextLength = 4000;

    /// <summary> 
    /// Equivalent of Python format_context(): joins chunks into one string for the LLM
    /// Each chunk is prefixed with [CHUNK_ID: ...] so the LLM can cite it
    /// stops at the first chunk that would exceed maxLength (seperators are not counted)
    /// Note: C# string.Length counts UTF-16 units, Python len() counts code points; they only
    /// differ for emoji/rare symbols, so the cutoff can differ in that edge case.
    /// </summary> 
    public static string FormatContext(
        IReadOnlyList<ContextChunk> chunks, int maxLength = DefaultMaxContextLength)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var parts = new List<string>();
        int totalLength = 0;

        foreach (var chunk in chunks)
        {
            string chunkId = chunk.ChunkId ?? "unknown";
            string titleLine = string.IsNullOrEmpty(chunk.SectionTitle)
                ? ""
                : $"Title: {chunk.SectionTitle}\n";
            
            string chunkText = $"[CHUNK_ID: {chunkId}]\nSource: {chunk.Source}\n{titleLine}{chunk.Text}\n";
            
            if (totalLength + chunkText.Length > maxLength)
                break;

            parts.Add(chunkText);
            totalLength += chunkText.Length;
        }
        return string.Join("\n---\n", parts);
    }
}