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
    public static AppDestination Admin { get; } = new("Admin", "/admin");
    private static readonly AppDestination[] Destinations =
        [Chat, new("History", "/history"), new("Settings", "/settings"), Admin];

    // Use the same assembly as the Blazor Router. Publish a link only when
    // a routable page implements the destination's exact path.
    private static readonly HashSet<string> ImplementedPaths = typeof(App).Assembly.ExportedTypes
        .Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type))
        .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(inherit: false))
        .Select(route => route.Template.TrimEnd('/'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool Allows(ProfileResponse? verifiedProfile) => verifiedProfile is { Status: "active" } &&
        (this != Admin || verifiedProfile.Role is "admin" or "superuser");

    public static IEnumerable<AppDestination> Toolbar(ProfileResponse verifiedProfile) =>
        Destinations.Where(page => ImplementedPaths.Contains(page.Path) && page.Allows(verifiedProfile));

    public static IEnumerable<AppDestination> StartingPages(ProfileResponse profile) =>
        Toolbar(profile).Where(page => page.Path != "/settings");

    public static string LandingPage(ProfileResponse verifiedProfile, string? preference = null) =>
        StartingPages(verifiedProfile).FirstOrDefault(page => page.Path[1..] == preference)?.Path ??
        (ImplementedPaths.Contains(Admin.Path) && Admin.Allows(verifiedProfile) ? Admin.Path : Chat.Path);

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
