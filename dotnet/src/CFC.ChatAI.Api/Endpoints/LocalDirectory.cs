namespace CFC.ChatAI.Api.Endpoints;

internal static class LocalDirectory
{
    // These are operator-owned local directories, never writable by request callers. Reject
    // symlinks/junctions in the root and its ancestors rather than following them.
    public static string Require(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath) ||
            (OperatingSystem.IsWindows() && configuredPath.StartsWith("\\\\", StringComparison.Ordinal)))
            throw new DirectoryNotFoundException();
        var directory = new DirectoryInfo(Path.GetFullPath(configuredPath));
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
        {
            var attrs = File.GetAttributes(current.FullName);
            if ((attrs & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new DirectoryNotFoundException();
        }
        return directory.FullName;
    }

    public static bool IsUnavailable(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
