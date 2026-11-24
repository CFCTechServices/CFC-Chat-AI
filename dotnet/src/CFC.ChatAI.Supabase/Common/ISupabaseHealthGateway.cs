namespace CFC.ChatAI.Supabase.Common;

/// <summary>Bounded, credential-private provider probe; does not reveal provider errors.</summary>
public interface ISupabaseHealthGateway
{
    Task<bool> CheckAsync(CancellationToken cancellationToken = default);
}
