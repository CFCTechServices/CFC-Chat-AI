namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Indicates that the input file extension is not handled by this processor.</summary>
public sealed class UnsupportedDocumentTypeException : DocumentProcessingException
{
    public UnsupportedDocumentTypeException(string extension)
        : base($"Unsupported document type '{extension}'. Supported types: .pdf, .docx, .doc, .txt, .md, .eml.")
    {
    }
}
