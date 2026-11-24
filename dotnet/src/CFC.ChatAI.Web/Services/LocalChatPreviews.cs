using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services;

// One verified account in one interactive browser circuit. Only blob handles cross the circuit.
public sealed record LocalImage(string Url, string Name, long Size)
{
    public AnswerImage Thumbnail => new(new Uri(Url), "Local preview of " + Name, 0);
}
public sealed record ImageSelection(LocalImage[] Images, string? Error);

public sealed class LocalChatPreviews(IJSRuntime js) : IAsyncDisposable
{
    public const int MaxImages = 4;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const long TabBudget = 32 * 1024 * 1024;
    private const int TabImageLimit = 64;
    private readonly Dictionary<string, List<LocalImage>> drafts = [];
    private readonly Dictionary<(string Thread, string Anchor, string Text), LocalImage[]> sent = [];
    private readonly Dictionary<(string Thread, string Anchor, string Text), string?> previousMessages = [];
    private bool disposed;

    public IReadOnlyList<LocalImage> Draft(string? thread) =>
        drafts.TryGetValue(thread ?? "", out var images) ? images : [];

    public async Task<string?> SelectAsync(string? thread, ElementReference input)
    {
        if (disposed) return null;
        var key = thread ?? "";
        if (!drafts.TryGetValue(key, out var draft)) drafts[key] = draft = [];
        ImageSelection selection;
        try
        {
            selection = await js.InvokeAsync<ImageSelection>("cfcChat.selectImages", input,
                Math.Min(MaxImages - draft.Count, TabImageLimit - AllImages().Count()),
                MaxFileBytes, TabBudget - BytesUsed());
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
            return "The browser could not create these previews. Select the files again.";
        }
        if (disposed || !drafts.TryGetValue(key, out var current) || current != draft ||
            draft.Count + selection.Images.Length > MaxImages || AllImages().Count() + selection.Images.Length > TabImageLimit ||
            selection.Images.Any(i => !Uri.TryCreate(i.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != "blob" || i.Size is <= 0 or > MaxFileBytes) ||
            selection.Images.Sum(i => i.Size) > TabBudget - BytesUsed())
        {
            await ReleaseAsync(selection.Images);
            return "The selection changed or the preview limit was reached.";
        }
        draft.AddRange(selection.Images);
        return selection.Error;
    }

    public async Task RemoveAsync(string? thread, LocalImage image)
    {
        if (drafts.TryGetValue(thread ?? "", out var draft) && draft.Remove(image))
            await ReleaseAsync([image]);
    }

    internal Task DiscardUnconfirmedAsync(string thread, LocalImage[] snapshot) =>
        drafts.TryGetValue(thread, out var draft) ? ReleaseAsync(snapshot.Where(draft.Remove)) : Task.CompletedTask;

    internal void MoveFirstDraft(string sessionId)
    {
        if (Draft(sessionId).Count == 0 && drafts.Remove("", out var images)) drafts[sessionId] = images;
    }

    internal void Confirm(string thread, string text, string assistantId, IReadOnlyList<LocalImage> snapshot, string? previousId)
    {
        if (disposed || snapshot.Count == 0 || !drafts.TryGetValue(thread, out var draft)) return;
        var images = snapshot.Where(draft.Remove).ToArray();
        if (images.Length == 0) return;
        var key = (thread, "reply:" + assistantId, text);
        sent[key] = images;
        previousMessages[key] = previousId;
    }

    public IReadOnlyList<LocalImage> ForMessage(string thread, ChatLine line)
    {
        if (line.Role != "user") return [];
        var anchor = line.Id is not null ? "user:" + line.Id : "reply:" + line.FollowingAssistantId;
        return sent.TryGetValue((thread, anchor, line.Content), out var images) ? images : [];
    }

    internal async Task BindHistoryAsync(string thread, List<ChatLine> messages)
    {
        // A POST identifies only the assistant. Two tabs may submit identical text concurrently.
        // Bind a saved user ID only when a single candidate before that reply agrees with the receipt.
        var ambiguous = new List<LocalImage>();
        foreach (var (key, previousId) in previousMessages.Where(pair => pair.Key.Thread == thread).ToArray())
        {
            var start = previousId is null ? 0 : messages.FindIndex(m => m.Id == previousId) + 1;
            if (previousId is not null && start == 0) continue;
            var replyId = key.Anchor["reply:".Length..];
            var reply = messages.FindIndex(start, m => m.IsSavedAssistant && m.Id == replyId);
            if (reply <= start) continue;
            var user = messages[reply - 1];
            if (user.Role == "user" && user.Id is not null && user.Content == key.Text &&
                messages.Skip(start).Take(reply - start).Count(m => m.Role == "user" && m.Content == key.Text) == 1)
                sent[(thread, "user:" + user.Id, key.Text)] = sent[key];
            else
                ambiguous.AddRange(sent[key]);
            sent.Remove(key);
            previousMessages.Remove(key);
        }
        await ReleaseAsync(ambiguous);
    }

    internal async Task ForgetAsync(string thread)
    {
        List<LocalImage> removed = drafts.Remove(thread, out var draft) ? draft : [];
        foreach (var key in sent.Keys.Where(k => k.Thread == thread).ToArray())
        {
            removed.AddRange(sent[key]);
            sent.Remove(key);
            previousMessages.Remove(key);
        }
        await ReleaseAsync(removed);
    }

    public Task ClearDraftsAsync()
    {
        var unsent = drafts.Values.SelectMany(images => images).ToArray();
        drafts.Clear();
        return ReleaseAsync(unsent);
    }

    private IEnumerable<LocalImage> AllImages() =>
        drafts.Values.SelectMany(i => i).Concat(sent.Values.SelectMany(i => i));
    private long BytesUsed() => AllImages().Sum(i => i.Size);

    private async Task ReleaseAsync(IEnumerable<LocalImage> images)
    {
        var urls = images.Select(i => i.Url).ToArray();
        if (urls.Length == 0) return;
        try { await js.InvokeVoidAsync("cfcChat.releaseImages", (object)urls); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        var all = AllImages().ToArray();
        drafts.Clear();
        sent.Clear();
        previousMessages.Clear();
        await ReleaseAsync(all);
    }
}
