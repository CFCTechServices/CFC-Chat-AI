using System.Text.Json;
using CFC.ChatAI.Supabase.Profiles;

namespace CFC.ChatAI.Api.Endpoints;

internal enum AccountAction { Deactivate, Reactivate, Delete }

internal sealed record AccountOperation(Guid Reference, string UserId, string ActorId, AccountAction Action,
    DateTimeOffset StartedAt, string Email, string Role, string Status, DateTimeOffset? DeletedAt, string? DeletedBy,
    bool DeleteRequested = false)
{
    public bool Matches(Profile? profile) => profile is not null && profile.Id == UserId && profile.Role == Role &&
        profile.Status == Status && profile.DeletedAt == DeletedAt && profile.DeletedBy == DeletedBy &&
        string.Equals(profile.Email, Email, StringComparison.OrdinalIgnoreCase);

    public bool IsResult(Profile? profile) => profile is not null && profile.Id == UserId && profile.Role == Role &&
        string.Equals(profile.Email, Email, StringComparison.OrdinalIgnoreCase) &&
        (Action == AccountAction.Deactivate && profile.Status == "inactive" && profile.DeletedAt == StartedAt && profile.DeletedBy == ActorId ||
         Action == AccountAction.Reactivate && profile.Status == "active" && profile.DeletedAt is null && profile.DeletedBy is null);
}

// Single-writer host and a dedicated, persistent, operator-owned local directory. Flush the
// record before provider writes; replace/rename on the same filesystem so retries see whole records.
internal sealed class AccountLifecycleJournal(string? directory, bool enabled, bool permanentDeletionEnabled)
{
    public MutationSlots Slots { get; } = new();
    public bool IsEnabled(AccountAction action) => enabled && (action != AccountAction.Delete || permanentDeletionEnabled);

    public string RequireRoot() => LocalDirectory.Require(directory);

    public bool HasPending(string id) => !string.IsNullOrWhiteSpace(directory) && Read(RequireRoot(), id) is not null;

    public static AccountOperation? Read(string root, string id)
    {
        try
        {
            using var stream = File.OpenRead(PendingPath(root, id));
            return JsonSerializer.Deserialize<AccountOperation>(stream) ?? throw new InvalidDataException("Empty lifecycle journal record.");
        }
        catch (FileNotFoundException) { return null; }
    }

    public static void Save(string root, AccountOperation operation, bool overwrite)
    {
        var temporary = Path.Combine(root, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, operation);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, PendingPath(root, operation.UserId), overwrite);
        }
        finally { File.Delete(temporary); }
    }

    public static void Complete(string root, AccountOperation operation) =>
        File.Move(PendingPath(root, operation.UserId), Path.Combine(root, $"{operation.Reference:N}.audit.json"));

    private static string PendingPath(string root, string id) => Path.Combine(root, $"{Guid.Parse(id):N}.pending.json");
}
