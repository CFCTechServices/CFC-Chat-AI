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
