namespace CFC.ChatAI.DocumentHandler;

public sealed class ReplacementConflictException()
    : Exception("Replacement must preserve the document ID and format and target a known document.");

public sealed class ReplacementPartialFailureException(string stage, Exception innerException)
    : Exception("A document replacement may have partially completed.", innerException)
{
    public string Stage { get; } = stage;
}
