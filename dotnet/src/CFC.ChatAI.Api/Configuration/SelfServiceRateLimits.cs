using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CFC.ChatAI.Api.Configuration;

public static class SelfServiceRateLimits
{
    public static IServiceCollection AddSelfServiceRateLimits(this IServiceCollection services, IConfiguration config)
    {
        int Limit(string key, int fallback)
        {
            var limit = config.GetValue(key, fallback);
            if (limit <= 0 || limit > 10000) throw new InvalidOperationException("Self-service rate limits must be 1–10000.");
            return limit;
        }
        var recovery = Limit("Auth:RecoveryRequestsPerMinute", 10);
        var eligibility = Limit("Auth:EligibilityRequestsPerMinute", 30);
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Too many requests. Please try again later." }, ct);
            };
            // No X-Forwarded-For trust: use the transport peer until trusted proxy configuration is approved.
            void Add(string name, int permits) => options.AddPolicy(name, context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                    new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0, AutoReplenishment = true }));
            Add("Recovery", recovery);
            Add("Eligibility", eligibility);
        });
        return services;
    }
}
