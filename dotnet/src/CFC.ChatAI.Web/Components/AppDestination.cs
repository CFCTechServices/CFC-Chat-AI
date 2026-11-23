using System.Reflection;
using CFC.ChatAI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace CFC.ChatAI.Web.Components;

public sealed class AppDestination
{
    private AppDestination(string label, string path) => (Label, Path) = (label, path);

    public string Label { get; }
    public string Path { get; }

    private static readonly AppDestination Chat = new("Chat", "/chat");
    private static readonly AppDestination Docs = new("Docs", "/docs");
    public static AppDestination Admin { get; } = new("Admin", "/admin");
    private static readonly AppDestination[] Destinations =
        [Chat, new("History", "/history"), new("Settings", "/settings"), Docs, Admin];

    // Use the same assembly as the Blazor Router. Publish a link only when
    // a routable page implements the destination's exact path.
    private static readonly HashSet<string> ImplementedPaths = typeof(App).Assembly.ExportedTypes
        .Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type))
        .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(inherit: false))
        .Select(route => route.Template.TrimEnd('/'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool Allows(ProfileResponse? verifiedProfile) => verifiedProfile is { Status: "active" } &&
        (this != Admin || verifiedProfile.Role is "admin" or "superuser");

    // React lists Docs in Settings' starting-page choices, outside the toolbar.
    public static IEnumerable<AppDestination> Toolbar(ProfileResponse verifiedProfile) =>
        Destinations.Where(page => page != Docs && ImplementedPaths.Contains(page.Path) && page.Allows(verifiedProfile));

    public static string LandingPage(ProfileResponse verifiedProfile) =>
        ImplementedPaths.Contains(Admin.Path) && Admin.Allows(verifiedProfile) ? Admin.Path : Chat.Path;

    public static AppDestination? ForPage(Type pageType)
    {
        AppDestination? match = null;
        foreach (var route in pageType.GetCustomAttributes<RouteAttribute>(inherit: false))
        {
            var destination = Array.Find(Destinations, page =>
                route.Template.Equals(page.Path, StringComparison.OrdinalIgnoreCase) ||
                route.Template.StartsWith(page.Path + "/", StringComparison.OrdinalIgnoreCase));
            // Apply the admin restriction to every alias of an admin page.
            if (destination == Admin) return Admin;
            match ??= destination;
        }
        return match;
    }
}
