using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Api.Security;
using CFC.ChatAI.ChatService.Contracts;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Conversations;
using CFC.ChatAI.Supabase.Feedback;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Invitations;
using CFC.ChatAI.Supabase.Profiles;

namespace CFC.ChatAI.Api.Endpoints;

public static class InternalApiEndpoints
{
    private const string ResetMessage = "If an account exists with this email, a password reset link has been sent.";
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static void MapInternalApi(this WebApplication app, bool chatAvailable)
    {
        app.MapGet("/api/auth/config", (IConfiguration config) => Results.Ok(new Dictionary<string, string?>
        {
            ["supabaseUrl"] = config["SUPABASE_URL"],
            ["supabaseKey"] = config["SUPABASE_ANON_KEY"]
        }));

        app.MapPost("/api/auth/check-email", async (EmailRequest request, IInvitationRepository invites,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (!ValidEmail(request.Email)) return BadInput("Invalid email");
            var invite = await invites.GetUnregisteredByEmailAsync(request.Email, ct);
            return Results.Ok(invite is null || invite.IsRegistered == true
                ? new { eligible = false, message = "This email has not been invited. Please contact your administrator." }
                : invite.ExpiresAt is null || invite.ExpiresAt <= clock.GetUtcNow()
                    ? new { eligible = false, message = "Your invitation has expired. Please contact your administrator for a new one." }
                    : new { eligible = true, message = "Email is eligible for registration." });
        }).RequireRateLimiting("public-auth");

        app.MapPost("/api/auth/forgot-password", async (EmailRequest request, IIdentityGateway identity,
            IConfiguration config, CancellationToken ct) =>
        {
            if (!ValidEmail(request.Email)) return BadInput("Invalid email");
            var redirect = config["FRONTEND_BASE_URL"];
            var uri = Uri.TryCreate(redirect, UriKind.Absolute, out var parsed) &&
                parsed.Scheme is "http" or "https" ? parsed : null;
            try { await identity.RequestPasswordRecoveryAsync(request.Email, uri, ct); }
            catch (PasswordRecoveryRateLimitedException)
            {
                return Results.Json(new { success = false, message = "Too many reset attempts. Please wait a few minutes before trying again." }, statusCode: 429);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-enumerating: provider errors must never identify an account.
                return Results.Ok(new { success = true, message = ResetMessage });
            }
            return Results.Ok(new { success = true, message = ResetMessage });
        }).RequireRateLimiting("public-auth");

        var user = app.MapGroup("/api").RequireAuthorization("current-user");
        user.MapGet("/profile/me", async (ClaimsPrincipal principal, IUserProfileRepository profiles, CancellationToken ct) =>
        {
            var profile = await profiles.GetCurrentAsync(principal.Id(), principal.Token(), ct);
            return profile is null ? Results.NotFound(new { detail = "Profile not found" }) : Results.Ok(ProfileBody(profile));
        });

        user.MapGet("/chat/sessions", async (bool detail, ClaimsPrincipal principal, IConversationRepository conversations, CancellationToken ct) =>
        {
            var sessions = new List<object>();
            var pageSize = detail ? 100 : 500;
            for (var offset = 0; offset <= 5000; offset += pageSize)
            {
                var page = await conversations.ListSessionsAsync(principal.Id(), principal.Token(), new PageRequest(offset, pageSize), detail, ct);
                sessions.AddRange(page.Items.Select(session => SessionBody(session, detail)));
                if (!page.HasMore) return Results.Ok(sessions);
            }
            return Results.Json(new { detail = "Too many sessions; pagination is required" }, statusCode: 413);
        });
        user.MapPost("/chat/sessions", async (SessionRequest request, ClaimsPrincipal principal, IConversationRepository conversations, CancellationToken ct) =>
        {
            var title = request.Title ?? "New Chat";
            if (!ValidTitle(title)) return BadInput("Invalid title");
            var session = await conversations.CreateSessionAsync(principal.Id(), principal.Token(), title, ct);
            return session is null ? Results.Problem("Could not create session") : Results.Ok(SessionBody(session, false));
        });
        user.MapGet("/chat/sessions/{sessionId}", async (string sessionId, ClaimsPrincipal principal, IConversationRepository conversations, CancellationToken ct) =>
        {
            if (!ValidId(sessionId)) return BadInput("Invalid session ID");
            if (await conversations.GetSessionAsync(principal.Id(), principal.Token(), sessionId, ct) is null)
                return Results.NotFound(new { detail = "Session not found" });
            var messages = await conversations.GetHistoryAsync(principal.Id(), principal.Token(), sessionId, ct);
            return Results.Ok(messages.Select(m => new { m.Id, m.SessionId, m.Role, m.Content, m.CreatedAt, m.Metadata }));
        });
        user.MapPatch("/chat/sessions/{sessionId}", async (string sessionId, SessionRequest request, ClaimsPrincipal principal, IConversationRepository conversations, CancellationToken ct) =>
        {
            if (!ValidId(sessionId) || !ValidTitle(request.Title)) return BadInput("Invalid session ID or title");
            var session = await conversations.UpdateSessionTitleAsync(principal.Id(), principal.Token(), sessionId, request.Title!, ct);
            return session is null ? Results.NotFound(new { detail = "Session not found" }) : Results.Ok(SessionBody(session, false));
        });
        user.MapDelete("/chat/sessions/{sessionId}", async (string sessionId, ClaimsPrincipal principal, IConversationRepository conversations, CancellationToken ct) =>
        {
            if (!ValidId(sessionId)) return BadInput("Invalid session ID");
            var deleted = await conversations.DeleteSessionAsync(principal.Id(), principal.Token(), sessionId, ct);
            return deleted ? Results.Ok(new { success = true }) : Results.NotFound(new { detail = "Session not found" });
        });

        user.MapGet("/chat/feedback", async (string session_id, ClaimsPrincipal principal,
            IConversationRepository conversations, IFeedbackRepository feedback, CancellationToken ct) =>
        {
            if (!ValidId(session_id)) return BadInput("Invalid session ID");
            if (await conversations.GetSessionAsync(principal.Id(), principal.Token(), session_id, ct) is null)
                return Results.NotFound(new { detail = "Session not found" });
            var messages = await conversations.GetHistoryAsync(principal.Id(), principal.Token(), session_id, ct);
            var scores = new Dictionary<string, int>();
            foreach (var batch in messages.Select(m => m.Id).Chunk(100))
                foreach (var (key, value) in await feedback.GetUserScoresForMessagesAsync(principal.Id(), batch, ct))
                    scores[key] = value;
            return Results.Ok(new { feedback = scores });
        });

        if (chatAvailable)
        {
            user.MapGet("/visibility/vector-store", async (IPineconeService pinecone, IConfiguration config, CancellationToken ct) =>
            {
                var stats = await pinecone.GetIndexStatsAsync(ct);
                return Results.Ok(new
                {
                    success = true,
                    index_name = config["PINECONE_INDEX_NAME"],
                    total_vectors = stats.TotalVectorCount ?? stats.NamespaceVectorCounts.Values.Sum(),
                    stats.Dimension,
                    index_fullness = (double?)null,
                    namespaces = stats.NamespaceVectorCounts.Select(pair => new
                    {
                        name = string.IsNullOrEmpty(pair.Key) ? "default" : pair.Key,
                        vector_count = pair.Value
                    })
                });
            });
            user.MapPost("/chat/feedback", async (FeedbackRequest request, ClaimsPrincipal principal,
                IConversationRepository conversations, IFeedbackRepository feedback, IQueryEmbeddingService embeddings,
                CancellationToken ct) =>
            {
                if (!ValidId(request.SessionId) || !ValidId(request.MessageId) || request.Rating is not (null or -1 or 1))
                    return BadInput("Invalid feedback");
                var context = await conversations.GetFeedbackMessageContextAsync(principal.Id(), principal.Token(), request.MessageId, ct);
                if (context is null || context.SessionId != request.SessionId)
                    return Results.NotFound(new { detail = "Message not found" });
                await feedback.SubmitMessageFeedbackAsync(new FeedbackSubmission(request.MessageId, principal.Id(), request.Rating), ct);
                // Deliberately synchronous: a failed non-transactional event replace must not be reported as success.
                // Re-submitting the same vote repairs it. No provider search is used to obtain an embedding.
                try
                {
                    await feedback.DeleteQueryEventsForMessageAsync(request.MessageId, ct);
                    if (request.Rating is int rating && !string.IsNullOrWhiteSpace(context.PreviousUserMessageContent))
                    {
                        var ids = CitationIds(context.Metadata);
                        if (ids.Count > 0)
                        {
                            var vector = await embeddings.EmbedQueryAsync(context.PreviousUserMessageContent, ct);
                            await feedback.RecordQueryEventsAsync(new QueryFeedbackEventSet(
                                ids.Select(id => new QueryFeedbackEvent(id, request.MessageId, rating)).ToArray(), vector), ct);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Problem("Vote saved but query events may be incomplete; retry the same vote.", statusCode: 500);
                }
                return Results.Ok(new { success = true, score = request.Rating });
            });
            user.MapPost("/chat/message", async (MessageRequest request, ClaimsPrincipal principal, IChatService chat, CancellationToken ct) =>
            {
                if (!ValidId(request.SessionId) || string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 16000)
                    return BadInput("Invalid session ID or content");
                try
                {
                    var answer = await chat.SendMessageAsync(principal.Id(), principal.Token(), request.SessionId, request.Content, ct);
                    return Results.Ok(new { answer.Id, answer.Role, answer.Content, answer.Citations, answer.RelevantImages, answer.CreatedAt });
                }
                catch (ChatSessionNotFoundException) { return Results.NotFound(new { detail = "Session not found" }); }
            });
        }

        var admin = app.MapGroup("/api/admin").RequireAuthorization("admin");
        admin.MapPost("/invite", async (EmailRequest request, ClaimsPrincipal principal, IInvitationRepository invites,
            IAdminProfileRepository profiles, TimeProvider clock, CancellationToken ct) =>
        {
            if (!ValidEmail(request.Email)) return BadInput("Invalid email");
            if (await profiles.GetByEmailAsync(request.Email, ct) is not null)
                return Results.Conflict(new { detail = "This user already exists in the system." });
            var existing = await invites.GetUnregisteredByEmailAsync(request.Email, ct);
            if (existing is not null)
            {
                if (existing.ExpiresAt is null || existing.ExpiresAt > clock.GetUtcNow())
                    return Results.Conflict(new { detail = "There is already a pending invitation for this email." });
                await invites.DeleteByIdAsync(existing.Id, ct);
            }
            try
            {
                var invitation = await invites.InsertAsync(new CreateInvitation(request.Email, principal.Id()), ct);
                return Results.Ok(new { message = $"Invitation created for {request.Email}", request.Email, invitation.ExpiresAt });
            }
            catch (InvitationConflictException) { return Results.Conflict(new { detail = "An active invitation already exists for this email." }); }
        });
        admin.MapGet("/invitations/status/{email}", async (string email, IInvitationRepository invites, TimeProvider clock, CancellationToken ct) =>
        {
            if (!ValidEmail(email)) return BadInput("Invalid email");
            var invitation = await invites.GetLatestByEmailAsync(email, ct);
            var status = invitation is null ? "none" : invitation.IsRegistered == true ? "registered" :
                invitation.ExpiresAt is not null && invitation.ExpiresAt > clock.GetUtcNow() ? "active" : "expired";
            return Results.Ok(new { email, status, expires_at = invitation?.ExpiresAt });
        });
    }

    private static IReadOnlyList<string> CitationIds(JsonElement? metadata)
    {
        if (metadata is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("citations", out var citations) || citations.ValueKind != JsonValueKind.Array)
            return [];
        return citations.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("chunk_id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(c => c.GetProperty("chunk_id").GetString()!)
            .Where(id => Guid.TryParse(id, out _)).Distinct().ToArray();
    }

    private static object ProfileBody(Profile p) => new { p.Id, p.Email, p.FullName, p.AvatarUrl, p.Role, p.Status, p.CreatedAt };
    private static object SessionBody(ChatSession s, bool detail) => detail
        ? new { s.Id, s.UserId, s.Title, s.CreatedAt, message_count = s.MessageCount ?? 0, last_message = s.LastUserMessagePreview }
        : new { s.Id, s.UserId, s.Title, s.CreatedAt };
    private static bool ValidId(string? value) => Guid.TryParse(value, out _);
    private static bool ValidEmail(string? value) => value is { Length: <= 254 } && EmailValidator.IsValid(value);
    private static bool ValidTitle(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    private static IResult BadInput(string message) => Results.BadRequest(new { detail = message });

    public sealed record EmailRequest(string Email);
    public sealed record SessionRequest(string? Title);
    public sealed record MessageRequest(string SessionId, string Content);
    public sealed record FeedbackRequest(string MessageId, string SessionId, int? Rating);
}
