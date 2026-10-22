# Testing the C# migration

## Run the tests
1. Download the model files into `dotnet/models/` (see `docs/migration/embeddings.md` → Setup)
2. From `dotnet/`:
       dotnet test

## How our tests are structured
The migration's main goal is that the C# code behaves **exactly like the Python app**.
So most tests are **parity tests**:

1. A Python script in `tools/` runs the *existing* Python code on sample inputs
   and saves the results to `tests/.../TestData/*.json`. This is the "answer key".
2. A C# test runs the *ported* code on the same inputs and compares against it.

Example: `EmbeddingParityTests.cs` + `tools/make_reference.py` + `TestData/reference_embeddings.json`

Conventions:
- **Fixtures** (`IClassFixture<T>`) load expensive things (models, files) once per test class
- **`[Theory]` + `[MemberData]`** gives one test case per input, so failures name the input
- **Failure messages** include the input and the actual vs. expected value
- **Edge cases** belong in the reference data: empty strings, symbols, accents, very long text

## Adding tests for a newly ported component
1. Write a Python script in `tools/` that records the Python component's outputs
2. Save them to `tests/CFC.ChatAI.Pinecone.Tests/TestData/`
3. Write a C# test class that compares the port against that file
4. Document it in the component's `docs/migration/*.md` under "How it's tested"