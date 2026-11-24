using System.Text.Json.Serialization;

namespace CFC.ChatAI.Web.Models;

public sealed record AdminUser(
    string Id, string Email, string Role, string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("full_name")] string? FullName = null,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl = null,
    [property: JsonPropertyName("deleted_at")] string? DeletedAt = null,
    [property: JsonPropertyName("deleted_by")] string? DeletedBy = null,
    [property: JsonPropertyName("last_active")] string? LastActive = null);

public sealed record AdminUserList(IReadOnlyList<AdminUser> Users, int Total);
public sealed record AdminInvitation(string Message, string Email, [property: JsonPropertyName("expires_at")] string ExpiresAt);
public sealed record ChangeRoleRequest([property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("new_role")] string NewRole);
public sealed record ChangeRoleResponse(string Message, [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("new_role")] string NewRole);
public sealed record DeactivateUserResponse(string Message, [property: JsonPropertyName("user_id")] string UserId,
    string Email, string Status, [property: JsonPropertyName("deleted_at")] string DeletedAt);
public sealed record ReactivateUserResponse(string Message, [property: JsonPropertyName("user_id")] string UserId,
    string Email, string Status, [property: JsonPropertyName("restored_at")] string RestoredAt);
public sealed record DeleteUserResponse(string Message, [property: JsonPropertyName("user_id")] string UserId,
    string Email, [property: JsonPropertyName("permanently_deleted")] bool PermanentlyDeleted);

public sealed record AdminDocument([property: JsonPropertyName("doc_id")] string Id, string Source,
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("chunk_count")] int ChunkCount)
{
    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Source) ? Id : Source.Replace('\\', '/').Split('/')[^1];
}
public sealed record AdminDocumentList(IReadOnlyList<AdminDocument> Documents, int Total);
public sealed record DocumentDownload(string Url, string Filename);
public sealed record DocumentDeletion(bool Success, string Message);
public sealed record DocumentReplacement(bool Success, string Message, DocumentIngestion Ingestion);
public sealed record DocumentIngestion(bool Success, [property: JsonPropertyName("doc_id")] string? Id = null);

public sealed record AdminSystemSettings(
    [property: JsonPropertyName("auto_approve_uploads")] bool AutoApproveUploads,
    [property: JsonPropertyName("email_notifications")] bool EmailNotifications,
    [property: JsonPropertyName("maintenance_mode")] bool MaintenanceMode);

public sealed record AdminSettingsPatch(
    [property: JsonPropertyName("auto_approve_uploads"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AutoApproveUploads = null,
    [property: JsonPropertyName("email_notifications"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? EmailNotifications = null,
    [property: JsonPropertyName("maintenance_mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MaintenanceMode = null)
{
    public bool Matches(AdminSystemSettings settings) =>
        (AutoApproveUploads is null || AutoApproveUploads == settings.AutoApproveUploads) &&
        (EmailNotifications is null || EmailNotifications == settings.EmailNotifications) &&
        (MaintenanceMode is null || MaintenanceMode == settings.MaintenanceMode);
}

public sealed record ApiHealthResponse(bool Ok, string Message, string? Version = null);
