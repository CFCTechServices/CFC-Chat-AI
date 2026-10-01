# embeddings.py → CFC.ChatAI.Rag/OnnxEmbeddingModel.cs + WordPieceTokenizer.cs

**Status:** Ported · Parity-tested · Verified in live app
**Owner:** Logan Loch
**Last edited:** 10/1/26

## What it does
Turns text (user questions and document chunks) into 384-dimension vectors
using the `sentence-transformers/all-MiniLM-L6-v2` model. These vectors are
what Pinecone searches, and what FeedbackService stores for query-aware re-ranking.

## Used by (Python)
- `app/core/rag.py`: embeds the user's query before searching Pinecone
- `app/api/endpoints/ingest.py`: embeds document chunks during ingestion
- `app/api/endpoints/chat.py`: passes query embeddings to FeedbackService
- `app/services/chat_service.py`

## Methods

| Python | C# | Status | Notes |
|---|---|---|---|
| `load_model()` | constructor | Ported | Loads once; register as a singleton in ASP.NET |
| `encode(texts)` | `Encode(IReadOnlyList<string>)` | Ported | Embeds one at a time (no batching yet) |
| `encode_query(query)` | `EncodeQuery(string)` | Ported | |

## Behavior to preserve
- Model: all-MiniLM-L6-v2, 384 dimensions, output L2-normalized
- Pipeline: BERT uncased tokenize → transformer → mean pooling → L2 normalize
- Max 256 tokens including [CLS]/[SEP]; longer text is truncated
- Vectors must match Python's exactly, because existing Pinecone data and the
  `chunk_feedback_events` query embeddings were all produced by the Python model

## Quirks and bugs found
- Python loads the model with `local_files_only=True` first, then falls back
  to downloading from Hugging Face. C# requires `model.onnx` and `vocab.txt`
  to be present in `dotnet/models/`.
- **ASP.NET resolves relative paths against the project's content root**, not the
  folder `dotnet run` was started from. A bare `models/model.onnx` path works in a
  console app but fails in a web app. The model path should come from configuration,
  either absolute or built from `ContentRootPath`.

## Migration decisions
- **Runtime: ONNX Runtime in-process, not a Python microservice.**
  Resolves the embedding-model migration risk in the team contract.
  Evidence: cosine similarity 1.000000 vs. Python on all 13 reference inputs, and
  identical retrieval in the live app (see *Verification in the live app*).
- **Tokenizer: custom port of Hugging Face's BERT tokenizer,
  replacing Microsoft.ML.Tokenizers 2.0.0.** Microsoft's tokenizer:
  - dropped the `°` symbol (`72°F` → cosine 0.947)
  - did not strip accents by default (`Café` → cosine 0.273)

  The custom `WordPieceTokenizer` matches Hugging Face on all test inputs,
  including accents, symbols, emoji, CJK text, whitespace, empty strings,
  and inputs over 256 tokens.
- **Model files are not committed** (90 MB). They are downloaded into
  `dotnet/models/`, which is gitignored.
- **Model license: Apache 2.0** (sentence-transformers/all-MiniLM-L6-v2), which
  permits commercial use.
- **Naming:** projects follow CFC's Programming Standards namespace convention,
  `CFC.ChatAI.<Component>` (Company.Product.Component). The repository is named
  `CFC-Chat-AI`; hyphens are not valid in C# identifiers.

## Verification in the live app (bridge test, 9/30–10/1/26)
To verify the C# embedder against real usage, it was run **inside the existing
Python app**. Code is on branch `migration/embedding-bridge-test`, which is kept
as a record and not merged.

**Setup:** a test-only ASP.NET service (`CFC.Rag.EmbeddingService`) exposed the C#
embedder over HTTP. A switch in `app/core/embeddings.py` (`EMBEDDING_BACKEND`)
made the Python app use it:
- `compare`: Python vectors are used, but C# is called for every embedding and
  the cosine is logged
- `csharp`: the app runs entirely on C# vectors

**Results:**
1. **Compare mode, 13 real questions through the chat UI:** every one
   `PASS cosine=1.000000`, including typos, `DM%`, `°F`, curly quotes, and em dashes.
2. **C# mode:** the full chat flow worked with no Python embedding model loaded.
3. **Scripted side-by-side** (`tools/compare_retrieval.py`, 15 questions, production
   Pinecone index, read-only):
   - Max element difference between Python and C# vectors: ~1e-7 (float rounding)
   - Retrieval identical for 12–13 of 15 questions across two runs
   - Every non-identical result differed **only in the order of exactly tied
     scores**, which Pinecone itself reorders between identical queries
     (observed: the same Python vector queried twice returned tied chunks in
     different orders)
   - Full output: `docs/migration/bridge-test-results.txt` on the bridge branch

**Conclusion:** the C# embedder is a drop-in replacement. Existing Pinecone vectors
are compatible, and no re-embedding is needed.

## Findings outside this component
Found during the bridge test and reported to the team and client:
- **Duplicate chunks in the Pinecone index.** Many chunks score identically to
  6 decimal places, meaning identical text, most likely from duplicate documents
  (e.g., `cfc-horse-ration-89-guide` and `cfc-horse-ration-guide-bak1`). With
  `top_k=5`, duplicates take up slots; one test question received only 2 distinct
  chunks out of 5. Deduplicating the index would improve answer quality with no
  code changes.
- **Azure OpenAI deployment returns 404** (`DeploymentNotFound` for `gpt-4o-mini`)
  in the local environment, so the chatbot falls back to raw chunk summaries.
  Production may be affected if it uses the same deployment.

## Setup
    curl.exe -L -o dotnet\models\model.onnx https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx
    curl.exe -L -o dotnet\models\vocab.txt https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt

## How it's tested
- Reference data: `tests/CFC.ChatAI.Rag.Tests/TestData/reference_embeddings.json`,
  generated by `tools/make_reference.py` from the Python model
- Harness: `dotnet run --project tools/CFC.ChatAI.Rag.Harness` prints cosine per input
- Token debugging: `python tools\debug_tokens.py "any sentence"` prints Python's
  token IDs for comparison with `WordPieceTokenizer`
- Pass threshold: cosine ≥ 0.9999

## Open items
- [ ] Convert the harness check into xUnit tests (`EmbeddingParityTests.cs`)
- [ ] Pin the model download to a specific Hugging Face commit (not `main`);
      before deployment, host a copy CFC controls (e.g., Supabase storage)
- [ ] Argument validation on public methods (CFC standards)
- [ ] Align formatting with CFC Programming Standards (tabs, `ID` casing, constants)
- [ ] Batch embedding with padding for faster ingestion (optimization)
- [ ] Recommend index deduplication to the client (stretch goal)