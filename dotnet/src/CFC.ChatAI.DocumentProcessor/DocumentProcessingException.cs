namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Indicates that a supported document could not be read or contained no extractable text.</summary>
public class DocumentProcessingException : Exception
{
    public DocumentProcessingException(string message)
        : base(message)
    {
    }

    public DocumentProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Indicates that the input file extension is not handled by this processor.</summary>
public sealed class UnsupportedDocumentTypeException : DocumentProcessingException
{
    public UnsupportedDocumentTypeException(string extension)
        : base($"Unsupported document type '{extension}'. Supported types: .pdf, .docx, .doc, .txt, .md, .eml.")
    {
    }
}
