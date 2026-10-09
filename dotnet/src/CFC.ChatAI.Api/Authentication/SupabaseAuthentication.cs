using System.Security.Claims;
using System.Text.Encodings.Web;
using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace CFC.ChatAI.Api.Authentication;

internal sealed record AccessFailure(int Status, string Detail);
internal sealed class PendingIdentity(VerifiedUser user) { public VerifiedUser User { get; } = user; }

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public VerifiedUser Get() => accessor.HttpContext?.Features.Get<VerifiedUser>()
        ?? throw new InvalidOperationException("ActiveAccount policy must run before accessing the user context.");
}

internal sealed class SupabaseBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IIdentityGateway identity)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "SupabaseBearer";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count is not > 0)
            return AuthenticateResult.NoResult();
        var header = Request.Headers.Authorization.ToString();
        var separator = header.IndexOf(' ');
        if (separator < 0 || !header[..separator].Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || separator == header.Length - 1)
            return Reject(401, "Not authenticated");
        // FastAPI partitions at the first space; Auth, not a decoded JWT, validates the credential.
        var token = header[(separator + 1)..];
        var result = await identity.ValidateAsync(token, Context.RequestAborted);
        if (result.Outcome == Outcome.InvalidToken) return Reject(401, "Invalid token");
        if (result.Outcome != Outcome.Success) return Reject(503, "Authentication service unavailable");
        var user = new VerifiedUser(result.Value!.Id, token);
        Context.Features.Set(new PendingIdentity(user));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
    private AuthenticateResult Reject(int status, string detail)
    {
        Context.Features.Set(new AccessFailure(status, detail));
        return AuthenticateResult.Fail(detail);
    }
}

internal sealed class ActiveAccountRequirement : IAuthorizationRequirement;
internal sealed class ActiveAccountHandler(IProfileAccessReader profiles, IHttpContextAccessor accessor)
    : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        var http = accessor.HttpContext!;
        var pending = http.Features.Get<PendingIdentity>();
        if (pending is null || context.User.Identity?.IsAuthenticated != true) return;
        var result = await profiles.ReadAsync(pending.User.Id, http.RequestAborted);
        var failure = result.Outcome switch
        {
            Outcome.NotFound => new AccessFailure(403, "User profile not found"),
            Outcome.Success => result.Value!.Status switch
            {
                "active" => null,
                "inactive" => new AccessFailure(403, "Account is inactive"),
                "deleted" => new AccessFailure(403, "Account has been deleted"),
                _ => new AccessFailure(403, "Account is not active")
            },
            _ => new AccessFailure(503, "Account verification unavailable")
        };
        if (failure is not null)
        {
            http.Features.Set(failure);
            context.Fail();
            return;
        }
        http.Features.Set(pending.User);
        context.Succeed(requirement);
    }
}

internal sealed class SafeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (result.Succeeded) { await next(context); return; }
        var failure = context.Features.Get<AccessFailure>() ?? new(401, "Not authenticated");
        context.Response.StatusCode = failure.Status;
        if (failure.Status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new { detail = failure.Detail }, context.RequestAborted);
    }
}

public static class SupabaseAuthenticationRegistration
{
    public static IServiceCollection AddSupabaseAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddAuthentication(SupabaseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SupabaseBearerHandler>(SupabaseBearerHandler.SchemeName, _ => { });
        services.AddAuthorization(options => options.AddPolicy("ActiveAccount", policy =>
            policy.AddAuthenticationSchemes(SupabaseBearerHandler.SchemeName).RequireAuthenticatedUser()
                .AddRequirements(new ActiveAccountRequirement())));
        services.AddScoped<IAuthorizationHandler, ActiveAccountHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SafeAuthorizationResultHandler>();
        return services;
    }
}
