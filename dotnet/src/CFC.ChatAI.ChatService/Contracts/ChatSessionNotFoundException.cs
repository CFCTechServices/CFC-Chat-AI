namespace CFC.ChatAI.ChatService.Contracts;

/// <summary>Raised when a chat session does not exist or is not visible to the authenticated user.</summary>
public sealed class ChatSessionNotFoundException : Exception
{
    public ChatSessionNotFoundException()
        : base("The chat session was not found for the authenticated user.")
    {
    }
}
