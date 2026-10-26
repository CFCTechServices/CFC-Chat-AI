using System.Globalization;

namespace CFC.ChatAI.Rag;

/// <summary>
/// Feedback re-ranking settings. Same environment variable names and defaults as app/config.py
/// </summary>

public sealed record FeedbackSettings
{
    /// <summary>FEEDBACK_ENABLED: false turns all feedback re-ranking off.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>FEEDBACK_ALPHA: max boost/penalty from global votes (0.3 = ±30%).</summary>
    public double Alpha { get; init; } = 0.3;

    /// <summary>FEEDBACK_SCALE: how quickly the global signal saturates.</summary>
    public double Scale { get; init; } = 5.0;

    /// <summary>FEEDBACK_ALPHA_QUERY: max boost/penalty from votes on similar past questions (0.15 = ±15%).</summary>
    public double AlphaQuery { get; init; } = 0.15;

    /// <summary>FEEDBACK_SCALE_QUERY: how quickly the query-aware signal saturates.</summary>
    public double ScaleQuery { get; init; } = 3.0;

    /// <summary>FEEDBACK_SIM_THRESHOLD: how similar a past question must be for its votes to count.</summary>
    public double SimThreshold { get; init; } = 0.75;

    public static FeedbackSettings FromEnvironment(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        var defaults = new FeedbackSettings();

        double Number(string key, double fallback)
        {
            string? value = get(key);
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return new FeedbackSettings
        {
            // Python: os.getenv("FEEDBACK_ENABLED", "true").lower() in ("true", "1", "yes")
            Enabled = (get("FEEDBACK_ENABLED") ?? "true").Trim().ToLowerInvariant() is "true" or "1" or "yes",
            Alpha = Number("FEEDBACK_ALPHA", defaults.Alpha),
            Scale = Number("FEEDBACK_SCALE", defaults.Scale),
            AlphaQuery = Number("FEEDBACK_ALPHA_QUERY", defaults.AlphaQuery),
            ScaleQuery = Number("FEEDBACK_SCALE_QUERY", defaults.ScaleQuery),
            SimThreshold = Number("FEEDBACK_SIM_THRESHOLD", defaults.SimThreshold),
        };
    }
}