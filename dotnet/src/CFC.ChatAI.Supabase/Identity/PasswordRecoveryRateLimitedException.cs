namespace CFC.ChatAI.Supabase.Identity;

/// <summary>Provider throttled recovery; no account information is exposed.</summary>
public sealed class PasswordRecoveryRateLimitedException() : Exception("Password recovery rate limited.");
