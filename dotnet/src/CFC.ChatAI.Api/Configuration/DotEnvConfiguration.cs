using Microsoft.Extensions.Configuration.Memory;

namespace CFC.ChatAI.Api.Configuration;

public static class DotEnvConfiguration
{
    /// <summary>Load local secrets as defaults, without changing process environment variables.</summary>
    public static void AddProjectDotEnv(this ConfigurationManager configuration, string contentRoot,
        string? explicitPath = null)
    {
        string? path;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            path = Path.GetFullPath(explicitPath, contentRoot);
            if (!File.Exists(path))
                throw new FileNotFoundException("The configured DOTENV_PATH file does not exist.");
        }
        else
        {
            path = FindDotEnv(contentRoot) ?? FindDotEnv(AppContext.BaseDirectory);
        }

        // A deployment using environment variables alone does not require a file.
        if (path is null) return;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in DotNetEnv.Env.NoEnvVars().Load(path))
            values[entry.Key.Replace("__", ":")] = entry.Value;

        // Keep appsettings, environment variables and command-line overrides above .env.
        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
    }

    private static string? FindDotEnv(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
