using System.Net;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Api;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed class ChatApiClientTests : IDisposable
{
    private const string SessionJson = """
        {"id":"s1","user_id":"u1","created_at":"2026-03-17T09:00:00Z","title":"First chat"}
        """;
    private readonly string token = Guid.NewGuid().ToString("N");
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> expected = new();
    private readonly HttpClient http;
    private readonly ChatApiClient client;

    public ChatApiClientTests()
    {
        http = CreateHttpClient((request, ct) => expected.Dequeue()(request, ct));
        client = new ChatApiClient(http);
    }

    public void Dispose() => http.Dispose();

    [Fact]
    public async Task ReadsPublicAuthContractsAndAuthenticatedProfile()
    {
        var publicKey = Guid.NewGuid().ToString("N");
        Expect(HttpMethod.Get, "api/auth/config",
            JsonSerializer.Serialize(new { supabaseUrl = "https://auth.example.test", supabaseKey = publicKey }));
        var config = await client.GetAuthConfigAsync();
        Assert.Equal("https://auth.example.test", config.SupabaseUrl);
        Assert.Equal(publicKey, config.SupabaseKey);

        var email = new EmailRequest("person@example.test");
        const string emailJson = """{"email":"person@example.test"}""";
        Expect(HttpMethod.Post, "api/auth/check-email", """{"eligible":true,"message":"Eligible"}""", body: emailJson);
        Assert.Equal(new CheckEmailResponse(true, "Eligible"), await client.CheckEmailAsync(email));

        Expect(HttpMethod.Post, "api/auth/forgot-password", """{"success":false,"message":"Try later"}""", body: emailJson);
        Assert.Equal(new ForgotPasswordResponse(false, "Try later"), await client.RequestPasswordResetAsync(email));

        Expect(HttpMethod.Get, "api/profile/me", """
            {"id":"u1","email":"person@example.test","full_name":"Test User","avatar_url":null,
             "status":"active","role":"user","created_at":"2026-03-17T09:00:00Z"}
            """, token);
        var profile = await client.GetProfileAsync(token);
        Assert.Equal("Test User", profile.FullName);
        Assert.Equal("active", profile.Status);
        Assert.Null(profile.AvatarUrl);
    }

    [Fact]
    public async Task PatchesOnlyChangedProfileFieldsForBearerOwner()
    {
        const string profileJson = """{"id":"u1","email":"user@example.test","role":"user","status":"active","created_at":"today","full_name":"Pat","avatar_url":"image"}""";
        Expect(HttpMethod.Patch, "api/profile/me", profileJson, token, """{"full_name":"Pat"}""");
        Assert.Equal("Pat", (await client.UpdateProfileAsync(token, new("Pat", null))).FullName);
        Expect(HttpMethod.Patch, "api/profile/me", profileJson, token, """{"avatar_url":"image"}""");
        Assert.Equal("image", (await client.UpdateProfileAsync(token, new(null, "image"))).AvatarUrl);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task AdminUsersUseExactApiContractsAndPerRequestAuthorization()
    {
        Expect(HttpMethod.Get, "api/admin/users", """
            {"users":[{"id":"u/1","email":"p@example.test","full_name":"Pat","role":"dev","status":"inactive",
            "created_at":"joined","deleted_at":"when","deleted_by":"owner","last_active":null}],"total":1}
            """, token);
        var list = await client.GetAdminUsersAsync(token, default);
        Assert.Equal(1, list.Total);
        var user = Assert.Single(list.Users);
        Assert.Equal("Pat", user.FullName);
        Assert.Equal("owner", user.DeletedBy);

        Expect(HttpMethod.Post, "api/admin/invite", """{"message":"Created","email":"p@example.test","expires_at":"later"}""",
            token, """{"email":"p@example.test"}""");
        Assert.Equal("later", (await client.InviteUserAsync(token, new(user.Email), default)).ExpiresAt);
        Expect(HttpMethod.Post, "api/admin/change-role", """{"message":"Changed","user_id":"u/1","new_role":"admin"}""",
            token, """{"user_id":"u/1","new_role":"admin"}""");
        Assert.Equal("admin", (await client.ChangeUserRoleAsync(token, new(user.Id, "admin"), default)).NewRole);
        Expect(HttpMethod.Post, "api/admin/users/u%2F1/deactivate",
            """{"message":"Done","user_id":"u/1","email":"p@example.test","status":"inactive","deleted_at":"now"}""", token, "{}");
        Assert.Equal("now", (await client.DeactivateUserAsync(token, user.Id, default)).DeletedAt);
        Expect(HttpMethod.Post, "api/admin/users/u%2F1/reactivate",
            """{"message":"Done","user_id":"u/1","email":"p@example.test","status":"active","restored_at":"later"}""", token);
        Assert.Equal("later", (await client.ReactivateUserAsync(token, user.Id, default)).RestoredAt);
        Expect(HttpMethod.Delete, "api/admin/users/u%2F1",
            """{"message":"Deleted","user_id":"u/1","email":"p@example.test","permanently_deleted":true}""", token);
        Assert.True((await client.DeleteUserAsync(token, user.Id, default)).PermanentlyDeleted);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task AdminDocumentsUseExactRoutesBearerAndResponseContracts()
    {
        const string resource = "api/admin/documents/guide%20A";
        Expect(HttpMethod.Get, "api/admin/documents",
            """{"documents":[{"doc_id":"guide A","source":"/docs/Guide.docx","source_type":"document","chunk_count":3}],"total":1}""", token);
        var list = await client.GetDocumentsAsync(token, default);
        var doc = Assert.Single(list.Documents);
        Assert.Equal(1, list.Total);
        Assert.Equal("Guide.docx", doc.Name);
        Assert.Equal(3, doc.ChunkCount);
        Expect(HttpMethod.Get, resource + "/download", """{"url":"https://fixture.invalid/download","filename":"Guide.docx"}""", token);
        Assert.Equal("Guide.docx", (await client.GetDocumentDownloadAsync(token, doc.Id, default)).Filename);
        expected.Enqueue(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("/backend/" + resource + "/replace", req.RequestUri!.PathAndQuery);
            Assert.Equal(token, req.Headers.Authorization?.Parameter);
            var multipart = Assert.IsType<MultipartFormDataContent>(req.Content);
            Assert.Equal("multipart/form-data", multipart.Headers.ContentType!.MediaType);
            var wire = Encoding.UTF8.GetString(await multipart.ReadAsByteArrayAsync(ct));
            Assert.Contains("name=\"file\"; filename=\"Guía.docx\"", wire);
            Assert.Contains("sample contents", wire);
            return JsonResponse("""{"success":true,"message":"Accepted","ingestion":{"doc_id":"guía","success":true,"chunks_processed":2}}""");
        });
        using var file = new MemoryStream("sample contents"u8.ToArray());
        var replaced = await client.ReplaceDocumentAsync(token, doc.Id, "Guía.docx", file, file.Length, default);
        Assert.Equal("guía", replaced.Ingestion.Id);
        Expect(HttpMethod.Delete, resource, """{"success":true,"message":"Removed"}""", token);
        Assert.True((await client.DeleteDocumentAsync(token, doc.Id, default)).Success);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task AdminSettingsUseAuthenticatedSparsePatchesAndHealthIsAnonymous()
    {
        const string path = "api/admin/settings";
        const string json = """{"auto_approve_uploads":true,"email_notifications":false,"maintenance_mode":true}""";
        var settings = new AdminSystemSettings(true, false, true);
        Expect(HttpMethod.Get, path, json, token);
        Assert.Equal(settings, await client.GetAdminSettingsAsync(token, default));
        foreach (var (change, body) in new (AdminSettingsPatch, string)[]
        {
            (new(AutoApproveUploads: true), """{"auto_approve_uploads":true}"""),
            (new(EmailNotifications: false), """{"email_notifications":false}"""),
            (new(MaintenanceMode: true), """{"maintenance_mode":true}""")
        })
        {
            Expect(HttpMethod.Patch, path, json, token, body);
            Assert.Equal(settings, await client.UpdateAdminSettingsAsync(token, change, default));
        }
        Expect(HttpMethod.Get, "api/health", """{"ok":true,"message":"Running"}""");
        Assert.Equal(new ApiHealthResponse(true, "Running"), await client.GetHealthAsync(default));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ListsLightweightAndDetailedSessions()
    {
        Expect(HttpMethod.Get, "api/chat/sessions", $"[{SessionJson}]", token);
        var session = Assert.Single(await client.GetSessionsAsync(token));
        Assert.Equal("s1", session.Id);
        Assert.Equal("u1", session.UserId);
        Assert.Null(session.MessageCount);

        Expect(HttpMethod.Get, "api/chat/sessions?detail=true", """
            [{"id":"s1","user_id":"u1","title":null,"created_at":"2026-03-17T09:00:00Z",
              "message_count":2,"last_message":"A question"}]
            """, token);
        var detailed = Assert.Single(await client.GetSessionsAsync(token, detail: true));
        Assert.Null(detailed.Title);
        Assert.Equal(2, detailed.MessageCount);
        Assert.Equal("A question", detailed.LastMessage);
    }

    [Fact]
    public async Task CreatesRenamesAndDeletesSession()
    {
        Expect(HttpMethod.Post, "api/chat/sessions", SessionJson, token, """{"title":"New Chat"}""");
        var session = await client.CreateSessionAsync(token, new CreateSessionRequest());
        Assert.Equal("s1", session.Id);

        Expect(HttpMethod.Patch, "api/chat/sessions/s1", SessionJson, token, """{"title":"First chat"}""");
        var renamed = await client.UpdateSessionAsync(token, session.Id, new UpdateSessionRequest("First chat"));
        Assert.Equal("First chat", renamed.Title);

        Expect(HttpMethod.Delete, "api/chat/sessions/s1", """{"success":true}""", token);
        Assert.True((await client.DeleteSessionAsync(token, session.Id)).Success);
    }

    [Fact]
    public async Task SendsOnlySessionIdAndContentAndReadsSavedAssistantReply()
    {
        Expect(HttpMethod.Post, "api/chat/message", """
            {"id":"m2","role":"assistant","content":"Answer","created_at":"2026-03-17T09:00:00Z",
             "citations":[{"chunk_id":"c1","extra":{"page":3}}],
             "relevant_images":[{"path":"docs/a.png","position":4,"alt_text":"Image","relevance_score":0.8,
                                 "context_text":"Example","width":200}]}
            """, token, """{"session_id":"s1","content":"Question"}""");
        var reply = await client.SendMessageAsync(token, new ChatMessageRequest("s1", "Question"));
        Assert.Equal("m2", reply.Id);
        Assert.Equal("assistant", reply.Role);
        Assert.Equal("Answer", reply.Content);
        Assert.Equal("2026-03-17T09:00:00Z", reply.CreatedAt);
        var citation = Assert.Single(reply.Citations!);
        Assert.Equal(3, citation["extra"].GetProperty("page").GetInt32());
        var image = Assert.Single(reply.RelevantImages!);
        Assert.Equal("docs/a.png", image.Path);
        Assert.Equal(4, image.Position);
        Assert.Equal("Image", image.AltText);
        Assert.Equal(0.8, image.RelevanceScore);
        Assert.Equal("Example", image.ContextText);
        Assert.Equal(200, image.AdditionalFields!["width"].GetInt32());
    }

    [Fact]
    public async Task ReadsHistoryWithAbsentNullAndNestedMetadata()
    {
        Expect(HttpMethod.Get, "api/chat/sessions/session%20A", """
            [{"id":"m1","session_id":"session A","role":"user","content":"Question","created_at":"t1"},
             {"id":"m2","session_id":"session A","role":"assistant","content":"Older","created_at":"t2","metadata":null},
             {"id":"m3","session_id":"session A","role":"assistant","content":"Answer","created_at":"t3",
              "metadata":{"citations":[],"relevant_images":[{"path":"docs/a.png","position":null}],"version":2}}]
            """, token);
        var history = await client.GetSessionHistoryAsync(token, "session A");
        Assert.Equal(3, history.Count);
        Assert.Null(history[0].Metadata);
        Assert.Null(history[1].Metadata);
        Assert.Equal("session A", history[2].SessionId);
        var metadata = Assert.IsType<ChatMessageMetadata>(history[2].Metadata);
        Assert.Empty(metadata.Citations!);
        Assert.Equal("docs/a.png", Assert.Single(metadata.RelevantImages!).Path);
        Assert.Equal(2, metadata.AdditionalFields!["version"].GetInt32());
    }

    [Fact]
    public async Task ReadsFeedbackMapWithEncodedSessionQuery()
    {
        Expect(HttpMethod.Get, "api/chat/feedback?session_id=s%2B1%26x%3D2", """{"feedback":{"m1":1,"m2":-1}}""", token);
        var votes = await client.GetFeedbackAsync(token, "s+1&x=2");
        Assert.Equal(1, votes.Feedback["m1"]);
        Assert.Equal(-1, votes.Feedback["m2"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(null)]
    public async Task SubmitsOrClearsFeedback(int? rating)
    {
        Expect(HttpMethod.Post, "api/chat/feedback", JsonSerializer.Serialize(new { success = true, score = rating }),
            token, JsonSerializer.Serialize(new { message_id = "m1", session_id = "s1", rating }));
        var result = await client.SubmitFeedbackAsync(token, new FeedbackRequest("m1", "s1", rating));
        Assert.True(result.Success);
        Assert.Equal(rating, result.Score);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PreservesHttpStatusAndStringDetail(HttpStatusCode status)
    {
        Expect(HttpMethod.Post, "api/chat/message", """{"detail":"Request failed"}""", token,
            """{"session_id":"s1","content":"Question"}""", status);
        var error = await Assert.ThrowsAsync<ChatApiException>(() =>
            client.SendMessageAsync(token, new ChatMessageRequest("s1", "Question")));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("Request failed", error.Message);
        Assert.Empty(expected);
    }

    [Fact]
    public async Task PreservesStructuredValidationDetail()
    {
        Expect(HttpMethod.Post, "api/auth/check-email", """
            {"detail":[{"loc":["body","email"],"msg":"Invalid address","type":"value_error","input":"invalid"}]}
            """, body: """{"email":"invalid"}""", status: HttpStatusCode.UnprocessableEntity);
        var error = await Assert.ThrowsAsync<ChatApiException>(() => client.CheckEmailAsync(new EmailRequest("invalid")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Equal("Invalid address", error.Message);
        Assert.Equal("email", error.Detail!.Value[0].GetProperty("loc")[1].GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Unavailable</html>")]
    public async Task PreservesStatusForNonJsonErrors(string responseBody)
    {
        Expect(HttpMethod.Get, "api/chat/sessions", responseBody, token, status: HttpStatusCode.BadGateway);
        var error = await Assert.ThrowsAsync<ChatApiException>(() => client.GetSessionsAsync(token));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Null(error.Detail);
    }

    [Fact]
    public async Task RejectsMalformedSuccessPayload()
    {
        Expect(HttpMethod.Post, "api/chat/message", """{"answer":"Wrong endpoint shape"}""", token,
            """{"session_id":"s1","content":"Question"}""");
        await Assert.ThrowsAsync<JsonException>(() =>
            client.SendMessageAsync(token, new ChatMessageRequest("s1", "Question")));
    }

    [Fact]
    public async Task OverlappingUsersAndPublicCallHaveIndependentAuthorization()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherToken = Guid.NewGuid().ToString("N");
        using var sharedHttp = CreateHttpClient(async (request, ct) =>
        {
            await release.Task.WaitAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/config", StringComparison.Ordinal))
            {
                Assert.Null(request.Headers.Authorization);
                return JsonResponse("""{"supabaseUrl":null,"supabaseKey":null}""");
            }
            var expectedToken = path.EndsWith("/first", StringComparison.Ordinal) ? token : otherToken;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            return JsonResponse("[]");
        });
        var sharedClient = new ChatApiClient(sharedHttp);
        var first = sharedClient.GetSessionHistoryAsync(token, "first");
        var second = sharedClient.GetSessionHistoryAsync(otherToken, "second");
        var anonymous = sharedClient.GetAuthConfigAsync();
        release.SetResult();
        await Task.WhenAll(first, second, anonymous);
        Assert.Null(sharedHttp.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task CancellationReachesInFlightHandler()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expected.Enqueue(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse("[]");
        });
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetSessionsAsync(token, cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private void Expect(HttpMethod method, string path, string response, string? bearer = null,
        string? body = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        expected.Enqueue(async (request, ct) =>
        {
            Assert.Equal(method, request.Method);
            Assert.Equal("/backend/" + path, request.RequestUri!.PathAndQuery);
            Assert.Equal(bearer is null ? null : "Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(bearer, request.Headers.Authorization?.Parameter);
            if (body is null)
            {
                Assert.Null(request.Content);
            }
            else
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                using var actual = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                using var wanted = JsonDocument.Parse(body);
                Assert.True(JsonElement.DeepEquals(wanted.RootElement, actual.RootElement), $"Unexpected body: {actual.RootElement}");
            }
            return JsonResponse(response, status);
        });
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new FakeHttpHandler(send)) { BaseAddress = new Uri("https://backend.example.test/backend/") };

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }
}
