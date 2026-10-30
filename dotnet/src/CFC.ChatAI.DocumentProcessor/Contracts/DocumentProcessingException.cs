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

