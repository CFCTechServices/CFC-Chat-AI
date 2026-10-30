using System.Security.Claims;
using System.Text.Encodings.Web;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CFC.ChatAI.Api.Security;

/// <summary>Validates the caller's token and reads their current profile on every request; never trusts JWT role claims.</summary>
public sealed class ApiAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIdentityGateway identity,
    IUserProfileRepository profiles)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var token = header[7..].Trim();
        if (token.Length == 0 || token.Contains(' ') || token.Length > 8192)
            return AuthenticateResult.Fail("Invalid bearer token");

        var user = await identity.GetUserAsync(token, Context.RequestAborted);
        if (user is null || !Guid.TryParse(user.Id, out _))
            return AuthenticateResult.Fail("Invalid bearer token");
        var profile = await profiles.GetCurrentAsync(user.Id, token, Context.RequestAborted);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new("access_token", token) };
        if (profile is { DeletedAt: null, Status: "active" })
        {
            claims.Add(new Claim("active", "true"));
            claims.Add(new Claim(ClaimTypes.Role, profile.Role));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public static class Caller
{
    public static string Id(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static string Token(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("access_token")!;
}
