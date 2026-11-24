namespace CFC.ChatAI.Feedback;

/// <summary>Controls the bounded global and query-aware feedback adjustments.</summary>
public sealed class FeedbackOptions
{
    /// <summary>When false, search remains semantic-only and does not read feedback scores.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum absolute boost from accumulated chunk votes. Defaults to 30 percent.</summary>
    public double GlobalAlpha { get; set; } = 0.30;

    /// <summary>Global vote count at which the global adjustment approaches its limit.</summary>
    public double GlobalScale { get; set; } = 5.0;

    /// <summary>Maximum absolute boost from query-similar feedback. Defaults to 15 percent.</summary>
    public double QueryAlpha { get; set; } = 0.15;

    /// <summary>Weighted query-feedback score at which the query adjustment approaches its limit.</summary>
    public double QueryScale { get; set; } = 3.0;

    /// <summary>Minimum cosine similarity for a past query-feedback event to contribute.</summary>
    public double SimilarityThreshold { get; set; } = 0.75;

    internal void Validate()
    {
        ValidateAlpha(GlobalAlpha, nameof(GlobalAlpha));
        ValidateAlpha(QueryAlpha, nameof(QueryAlpha));
        if (GlobalAlpha + QueryAlpha > 1)
            throw new ArgumentOutOfRangeException(nameof(QueryAlpha), "The combined feedback alphas must not exceed 1.");
        if (!double.IsFinite(GlobalScale) || GlobalScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(GlobalScale), "The global feedback scale must be finite and positive.");
        if (!double.IsFinite(QueryScale) || QueryScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueryScale), "The query feedback scale must be finite and positive.");
        if (!double.IsFinite(SimilarityThreshold) || SimilarityThreshold is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(SimilarityThreshold), "Cosine similarity threshold must be in [-1, 1].");
    }

    private static void ValidateAlpha(double alpha, string name)
    {
        if (!double.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "Feedback alpha must be finite and in [0, 1].");
    }
}
