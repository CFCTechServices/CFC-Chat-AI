namespace CFC.ChatAI.ChatService.Contracts;

/// <summary>A feedback-reranked retrieved chunk and the source metadata used by the chat response.</summary>
public sealed record ChatCitation(
    int Rank,
    double Score,
    string ChunkId,
    string Text,
    string? Source,
    string SourceType,
    string? DocId,
    string? SectionId,
    string? SectionPath,
    string? SectionTitle,
    IReadOnlyList<string> ImagePaths,
    double? StartSeconds,
    double? EndSeconds,
    string? VideoUrl,
    string? TxtUrl,
    string? SrtUrl,
    string? VttUrl);
