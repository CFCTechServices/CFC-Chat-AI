namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Extracts text from a supported document and returns bounded text chunks.</summary>
public interface IDocumentProcessor
{
    Task<DocumentProcessingResult> ProcessAsync(
        DocumentInput document,
        CancellationToken cancellationToken = default);
}
