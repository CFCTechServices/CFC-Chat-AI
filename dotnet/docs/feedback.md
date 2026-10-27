# Feedback search and reranking

**Project:** `src/CFC.ChatAI.Feedback`  
**Status:** Implemented; covered by unit tests in `tests/CFC.ChatAI.Feedback.Tests`.

## Responsibility

`IFeedbackService` accepts a query and returns Pinecone matches reranked with feedback signals. It coordinates the existing provider interfaces; it does not own Pinecone or Supabase transport, and does not persist votes or feedback events.

A search performs these steps:

1. Search Pinecone, which embeds the query and returns its matches and the exact query vector used.
2. Load matching full chunk records from Supabase `IChunkRepository`.
3. Read global vote scores and query-aware weighted scores from Supabase `IFeedbackRepository`.
4. Apply the bounded feedback formula and return matches in descending adjusted-score order, with a one-based rank and hydrated `Chunk` when available.

The original Pinecone match and score remain available on each result as `SearchMatch`. If a Pinecone vector has no corresponding Supabase row, or Supabase chunk hydration fails, the match is still returned with `Chunk == null`.

## Scoring

For each match, the adjusted score is:

```text
pinecone_score × (1
    + global_alpha × tanh(global_net_score / global_scale)
    + query_alpha  × tanh(query_weighted_score / query_scale))
```

Missing feedback signals count as zero. The defaults mirror the Python implementation:

| Option | Default | Meaning |
|---|---:|---|
| `Enabled` | `true` | Read feedback and apply reranking. |
| `GlobalAlpha` | `0.30` | Maximum absolute global-vote adjustment. |
| `GlobalScale` | `5.0` | Global score saturation scale. |
| `QueryAlpha` | `0.15` | Maximum absolute query-aware adjustment. |
| `QueryScale` | `3.0` | Query-aware score saturation scale. |
| `SimilarityThreshold` | `0.75` | Minimum query-vector cosine similarity for stored events. |

The alphas must be finite, nonnegative, and sum to at most one, keeping the combined multiplier nonnegative. Scales must be finite and positive; the similarity threshold must be in `[-1, 1]`. Set `Enabled = false` to return semantic results in Pinecone order without reading score tables. Chunk hydration still occurs.

## Registration and use

Register Pinecone and Supabase first, then Feedback. Their public interfaces are required by the feedback component:

```csharp
services
    .AddPineconeInfrastructure(options => { /* configure Pinecone */ })
    .AddSupabaseInfrastructure(options => { /* configure Supabase */ })
    .AddFeedback(options =>
    {
        options.Enabled = true;
        options.SimilarityThreshold = 0.75;
    });
```

Inject `IFeedbackService` from `CFC.ChatAI.Feedback.Contracts` and call `SearchAsync(query, topK, filter, cancellationToken)`. Pinecone search errors propagate because there is no useful result without vector retrieval. Feedback-score reads and chunk hydration fail open independently: the search continues with remaining feedback signals or without hydrated chunk data. Cancellation is not swallowed.

## Boundaries and follow-up

- Feedback submission and query-event recording remain Supabase repository operations. The chat workflow should coordinate them and decide whether event recording is best-effort.
- This component does not perform answer generation, prompt assembly, or API endpoint mapping; those belong to ChatService/API layers.
- Feedback weights and threshold are supplied through `FeedbackOptions`; environment/configuration binding is the host's responsibility.
- This project deliberately uses only the Pinecone and Supabase abstractions. See [`pinecone-supabase-both-or-none.md`](pinecone-supabase-both-or-none.md) for endpoint dependency ownership and [`rag/embeddings.md`](rag/embeddings.md) for embedding compatibility details.
