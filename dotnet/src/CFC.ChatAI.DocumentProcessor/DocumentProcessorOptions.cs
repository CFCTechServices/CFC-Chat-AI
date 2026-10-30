namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Resource limits and external converter settings for document processing.</summary>
public sealed class DocumentProcessorOptions
{
    /// <summary>Maximum accepted input size. Defaults to the application's 50 MiB upload limit.</summary>
    public long MaxDocumentBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Maximum extracted text length in a chunk.</summary>
    public int MaxChunkCharacters { get; set; } = 1200;

    /// <summary>Executable used to convert legacy .doc files to .docx (LibreOffice).</summary>
    public string LibreOfficePath { get; set; } = "soffice";

    /// <summary>Maximum time allowed for legacy Word document conversion.</summary>
    public TimeSpan ConversionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        if (MaxDocumentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDocumentBytes), "The document size limit must be positive.");
        }

        if (MaxChunkCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChunkCharacters), "The chunk size must be positive.");
        }

        if (string.IsNullOrWhiteSpace(LibreOfficePath))
        {
            throw new ArgumentException("A LibreOffice executable path is required.", nameof(LibreOfficePath));
        }

        if (ConversionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConversionTimeout), "The conversion timeout must be positive.");
        }
    }
}
