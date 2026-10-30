namespace CFC.ChatAI.Supabase.Common;

/// <summary>A bounded offset-based page request for provider-backed collections.</summary>
public sealed record PageRequest
{
    public const int MaximumPageSize = 1000;

    public PageRequest(int offset = 0, int limit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaximumPageSize);
        Offset = offset;
        Limit = limit;
    }

    public int Offset { get; }
    public int Limit { get; }
}
