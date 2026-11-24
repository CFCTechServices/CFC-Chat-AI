namespace CFC.ChatAI.Api.Endpoints;

// Bounded, process-local exclusion; a collision is reported as busy rather than queued.
internal sealed class MutationSlots
{
    private readonly int[] _slots = new int[256];

    public IDisposable? TryEnter(string key)
    {
        var index = (int)((uint)StringComparer.Ordinal.GetHashCode(key) % _slots.Length);
        return Interlocked.CompareExchange(ref _slots[index], 1, 0) == 0 ? new Lease(_slots, index) : null;
    }

    private sealed class Lease(int[] slots, int index) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref slots[index], 0);
    }
}

internal sealed class DocumentMutations
{
    public MutationSlots Slots { get; } = new();
}
