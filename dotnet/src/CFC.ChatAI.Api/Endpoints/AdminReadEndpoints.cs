using System.Security.Claims;
using System.Text.Json;
using CFC.ChatAI.Api.Security;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Profiles;
using CFC.ChatAI.Supabase.Storage;

namespace CFC.ChatAI.Api.Endpoints;

public static class AdminReadEndpoints
{
    public static void MapAdminReads(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("admin");
        admin.MapGet("/settings", async (AdminSettingsStore store, CancellationToken ct) =>
            Results.Ok(await store.GetAsync(ct)));
        admin.MapPatch("/settings", async (JsonElement patch, AdminSettingsStore store, CancellationToken ct) =>
        {
            if (patch.ValueKind != JsonValueKind.Object || !patch.EnumerateObject().Any())
                return Results.BadRequest(new { detail = "No settings provided" });
            var changes = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var field in patch.EnumerateObject())
            {
                if (field.Name is not ("auto_approve_uploads" or "email_notifications" or "maintenance_mode") ||
                    field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !changes.TryAdd(field.Name, field.Value.GetBoolean()))
                    return Results.BadRequest(new { detail = "Invalid settings field" });
            }
            return Results.Ok(await store.PatchAsync(changes, ct));
        });
        admin.MapGet("/users", async (IAdminProfileRepository repository, CancellationToken ct) =>
        {
            var profiles = new List<Profile>();
            for (var offset = 0; offset <= 5000; offset += 500)
            {
                var page = await repository.ListAsync(new PageRequest(offset, 500), ct);
                profiles.AddRange(page.Items);
                if (!page.HasMore)
                {
                    var users = profiles.OrderBy(p => p.Status == "active" ? 0 : p.Status == "inactive" ? 1 : 2)
                        .ThenByDescending(p => p.CreatedAt)
                        .Select(p => new { p.Id, p.Email, p.FullName, p.AvatarUrl, p.Role, p.Status,
                            p.CreatedAt, p.DeletedAt, p.DeletedBy, last_active = (DateTimeOffset?)null });
                    return Results.Ok(new { users, total = profiles.Count });
                }
            }
            return Results.Json(new { detail = "Too many users; pagination is required" }, statusCode: 413);
        });
        admin.MapPost("/change-role", async (RoleRequest request, ClaimsPrincipal principal,
            IAdminProfileRepository repository, CancellationToken ct) =>
        {
            if (!Guid.TryParse(request.UserId, out _) || request.NewRole is not ("user" or "dev" or "admin"))
                return Results.BadRequest(new { detail = "Invalid user ID or role" });
            if (principal.Id() == request.UserId)
                return Results.BadRequest(new { detail = "Cannot change your own role" });
            var existing = await repository.GetByIdAsync(request.UserId, ct);
            if (existing is null) return Results.NotFound(new { detail = "User not found" });
            if (existing.Role == "superuser")
                return Results.BadRequest(new { detail = "Cannot change the role of a superuser account" });
            var changed = await repository.UpdateRoleAsync(request.UserId, request.NewRole, ct);
            return changed is null ? Results.NotFound(new { detail = "User not found" }) :
                Results.Ok(new { message = $"Successfully changed user role to {request.NewRole}", request.UserId, request.NewRole });
        });
        admin.MapGet("/documents/{docId}/download", async (string docId, IChunkRepository chunks,
            IObjectStorage storage, CancellationToken ct) =>
        {
            if (!ValidDocumentId(docId)) return Results.BadRequest(new { detail = "Invalid document ID" });
            var metadata = await chunks.GetDocumentMetadataAsync(docId, ct);
            if (metadata is null) return Results.NotFound(new { detail = "Document not found" });
            var video = string.Equals(metadata.SourceType, "video", StringComparison.OrdinalIgnoreCase);
            var area = video ? StorageArea.Videos : StorageArea.Documents;
            var prefix = $"{(video ? "videos" : "docs")}/{docId}/original";
            var objects = await storage.ListAsync(new ObjectPrefix(area, prefix), new PageRequest(0, 1000), ct);
            var source = objects.Items.FirstOrDefault(item => !item.IsFolder &&
                item.Key.Path.StartsWith(prefix + "/", StringComparison.Ordinal) &&
                !item.Key.Path[(prefix.Length + 1)..].Contains('/'));
            if (source is null && objects.HasMore)
                return Results.Json(new { detail = "Too many original objects; manual reconciliation required" }, statusCode: 413);
            if (source is null) return Results.NotFound(new { detail = "Original file not found in storage" });
            var signed = await storage.CreateSignedUrlAsync(source.Key, TimeSpan.FromMinutes(5), ct);
            return Results.Ok(new { url = signed.ToString(), filename = Path.GetFileName(source.Key.Path) });
        });
        admin.MapGet("/documents", async (IChunkRepository chunks, CancellationToken ct) =>
        {
            var documents = new List<object>();
            for (var offset = 0; offset <= 5000; offset += 500)
            {
                var page = await chunks.ListDocumentSummariesAsync(new PageRequest(offset, 500), ct);
                documents.AddRange(page.Items.Select(d => (object)new
                {
                    doc_id = d.DocumentId, source = d.Source ?? "", source_type = d.SourceType ?? "document", chunk_count = d.ChunkCount
                }));
                if (!page.HasMore) return Results.Ok(new { documents, total = documents.Count });
            }
            return Results.Json(new { detail = "Too many documents; pagination is required" }, statusCode: 413);
        });
    }

    private static bool ValidDocumentId(string? id) => id is { Length: > 0 and <= 128 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    public sealed record RoleRequest(string UserId, string NewRole);
}
