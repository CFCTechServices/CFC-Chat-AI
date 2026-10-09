using System.Globalization;

namespace CFC.ChatAI.Api.Configuration;

public sealed class SupabaseOptions
{
    public string? Url { get; init; }
    public string? AnonKey { get; init; }
    public string? ServiceRoleKey { get; init; }
    public string FrontendBaseUrl { get; init; } = "http://localhost:8000/ui";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public static SupabaseOptions FromConfiguration(IConfiguration config)
    {
        string? Get(string key, string legacy) => config[key] ?? config[legacy];
        var raw = config["Supabase:RequestTimeoutSeconds"] ?? "5";
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds <= 0 || seconds > 60)
            throw new InvalidOperationException("Supabase timeout must be positive and at most 60 seconds.");
        return new()
        {
            Url = Get("Supabase:Url", "SUPABASE_URL"),
            AnonKey = Get("Supabase:AnonKey", "SUPABASE_ANON_KEY"),
            ServiceRoleKey = Get("Supabase:ServiceRoleKey", "SUPABASE_SERVICE_ROLE_KEY"),
            FrontendBaseUrl = Get("Frontend:BaseUrl", "FRONTEND_BASE_URL") ?? "http://localhost:8000/ui",
            RequestTimeout = TimeSpan.FromSeconds(seconds)
        };
    }
}
