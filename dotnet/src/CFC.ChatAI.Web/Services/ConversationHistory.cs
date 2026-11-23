using System.Globalization;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public static class ConversationHistory
{
    public static string Title(ChatSession session) =>
        string.IsNullOrWhiteSpace(session.Title) ? "Untitled Chat" : session.Title;

    public static DateTimeOffset? CreatedAt(ChatSession session) =>
        DateTimeOffset.TryParse(session.CreatedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created) ? created : null;

    public static string CreationLabel(DateTimeOffset created, DateTimeOffset now)
    {
        var date = created.ToString("MMM d, yyyy · HH:mm") + " UTC";
        var age = now - created;
        if (age < TimeSpan.Zero) return date;
        var relative = age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{age.Minutes}m ago",
            { TotalDays: < 1 } => $"{age.Hours}h ago",
            _ => $"{age.Days}d ago"
        };
        return $"{date} ({relative})";
    }

    public static IReadOnlyList<ChatSession> Filter(IReadOnlyList<ChatSession> sessions, string query,
        int days, DateTimeOffset now)
    {
        var search = query.Trim();
        var since = now.AddDays(-days);
        return sessions.Where(session =>
            (days == 0 || (CreatedAt(session) is { } created && created >= since && created <= now)) &&
            (Title(session).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                session.LastMessage?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
    }
}
