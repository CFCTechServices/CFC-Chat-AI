using CFC.ChatAI.Api.Application.Models;
using CFC.ChatAI.Api.Application.Services;
using CFC.ChatAI.Api.Authentication;
using CFC.ChatAI.Api.Contracts;

namespace CFC.ChatAI.Api.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfile(this WebApplication app)
    {
        var group = app.MapGroup("/api/profile").RequireAuthorization("ActiveAccount");
        group.MapGet("/me", async (ProfileService service, CurrentUser current, CancellationToken ct) =>
        {
            var result = await service.ReadAsync(current.Get(), ct);
            return result.Outcome switch
            {
                Outcome.Success => Results.Ok(result.Value),
                Outcome.NotFound => RequestValidation.Detail(404, "Profile not found"),
                _ => RequestValidation.Detail(500, "Failed to get profile")
            };
        });
        group.MapPatch("/me", UpdateAsync);
        group.MapPost("/complete", UpdateAsync);
    }
    private static async Task<IResult> UpdateAsync(HttpRequest request, ProfileService service,
        CurrentUser current, CancellationToken ct)
    {
        var validated = await RequestValidation.ReadAsync(request, ["full_name", "avatar_url"], false, ct);
        if (validated.Error is not null) return validated.Error;
        var body = validated.Body!.Value;
        var patch = new ProfilePatch(RequestValidation.Optional(body, "full_name"), RequestValidation.Optional(body, "avatar_url"));
        if (patch.IsEmpty) return RequestValidation.Detail(400, "No fields to update. Provide full_name or avatar_url.");
        var result = await service.UpdateAsync(current.Get(), patch, ct);
        return result.Outcome == Outcome.Success ? Results.Ok(result.Value) : RequestValidation.Detail(500, "Failed to update profile");
    }
}
