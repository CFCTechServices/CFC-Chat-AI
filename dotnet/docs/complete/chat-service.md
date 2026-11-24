# ChatService

**Project:** `src/CFC.ChatAI.ChatService`  
**Responsibility:** the application workflow behind the authenticated `POST /api/chat/message` operation.

`IChatService` combines conversation persistence, feedback-aware retrieval, context formatting, Azure OpenAI generation, and answer metadata. It calls the existing `IFeedbackService` and `IConversationRepository` contracts; it does not call Pinecone or Supabase transports directly and does not call `DocumentProcessor` or `VideoProcessor`.

## Workflow

`SendMessageAsync(userId, accessToken, sessionId, content, cancellationToken)` performs these steps:

1. Validates the authenticated user/session inputs and inserts the user message using the supplied caller access token. A missing or non-owned session raises `ChatSessionNotFoundException`.
2. Loads the most recent conversation messages and uses prior user/assistant entries as model history. The newly inserted question is not duplicated in history; it is sent once with the current RAG prompt.
3. Calls `IFeedbackService.SearchAsync` with the question and `DefaultTopK`. Feedback handles Pinecone search, Supabase chunk hydration, and fail-open reranking/hydration. A Pinecone retrieval failure propagates.
4. Builds ranked citations and a bounded context prompt. It offers up to `MaxImages` images from sufficiently relevant chunks and validates image markers in the model answer against those candidates.
5. Calls Azure OpenAI when configured. It strips `[IMAGE: ...]` and `[CHUNKS_CITED: ...]` markers from user-visible answer text and returns only verified image references. If Azure OpenAI is not configured or completion fails, it uses a concise summary of the retrieved text; cancellation is never swallowed.
6. Inserts the assistant message. Its Supabase metadata contains snake-case `citations` and `relevant_images`, so a session reload can reconstruct the same context and images. The returned `ChatMessageResult` contains the saved message ID and timestamp.

The chat prompt instructs the model to ground company-specific statements in retrieved text, treat retrieved content as untrusted reference material, and cite chunk IDs. Citations in the response represent the retrieved/reranked context; image references are separately validated against image paths from that context.

## Registration and use

Register Supabase and Feedback before ChatService. Feedback itself requires Pinecone and Supabase to be registered. Configure the Azure resource endpoint, API key, and **deployment name** together; endpoint/key/deployment are an all-or-none set.

```csharp
using CFC.ChatAI.ChatService;
using CFC.ChatAI.ChatService.Contracts;
using CFC.ChatAI.Feedback;
using CFC.ChatAI.Pinecone;
using CFC.ChatAI.Supabase;

services
    .AddPineconeInfrastructure(options =>
    {
        options.ApiKey = configuration["Pinecone:ApiKey"]!;
        options.IndexName = configuration["Pinecone:IndexName"]!;
        options.ModelPath = configuration["Embeddings:ModelPath"]!;
        options.VocabularyPath = configuration["Embeddings:VocabularyPath"]!;
    })
    .AddSupabaseInfrastructure(options =>
    {
        options.Url = configuration["Supabase:Url"]!;
        options.AnonKey = configuration["Supabase:AnonKey"]!;
        options.ServiceRoleKey = configuration["Supabase:ServiceRoleKey"]!;
    })
    .AddFeedback()
    .AddChatService(options =>
    {
        options.AzureOpenAIEndpoint = configuration["AzureOpenAI:Endpoint"]!;
        options.AzureOpenAIApiKey = configuration["AzureOpenAI:ApiKey"]!;
        options.AzureOpenAIDeploymentName = configuration["AzureOpenAI:DeploymentName"]!;
        options.DefaultTopK = 5;
    });
```

The application host obtains `userId` and `accessToken` from its authenticated principal, not from the JSON request body:

```csharp
ChatMessageResult result = await chatService.SendMessageAsync(
    currentUser.Id,
    currentUser.AccessToken,
    request.SessionId,
    request.Content,
    cancellationToken);
```

Keep API keys in a secret store or environment configuration. If all Azure OpenAI options are left empty, the service remains usable in fallback mode and summarizes retrieved text. Defaults mirror the existing Python workflow: five retrieval results, ten recent messages (including the current user message), 4,000 context characters, a 30-second Azure OpenAI network-operation timeout, temperature `0.2`, 500 output tokens, and at most three image candidates with adjusted score at least `0.3`.

The Azure adapter uses the official `Azure.AI.OpenAI` 2.0.0 package from the Azure .NET SDK repository (`AzureOpenAIClient.GetChatClient(...).CompleteChatAsync(...)`). The deployment name is used as the model identifier by that client; callers do not depend on SDK request/response types.

## Boundaries and failure semantics

- Conversation reads/writes use the caller's Supabase access token and user ID, preserving the repository's user/RLS scope. The legacy Python `/api/chat/message` currently uses service-role operations with application ownership checks; this .NET behavior is not assumed to be security-equivalent until the deployed RLS policy is verified.
- Supabase message writes and Pinecone retrieval/model calls do not share a transaction. The user message is persisted before retrieval. If retrieval or a required Supabase call fails, that user message can remain without an assistant reply. A failure saving the assistant reply also does not roll back the user message.
- Azure completion failures (other than caller cancellation) fail open to an extractive summary. Retrieval failures are not converted to an answer because the service cannot claim it searched successfully.
- The component does not define HTTP status codes, authenticate requests, manage chat sessions, accept image uploads, or implement the separate chat-feedback endpoints. The API host should map `ChatSessionNotFoundException` to HTTP 404 and map provider failures according to its API error policy.
- Feedback vote persistence and query-feedback event maintenance remain separate Supabase/application workflows; ChatService only consumes feedback-aware search results.
- The API project currently maps health endpoints only. This component is independently registered/tested; route wiring belongs to the `Api` component.

## Verification

From `dotnet/` run:

```sh
dotnet test tests/CFC.ChatAI.ChatService.Tests/CFC.ChatAI.ChatService.Tests.csproj
```

The tests use fake conversation, retrieval, and completion contracts. They verify owner-scoped inputs, prompt/history assembly, marker filtering, metadata serialization, missing sessions, fallback behavior, and retrieval failure semantics. They do not require Azure OpenAI, Pinecone, or Supabase credentials.
