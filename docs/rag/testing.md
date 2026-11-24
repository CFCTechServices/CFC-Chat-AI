# Testing the .NET RAG and processing components

Run `dotnet test CFC.ChatAI.slnx` from the repository root. The default suite uses synthetic inputs and fake providers; it does not need a live Pinecone index or Supabase project. Download the [embedding assets](embeddings.md#setup) to `models/` to enable model-dependent parity cases. Private processor fixtures and the corresponding reference JSON (if available) stay under the ignored `tests/CFC.ChatAI.DocumentProcessor.Tests/TestData/` directory.

## Parity checks

- `tests/CFC.ChatAI.Pinecone.Tests/TestData/` contains checked-in reference JSON. For example, `EmbeddingParityTests` compares the ONNX output against the Python model's output recorded by `tools/make_reference.py`. Include whitespace, Unicode and long inputs when expanding reference cases.
- `tools/make_document_reference.py`, `tools/make_pipeline_reference.py`, and `tools/make_retrieval_reference.py` use the archived Python implementation and [its dependencies](../../.archive/requirements.txt). Use matching, approved non-production providers for live retrieval comparisons; they are separate from the default tests.
- The [offline upload parity harness](../tests/document-upload-parity-report.md#reproduction-and-verification) exercises checked-in synthetic fixtures across the two implementations without provider credentials.

## DocumentProcessor tests

Run `dotnet test tests/CFC.ChatAI.DocumentProcessor.Tests` from the repository root. Tests cover text, Markdown, PDF, DOCX and email extraction, chunking, validation and DI registration. They do not require LibreOffice for the default synthetic tests; legacy `.doc` conversion depends on the external executable.
