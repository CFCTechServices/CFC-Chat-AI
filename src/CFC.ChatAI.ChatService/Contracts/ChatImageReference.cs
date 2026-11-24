namespace CFC.ChatAI.ChatService.Contracts;

/// <summary>An image candidate selected from retrieved context for an assistant answer.</summary>
public sealed record ChatImageReference(
    string Path,
    int? Position,
    string? AltText,
    double? RelevanceScore,
    string? ContextText);
