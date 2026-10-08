using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeBoundaryTests
{
    [Fact]
    public void PublicSurfaceContainsNoEmbeddingArraysSdkObjectsOrRawTransport()
    {
        var types = typeof(IPineconeService).Assembly.GetExportedTypes();
        Assert.DoesNotContain(typeof(OnnxEmbeddingModel), types);
        Assert.DoesNotContain(typeof(WordPieceTokenizer), types);
        Assert.DoesNotContain(types, t => t.Namespace?.Contains("Internal") == true);
        Assert.Empty(typeof(PineconeService).GetConstructors());
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties()) Check(property.PropertyType);
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance))
            {
                Check(method.ReturnType);
                foreach (var parameter in method.GetParameters()) Check(parameter.ParameterType);
            }
        }
        static void Check(Type type)
        {
            Assert.NotEqual(typeof(float[]), type); Assert.NotEqual(typeof(double[]), type);
            Assert.DoesNotContain("Pinecone.Sdk", type.FullName ?? "");
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Check(argument);
        }
    }

    [Fact]
    public void ApplicationSourcesDoNotUsePrivateModelOrTransportAndRagHasNoDatabaseDependency()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CFC.ChatAI.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "CFC.ChatAI.Api"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("obj")))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("EncodeQuery(", source); Assert.DoesNotContain("OnnxEmbeddingModel", source);
            Assert.DoesNotContain("PineconeTransport", source); Assert.DoesNotContain("Pinecone.Sdk", source);
        }
        var project = File.ReadAllText(Path.Combine(root, "src", "CFC.ChatAI.Rag", "CFC.ChatAI.Rag.csproj"));
        foreach (var forbidden in new[] { "Supabase", "EntityFramework", "Npgsql", "SqlClient" }) Assert.DoesNotContain(forbidden, project);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "CFC.ChatAI.Rag"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("obj")))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ChunkRepository", source); Assert.DoesNotContain("Supabase", source);
        }
    }

    [Fact]
    public async Task DiReusesOneEncoderAndDisposesItExactlyOnce()
    {
        var f = new ServiceFixture(); var encoder = new DisposableEncoder();
        var services = new ServiceCollection().AddPineconeService(f.Options, f.Embedding);
        services.AddSingleton<IEmbeddingEncoder>(_ => encoder); services.AddSingleton<IPineconeTransport>(_ => f.Transport);
        await using (var provider = services.BuildServiceProvider())
        {
            var a = provider.GetRequiredService<IPineconeService>();
            using (var scope = provider.CreateScope()) Assert.Same(a, scope.ServiceProvider.GetRequiredService<IPineconeService>());
            await a.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk()]);
            await a.UpsertChunksAsync(ChunkCollection.Videos, [ServiceFixture.Chunk(collection: ChunkCollection.Videos)]);
            Assert.Equal(2, encoder.Calls); Assert.Equal(0, encoder.Disposals);
        }
        Assert.Equal(1, encoder.Disposals);
    }

    [Fact]
    public void LegacyEnvironmentMappingIsExplicitAndDoesNotNeedDatabaseConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PINECONE_API_KEY"] = "offline-key", ["PINECONE_INDEX_NAME"] = "doc-index",
            ["PINECONE_VIDEO_INDEX_NAME"] = "video-index", ["PINECONE_NAMESPACE"] = "shared-ns",
            ["PINECONE_DOCUMENT_MODEL_IDENTITY"] = "export1", ["PINECONE_VIDEO_MODEL_IDENTITY"] = "export1",
            ["EMBEDDING_MODEL_IDENTITY"] = "export1"
        }).Build();
        using var provider = new ServiceCollection().AddPineconeService(configuration, Path.GetFullPath(".")).BuildServiceProvider();
        var options = provider.GetRequiredService<PineconeServiceOptions>();
        Assert.Equal("doc-index", options.Documents.IndexName); Assert.Equal("video-index", options.Videos.IndexName);
        Assert.Equal("shared-ns", options.Videos.Namespace);
        Assert.True(Path.IsPathFullyQualified(provider.GetRequiredService<EmbeddingOptions>().ModelPath));
        Assert.NotNull(provider.GetRequiredService<IPineconeService>());
    }

    private sealed class DisposableEncoder : IEmbeddingEncoder, IDisposable
    {
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        public Task<float[]> EncodeAsync(string text, CancellationToken cancellationToken) { Calls++; return Task.FromResult(FakeEncoder.UnitVector()); }
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public void Dispose() => Disposals++;
    }
}
