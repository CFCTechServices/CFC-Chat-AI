using CFC.ChatAI.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace CFC.ChatAI.Api.Tests;

public sealed class DotEnvConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public DotEnvConfigurationTests() => Directory.CreateDirectory(root);

    [Fact]
    public void DiscoversRepositoryFileFromNestedContentRoot()
    {
        File.WriteAllText(Path.Combine(root, ".env"), "PINECONE_API_KEY=local-key\nSUPABASE_URL=https://example.supabase.co\nSUPABASE_SERVICE_ROLE_KEY=local-service\nAZURE_OPENAI_API_KEY=local-azure\nAZURE_OPENAI_ENDPOINT=https://example.openai.azure.com\n");
        var nested = Directory.CreateDirectory(Path.Combine(root, "dotnet", "src", "Api")).FullName;
        var configuration = new ConfigurationManager();
        configuration.AddProjectDotEnv(nested);
        var options = RuntimeOptions.FromConfiguration(configuration);
        Assert.Equal("local-key", options.PineconeApiKey);
        Assert.Equal("https://example.supabase.co", options.SupabaseUrl);
        Assert.Equal("local-service", options.SupabaseServiceRoleKey);
        Assert.Equal("local-azure", options.AzureApiKey);
        Assert.Equal("https://example.openai.azure.com", options.AzureEndpoint);
    }

    [Fact]
    public void ExplicitPathSupportsQuotedValuesAndHierarchicalKeys()
    {
        File.WriteAllText(Path.Combine(root, "secrets.env"), "Supabase__ServiceRoleKey='test secret'\n");
        var configuration = new ConfigurationManager();
        configuration.AddProjectDotEnv(root, "secrets.env");
        Assert.Equal("test secret", RuntimeOptions.FromConfiguration(configuration).SupabaseServiceRoleKey);
    }

    [Fact]
    public void ExistingProvidersOverrideFileAndProcessEnvironmentIsUnchanged()
    {
        var variable = "CFC_DOTENV_TEST_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(root, ".env"), $"PINECONE_API_KEY=file-key\n{variable}=file-only\n");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PINECONE_API_KEY"] = "deployment-key" });
        configuration.AddProjectDotEnv(root);
        Assert.Equal("deployment-key", RuntimeOptions.FromConfiguration(configuration).PineconeApiKey);
        Assert.Equal("file-only", configuration[variable]);
        Assert.Null(Environment.GetEnvironmentVariable(variable));
    }

    [Fact]
    public void ExplicitMissingFileFailsInsteadOfSilentlyUsingAnotherFile()
    {
        var configuration = new ConfigurationManager();
        Assert.Throws<FileNotFoundException>(() => configuration.AddProjectDotEnv(root, "missing.env"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
