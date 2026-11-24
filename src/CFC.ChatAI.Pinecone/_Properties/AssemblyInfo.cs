using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CFC.ChatAI.Pinecone.Tests")]
[assembly: InternalsVisibleTo("CFC.ChatAI.Rag.Harness")]
// The offline upload parity harness uses the existing internal embedding/vector seams.
[assembly: InternalsVisibleTo("CFC.ChatAI.UploadParity.Runner")]
[assembly: InternalsVisibleTo("CFC.ChatAI.Rag.VectorStoreCheck")]
[assembly: InternalsVisibleTo("CFC.ChatAI.Rag.PipelineCheck")]
