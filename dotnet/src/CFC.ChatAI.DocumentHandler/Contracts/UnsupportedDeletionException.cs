namespace CFC.ChatAI.DocumentHandler;

/// <summary>Deletion is not supported for this document's source type.</summary>
public sealed class UnsupportedDeletionException : Exception
{
    public UnsupportedDeletionException() : base("Deletion is not supported for this source type.") { }
}
