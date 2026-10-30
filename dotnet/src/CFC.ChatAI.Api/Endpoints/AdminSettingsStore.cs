using System.Text.Json;

namespace CFC.ChatAI.Api.Endpoints;

public sealed record AdminSettings(bool AutoApproveUploads = false, bool EmailNotifications = true, bool MaintenanceMode = false);

/// <summary>Single-instance local state; writes are serialized and renamed atomically on the same volume.</summary>
public sealed class AdminSettingsStore(IConfiguration config, IWebHostEnvironment environment, ILogger<AdminSettingsStore> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly string _path = Path.GetFullPath(config["Api:AdminSettingsPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "admin_settings.json"));

    public async Task<AdminSettings> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await LoadAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task<AdminSettings> PatchAsync(IReadOnlyDictionary<string, bool> changes, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = await LoadAsync(ct, failIfCorrupt: true);
            var updated = new AdminSettings(
                changes.GetValueOrDefault("auto_approve_uploads", current.AutoApproveUploads),
                changes.GetValueOrDefault("email_notifications", current.EmailNotifications),
                changes.GetValueOrDefault("maintenance_mode", current.MaintenanceMode));
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".admin-settings-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(updated, JsonOptions), ct);
                File.Move(temporary, _path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return updated;
        }
        finally { _gate.Release(); }
    }

    private async Task<AdminSettings> LoadAsync(CancellationToken ct, bool failIfCorrupt = false)
    {
        if (!File.Exists(_path)) return new AdminSettings();
        try
        {
            var result = JsonSerializer.Deserialize<AdminSettings>(await File.ReadAllTextAsync(_path, ct), JsonOptions);
            return result ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning("Corrupt admin settings file; using defaults for reads. Repair the file before editing settings.");
            if (failIfCorrupt) throw new InvalidDataException("Admin settings require repair before updates.");
            return new AdminSettings();
        }
    }
}
