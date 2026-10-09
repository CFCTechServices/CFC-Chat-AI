using CFC.ChatAI.Api.Application.Services;
using CFC.ChatAI.Api.Contracts;

namespace CFC.ChatAI.Api.Endpoints;

public static class AuthSelfServiceEndpoints
{
    public static void MapAuthSelfService(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();
        group.MapPost("/forgot-password", async (HttpRequest request, RecoveryService service, CancellationToken ct) =>
        {
            var validated = await RequestValidation.ReadAsync(request, ["email"], true, ct);
            if (validated.Error is not null) return validated.Error;
            return Results.Ok(await service.RequestAsync(validated.Body!.Value.GetProperty("email").GetString()!, ct));
        }).RequireRateLimiting("Recovery");
        group.MapPost("/check-email", async (HttpRequest request, EligibilityService service, CancellationToken ct) =>
        {
            var validated = await RequestValidation.ReadAsync(request, ["email"], true, ct);
            if (validated.Error is not null) return validated.Error;
            var email = RequestValidation.InvitationEmail(validated.Body!.Value.GetProperty("email").GetString()!);
            if (email.Error is not null) return email.Error;
            return Results.Ok(await service.CheckAsync(email.Email!, ct));
        }).RequireRateLimiting("Eligibility");
    }
}
