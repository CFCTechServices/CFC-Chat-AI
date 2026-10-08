using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag.Internal;

internal interface IEmbeddingEncoder
{
    Task<float[]> EncodeAsync(string text, CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

internal sealed class OnnxEmbeddingEncoder(EmbeddingOptions options) : IEmbeddingEncoder, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OnnxEmbeddingModel? _model;

    public async Task<float[]> EncodeAsync(string text, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // No per-chunk Task.Run. Cancellation cannot interrupt the synchronous ONNX call.
            _model ??= new OnnxEmbeddingModel(options.ModelPath, options.VocabularyPath);
            var vector = _model.EncodeQuery(text);
            cancellationToken.ThrowIfCancellationRequested();
            EmbeddingValidation.Validate(vector);
            return vector;
        }
        catch (OperationCanceledException) { throw; }
        catch (PineconeServiceException) { throw; }
        catch { throw new PineconeServiceException(PineconeErrorCategory.Configuration, "Local embedding model could not run; verify the configured model/vocabulary assets."); }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try { await EncodeAsync("readiness", cancellationToken); return true; }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public void Dispose() { _model?.Dispose(); _gate.Dispose(); }
}

internal static class EmbeddingValidation
{
    internal static void Validate(float[] values)
    {
        var norm = values.Sum(x => (double)x * x);
        if (values.Length != OnnxEmbeddingModel.Dimension || values.Any(x => !float.IsFinite(x)) ||
            !double.IsFinite(norm) || Math.Abs(norm - 1) > 0.01)
            throw new PineconeServiceException(PineconeErrorCategory.IncompatibleIndex, "Embedding must contain 384 finite, approximately unit-length values.");
    }
}
