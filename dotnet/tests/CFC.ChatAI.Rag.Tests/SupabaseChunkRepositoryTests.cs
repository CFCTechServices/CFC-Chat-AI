using System.Net;
using System.Text;

namespace CFC.ChatAI.Rag.Tests;

public class SupabaseChunkRepositoryTests
{
    private const string Url = "https://example.supabase.co";
    private const string Key = "test-service-key";

    [Fact]
    public async Task SendsGetWithQueryAndAuthHeaders()
    {
        var handler = new StubHandler("[]");
        var repo = new SupabaseChunkRepository(new HttpClient(handler), Url, Key);

        await repo.GetByIdsAsync(["abc", "def"]);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://example.supabase.co/rest/v1/document_chunks?select=*&chunk_id=in.(%22abc%22%2C%22def%22)",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(Key, request.Headers.GetValues("apikey").Single());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Key, request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ParsesRowsIntoDictionaryKeyedByChunkId()
    {
        const string json = """
            [
              { "chunk_id": "doc1", "content": "How to add an ingredient", "source": "guide.docx",
                "source_type": "document", "section_title": "Ingredients", "image_paths": ["img/1.png"],
                "start_seconds": null, "video_url": null },
              { "chunk_id": "vid1", "content": "Video transcript", "source_type": "video",
                "start_seconds": 12.5, "end_seconds": "30", "video_url": "https://x/v.mp4" }
            ]
            """;
        var repo = new SupabaseChunkRepository(new HttpClient(new StubHandler(json)), Url, Key);

        var rows = await repo.GetByIdsAsync(["doc1", "vid1"]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("How to add an ingredient", rows["doc1"].Content);
        Assert.Equal("Ingredients", rows["doc1"].SectionTitle);
        Assert.Equal(new[] { "img/1.png" }, rows["doc1"].ImagePaths);
        Assert.Null(rows["doc1"].StartSeconds);
        Assert.Equal(12.5, rows["vid1"].StartSeconds);
        Assert.Equal(30.0, rows["vid1"].EndSeconds);    // number sent as a string still parses
        Assert.Null(rows["vid1"].ImagePaths);           // column missing entirely -> null
    }

    [Fact]
    public async Task EmptyIdList_MakesNoRequest()
    {
        var handler = new StubHandler("[]");
        var repo = new SupabaseChunkRepository(new HttpClient(handler), Url, Key);

        var rows = await repo.GetByIdsAsync([]);

        Assert.Empty(rows);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ErrorStatus_Throws()
    {
        var handler = new StubHandler("""{"message":"Invalid API key"}""", HttpStatusCode.Unauthorized);
        var repo = new SupabaseChunkRepository(new HttpClient(handler), Url, Key);

        await Assert.ThrowsAsync<HttpRequestException>(() => repo.GetByIdsAsync(["abc"]));
    }

    [Fact]
    public void BuildQuery_QuotesAndEscapesIds()
    {
        var query = SupabaseChunkRepository.BuildQuery(["a,b", "c\"d"]);

        // "a,b" stays one ID because it's quoted; the inner quote in c"d is backslash-escaped.
        Assert.Equal("document_chunks?select=*&chunk_id=in.(" + Uri.EscapeDataString("\"a,b\",\"c\\\"d\"") + ")", query);
    }

    /// <summary>Fake HTTP handler: records the request and returns a canned response. No network.</summary>
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}