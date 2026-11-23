using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public sealed class ChatLine(string role, string content, string? id = null, string? createdAt = null,
    ChatMessageMetadata? metadata = null)
{
    public string Role { get; } = role;
    public string Content { get; } = content;
    public string? Id { get; } = id;
    public string? CreatedAt { get; } = createdAt;
    public ChatMessageMetadata? Metadata { get; } = metadata;
    public string? Feedback { get; set; }
}

public enum ChatActivity { Idle, Loading, Sending, UpdatingTitle, Deleting }

public sealed class Conversation(ChatSession session)
{
    public string Id { get; } = session.Id;
    public string Title { get; internal set; } = session.Title ?? "New Chat";
    public List<ChatLine> Messages { get; } = [];
    public ChatActivity Activity { get; internal set; }
    public bool HistoryLoaded { get; internal set; }
    public string Draft { get; set; } = "";
    public string? PendingContent { get; internal set; }
    public string? Notice { get; internal set; }
    public string? PendingTitle { get; internal set; }
    public string? TitleError { get; internal set; }
    internal UnconfirmedSend? Unconfirmed { get; set; }
}

internal sealed record UnconfirmedSend(string Content, string? PreviousId, bool FirstMessage);

// Owned by one chat page and bound to the verified account that opened it.
public sealed class ChatWorkspace(FastApiClient api, IChatSessionAccess access, string ownerId) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private string firstDraft = "";
    public List<Conversation> Conversations { get; } = [];
    public Conversation? Active { get; private set; }
    public bool ListReady { get; private set; }
    public bool LoadingList { get; private set; }
    public bool Creating { get; private set; }
    public string? Notice { get; private set; }
    public event Action? Changed;
    public bool CanSend => !disposed && ListReady && !LoadingList && !Creating &&
        (Active is null || (Active.Activity == ChatActivity.Idle && Active.HistoryLoaded));
    public string Draft
    {
        get => Active?.Draft ?? firstDraft;
        set { if (Active is null) firstDraft = value; else Active.Draft = value; }
    }

    public async Task LoadAsync(string? preferredId)
    {
        if (disposed || LoadingList || Creating) return;
        LoadingList = true;
        ListReady = false;
        Notice = null;
        Notify();
        try
        {
            var rows = await RequestAsync((token, ct) => api.GetSessionsAsync(token, cancellationToken: ct),
                "The conversation list could not be loaded.", message => Notice = message);
            if (rows is null) return;
            var previous = Conversations.ToDictionary(c => c.Id);
            Conversations.Clear();
            foreach (var row in rows.Where(row => row.UserId == ownerId))
            {
                var thread = previous.GetValueOrDefault(row.Id) ?? new Conversation(row);
                thread.Title = row.Title ?? "New Chat";
                Conversations.Add(thread);
            }
            Active = Conversations.Find(c => c.Id == preferredId) ?? Conversations.FirstOrDefault();
            if (Active is not null && firstDraft.Length > 0 && Active.Draft.Length == 0)
            {
                Active.Draft = firstDraft;
                firstDraft = "";
            }
            ListReady = true;
            if (Active is not null) await SelectAsync(Active);
        }
        finally { LoadingList = false; Notify(); }
    }

    public async Task SelectAsync(Conversation thread)
    {
        if (disposed || !ListReady || Creating || !Conversations.Contains(thread)) return;
        Active = thread;
        Notify();
        if (thread.Activity != ChatActivity.Idle) return;
        thread.Activity = ChatActivity.Loading;
        Notify();
        try
        {
            var recovering = thread.Unconfirmed is not null;
            if (await ReadHistoryAsync(thread) && recovering && thread.Unconfirmed is null && thread.PendingTitle is not null)
                await SaveTitleAsync(thread);
        }
        finally { thread.Activity = ChatActivity.Idle; Notify(); }
    }

    public Task<Conversation?> NewAsync() => CreateAsync(forFirstSend: false);

    private async Task<Conversation?> CreateAsync(bool forFirstSend)
    {
        if (disposed || !ListReady || Creating || LoadingList) return null;
        Creating = true;
        Notice = null;
        Notify();
        try
        {
            var row = await RequestAsync((token, ct) => api.CreateSessionAsync(token, new(), ct),
                "Creation could not be confirmed. Reload the list before creating another conversation.", message => Notice = message);
            if (row is null || row.UserId != ownerId)
            {
                ListReady = false;
                Notice ??= "Reload the conversation list to verify the new chat.";
                return null;
            }
            var thread = new Conversation(row)
            {
                HistoryLoaded = true,
                Activity = forFirstSend ? ChatActivity.Sending : ChatActivity.Idle,
                Draft = Active is null ? firstDraft : ""
            };
            firstDraft = "";
            Conversations.Insert(0, thread);
            Active = thread;
            return thread;
        }
        finally { Creating = false; Notify(); }
    }

    public async Task SendAsync(string input)
    {
        var content = input.Trim();
        if (!CanSend || content.Length == 0) return;
        var thread = Active ?? await CreateAsync(forFirstSend: true);
        if (thread is null || disposed) return;
        thread.Activity = ChatActivity.Sending;
        try
        {
            // A previous timeout may have happened before the user row became visible.
            var previous = thread.Unconfirmed;
            if (previous is not null)
            {
                if (!await ReadHistoryAsync(thread)) return;
                if (thread.Unconfirmed is null && previous.Content == content)
                {
                    if (thread.PendingTitle is not null) await SaveTitleAsync(thread);
                    return;
                }
            }
            var attempt = new UnconfirmedSend(content, thread.Messages.LastOrDefault()?.Id, thread.Messages.Count == 0);
            thread.Unconfirmed = attempt;
            thread.PendingContent = content;
            thread.Draft = "";
            thread.Notice = null;
            Notify();
            var reply = await RequestAsync((token, ct) => api.SendMessageAsync(token, new(thread.Id, content), ct),
                "The send could not be confirmed.", message => thread.Notice = message);
            thread.PendingContent = null;
            if (disposed) return;
            if (reply is null)
            {
                var sendError = thread.Notice;
                thread.Draft = content;
                await ReadHistoryAsync(thread);
                thread.Notice = $"{sendError} {thread.Notice}".Trim();
            }
            else
            {
                thread.Unconfirmed = null;
                // The send endpoint returns only the assistant row. History supplies the user row ID.
                thread.Messages.Add(new ChatLine("user", content));
                thread.Messages.Add(new ChatLine(reply.Role, reply.Content, reply.Id, reply.CreatedAt,
                    new ChatMessageMetadata(reply.Citations, reply.RelevantImages)));
                if (attempt.FirstMessage) thread.PendingTitle = MakeTitle(content);
            }
            if (thread.PendingTitle is not null) await SaveTitleAsync(thread);
        }
        finally { thread.PendingContent = null; thread.Activity = ChatActivity.Idle; Notify(); }
    }

    private async Task<bool> ReadHistoryAsync(Conversation thread)
    {
        var rows = await RequestAsync((token, ct) => api.GetSessionHistoryAsync(token, thread.Id, ct),
            "History could not be loaded. Reload history before sending.", message => thread.Notice = message);
        thread.HistoryLoaded = rows is not null && rows.All(r => r.SessionId == thread.Id);
        if (!thread.HistoryLoaded)
        {
            if (rows is not null) thread.Notice = "The history did not match this conversation. Reload history.";
            return false;
        }
        thread.Notice = null;
        var votes = thread.Messages.Where(m => m.Id is not null && m.Feedback is not null)
            .ToDictionary(m => m.Id!, m => m.Feedback);
        thread.Messages.Clear();
        foreach (var row in rows!)
            thread.Messages.Add(new ChatLine(row.Role, row.Content, row.Id, row.CreatedAt, row.Metadata)
                { Feedback = votes.GetValueOrDefault(row.Id) });

        if (thread.Unconfirmed is { } attempt)
        {
            var start = attempt.PreviousId is null ? 0 : thread.Messages.FindIndex(m => m.Id == attempt.PreviousId) + 1;
            if (attempt.PreviousId is not null && start == 0)
            {
                thread.Unconfirmed = null;
                thread.Notice = "The earlier history changed. Review the saved messages before sending again.";
                return true;
            }
            var saved = thread.Messages.Skip(start).FirstOrDefault(m => m.Role == "user" && m.Content == attempt.Content);
            if (saved is null)
                thread.Notice = "This message is not in saved history yet. You can check again or retry.";
            else
            {
                thread.Unconfirmed = null;
                if (thread.Draft == attempt.Content) thread.Draft = "";
                if (attempt.FirstMessage) thread.PendingTitle = MakeTitle(attempt.Content);
                var next = thread.Messages.IndexOf(saved) + 1;
                thread.Notice = next < thread.Messages.Count && thread.Messages[next].Role == "assistant"
                    ? "Your message and the assistant reply were recovered from saved history."
                    : "Your message was saved. Reload history to check for a reply.";
            }
        }
        return true;
    }

    private static string MakeTitle(string content)
    {
        if (content.Length <= 30) return content;
        var end = char.IsHighSurrogate(content[29]) ? 29 : 30;
        return content[..end] + "...";
    }

    public async Task RetryTitleAsync(Conversation thread)
    {
        if (disposed || !Conversations.Contains(thread) || thread.Activity != ChatActivity.Idle || thread.PendingTitle is null) return;
        thread.Activity = ChatActivity.UpdatingTitle;
        Notify();
        try { await SaveTitleAsync(thread); }
        finally { thread.Activity = ChatActivity.Idle; Notify(); }
    }

    private async Task SaveTitleAsync(Conversation thread)
    {
        var updated = await RequestAsync((token, ct) => api.UpdateSessionAsync(token, thread.Id, new(thread.PendingTitle!), ct),
            "The conversation title could not be saved.", message => thread.TitleError = message);
        if (updated?.Id != thread.Id || updated.UserId != ownerId) return;
        thread.Title = updated.Title ?? "New Chat";
        thread.PendingTitle = null;
        thread.TitleError = null;
    }

    public async Task DeleteAsync(Conversation thread)
    {
        if (disposed || !ListReady || Creating || thread.Activity != ChatActivity.Idle || !Conversations.Contains(thread)) return;
        thread.Activity = ChatActivity.Deleting;
        Notify();
        try
        {
            var result = await RequestAsync((token, ct) => api.DeleteSessionAsync(token, thread.Id, ct),
                "Deletion could not be confirmed. Reload the conversation list.", message => Notice = message);
            if (result?.Success != true)
            {
                ListReady = false;
                Notice ??= "Reload the conversation list to verify deletion.";
                return;
            }
            Conversations.Remove(thread);
            if (Active != thread) return;
            Active = Conversations.FirstOrDefault();
            if (Active is not null) await SelectAsync(Active);
        }
        finally { thread.Activity = ChatActivity.Idle; Notify(); }
    }

    private async Task<T?> RequestAsync<T>(Func<string, CancellationToken, Task<T>> request, string failure,
        Action<string> report) where T : class
    {
        if (disposed) return null;
        try
        {
            var response = await access.WithAccessTokenAsync(ownerId, request, lifetime.Token);
            return disposed ? null : response;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (!disposed)
            {
                var reason = ex switch
                {
                    HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "Access was denied. ",
                    HttpRequestException { StatusCode: null or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable } => "The chat service is unavailable. ",
                    OperationCanceledException or HttpRequestException { StatusCode: HttpStatusCode.GatewayTimeout } => "The request timed out. ",
                    _ => ""
                };
                report(reason + failure);
            }
            return null;
        }
    }

    private void Notify() { if (!disposed) Changed?.Invoke(); }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
