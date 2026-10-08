using System.Diagnostics.Metrics;

namespace CFC.ChatAI.Rag.Internal;

internal static class PineconeDiagnostics
{
    private static readonly Meter Meter = new("CFC.ChatAI.Rag.Pinecone", "1.0.0");
    internal static readonly Counter<long> InvalidMetadata = Meter.CreateCounter<long>("cfc.pinecone.invalid_metadata");
    internal static readonly Counter<long> Retries = Meter.CreateCounter<long>("cfc.pinecone.retries");
    internal static readonly Histogram<double> OperationLatency = Meter.CreateHistogram<double>("cfc.pinecone.operation.duration", "ms");
}
