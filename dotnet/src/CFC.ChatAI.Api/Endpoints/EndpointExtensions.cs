namespace CFC.ChatAI.Api.Endpoints;

/// <summary>
/// Aggregator for all Minimal-API route groups.
/// Each new port adds one <c>MapX()</c> method + one line below —
/// that is the steady-state pattern from §6 of
/// <c>local/csharp-api-scaffolding-and-health-swap.md</c>.
/// </summary>
public static class EndpointExtensions
{
    public static void MapAllEndpoints(this WebApplication app)
    {
        app.MapHealth();
        app.MapRuntimeConfiguration();
        app.MapAuthSelfService();
        app.MapProfile();
    }
}
