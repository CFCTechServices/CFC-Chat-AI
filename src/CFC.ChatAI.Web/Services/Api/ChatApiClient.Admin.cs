using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services.Api;

public sealed partial class ChatApiClient
{
    private const string AdminUsersPath = "api/admin/users";
    private const string AdminSettingsPath = "api/admin/settings";

    public Task<AdminUserList> GetAdminUsersAsync(string token, CancellationToken ct) =>
        SendAsync<AdminUserList>(HttpMethod.Get, AdminUsersPath, ct, Bearer(token));

    public Task<AdminSystemSettings> GetAdminSettingsAsync(string token, CancellationToken ct) =>
        SendAsync<AdminSystemSettings>(HttpMethod.Get, AdminSettingsPath, ct, Bearer(token));

    public Task<AdminSystemSettings> UpdateAdminSettingsAsync(string token, AdminSettingsPatch patch, CancellationToken ct) =>
        SendAsync<AdminSystemSettings>(HttpMethod.Patch, AdminSettingsPath, ct, Bearer(token), patch);

    public Task<ApiHealthResponse> GetHealthAsync(CancellationToken ct) =>
        SendAsync<ApiHealthResponse>(HttpMethod.Get, "api/health", ct);

    public Task<AdminInvitation> InviteUserAsync(string token, EmailRequest request, CancellationToken ct) =>
        SendAsync<AdminInvitation>(HttpMethod.Post, "api/admin/invite", ct, Bearer(token), request);

    public Task<ChangeRoleResponse> ChangeUserRoleAsync(string token, ChangeRoleRequest request, CancellationToken ct) =>
        SendAsync<ChangeRoleResponse>(HttpMethod.Post, "api/admin/change-role", ct, Bearer(token), request);

    public Task<DeactivateUserResponse> DeactivateUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<DeactivateUserResponse>(HttpMethod.Post, ResourcePath(AdminUsersPath, userId) + "/deactivate", ct, Bearer(token), new { });

    public Task<ReactivateUserResponse> ReactivateUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<ReactivateUserResponse>(HttpMethod.Post, ResourcePath(AdminUsersPath, userId) + "/reactivate", ct, Bearer(token));

    public Task<DeleteUserResponse> DeleteUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<DeleteUserResponse>(HttpMethod.Delete, ResourcePath(AdminUsersPath, userId), ct, Bearer(token));
}
