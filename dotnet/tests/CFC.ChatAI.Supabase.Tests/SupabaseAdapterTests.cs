using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Conversations;
using CFC.ChatAI.Supabase.Feedback;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Invitations;
using CFC.ChatAI.Supabase.Storage;
using CFC.ChatAI.Supabase.Internal;
using CFC.ChatAI.Supabase.Profiles;

namespace CFC.ChatAI.Supabase.Tests;

public sealed class SupabaseAdapterTests
{
    [Fact]
    public void SupabaseOptions_RejectsUrlsWithEmbeddedCredentialsOrQueryComponents()
    {
        var options = CreateOptions();
        options.Url = "https://user:password@project.supabase.co";
        Assert.Throws<InvalidOperationException>(options.Validate);

        options = CreateOptions();
        options.Url = "https://project.supabase.co?apikey=secret";
        Assert.Throws<InvalidOperationException>(options.Validate);

        options = CreateOptions();
        options.Url = "https://project.supabase.co#fragment";
        Assert.Throws<InvalidOperationException>(options.Validate);

        options = CreateOptions();
        options.Url = "http://127.0.0.1:54321";
        options.Validate();
    }

    [Fact]
    public async Task ChunkUpsert_UsesTypedWireRowsAndServiceRoleHeaders()
    {
        var handler = new RecordingHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var transport = CreateTransport(handler);
        var repository = new SupabaseChunkRepository(transport);

        await repository.UpsertAsync([
            new Chunk("chunk-1", "doc-1", "content", SectionId: "section-1", ImagePaths: ["images/a.png"]),
        ]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/rest/v1/document_chunks?on_conflict=chunk_id", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        Assert.Equal("service-secret", handler.ApiKey);
        Assert.Contains("\"chunk_id\":\"chunk-1\"", handler.Body);
        Assert.Contains("\"doc_id\":\"doc-1\"", handler.Body);
        Assert.Contains("\"content\":\"content\"", handler.Body);
        Assert.DoesNotContain("embedding", handler.Body.ToLowerInvariant());
    }

    [Fact]
    public async Task ChunkRepository_AggregatesPagedDocumentRowsAndReturnsStableSummaryPages()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Contains("select=doc_id,source,source_type", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"doc_id\":\"doc-a\",\"source\":\"a.pdf\",\"source_type\":\"pdf\"},{\"doc_id\":\"doc-a\",\"source\":\"a.pdf\",\"source_type\":\"pdf\"},{\"doc_id\":\"doc-b\",\"source\":\"b.mp4\",\"source_type\":\"video\"}]"),
            });
        });
        var repository = new SupabaseChunkRepository(CreateTransport(handler));

        var page = await repository.ListDocumentSummariesAsync(new PageRequest(offset: 1, limit: 1));

        Assert.Single(page.Items);
        Assert.Equal(new DocumentSummary("doc-b", "b.mp4", "video", 1), page.Items[0]);
        Assert.False(page.HasMore);
        Assert.Contains("order=doc_id.asc,chunk_id.asc", handler.PathAndQuery);
        Assert.Contains("offset=0&limit=1000", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
    }

    [Fact]
    public async Task ChunkRepository_DocumentMetadataReturnsNullOrRejectsConflictingSourceValues()
    {
        var responses = new Queue<string>([
            "[]",
            "[{\"doc_id\":\"doc-a\",\"source\":\"one.pdf\",\"source_type\":\"pdf\"},{\"doc_id\":\"doc-a\",\"source\":\"two.pdf\",\"source_type\":\"pdf\"}]",
        ]);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue()),
        }));
        var repository = new SupabaseChunkRepository(CreateTransport(handler));

        Assert.Null(await repository.GetDocumentMetadataAsync("missing"));
        var exception = await Assert.ThrowsAsync<DocumentMetadataInconsistencyException>(
            () => repository.GetDocumentMetadataAsync("doc-a"));
        Assert.Equal("doc-a", exception.DocumentId);
        Assert.DoesNotContain("two.pdf", exception.Message);
    }

    [Fact]
    public async Task IdentityGateway_MapsUnauthorizedToAbsentUserButPropagatesOutage()
    {
        var unauthorizedHandler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var identity = new SupabaseIdentityGateway(CreateTransport(unauthorizedHandler));
        Assert.Null(await identity.GetUserAsync("expired-token"));
        Assert.Equal("Bearer expired-token", unauthorizedHandler.Authorization);
        Assert.Equal("anon-secret", unauthorizedHandler.ApiKey);

        var failedHandler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("contains-private-provider-details"),
        }));
        var failedIdentity = new SupabaseIdentityGateway(CreateTransport(failedHandler));
        var exception = await Assert.ThrowsAsync<DependencyFailureException>(
            () => failedIdentity.GetUserAsync("valid-shaped-token"));
        Assert.DoesNotContain("contains-private-provider-details", exception.Message);
    }

    [Fact]
    public async Task FeedbackVote_UsesOneAtomicRpcCall()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var repository = new SupabaseFeedbackRepository(CreateTransport(handler));

        await repository.SubmitMessageFeedbackAsync(new FeedbackSubmission("message-1", "user-1", -1));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/rest/v1/rpc/submit_message_feedback", handler.PathAndQuery);
        Assert.Contains("\"p_message_id\":\"message-1\"", handler.Body);
        Assert.Contains("\"p_user_id\":\"user-1\"", handler.Body);
        Assert.Contains("\"p_new_rating\":-1", handler.Body);
    }

    [Fact]
    public async Task ConversationContext_UsesCallerTokenOwnerFilterAndReturnsChronologicalLastMessages()
    {
        var paths = new List<string>();
        var handler = new RecordingHandler((request, _) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.EndsWith("/chat_sessions", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":\"session-1\",\"user_id\":\"user-1\",\"title\":\"Chat\",\"created_at\":\"2025-01-01T00:00:00Z\"}]"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\":\"m2\",\"session_id\":\"session-1\",\"role\":\"assistant\",\"content\":\"second\",\"created_at\":\"2025-01-01T00:00:02Z\",\"metadata\":{\"citations\":[{\"chunk_id\":\"c1\"}] }},{\"id\":\"m1\",\"session_id\":\"session-1\",\"role\":\"user\",\"content\":\"first\",\"created_at\":\"2025-01-01T00:00:01Z\"}]"),
            });
        });
        var repository = new SupabaseConversationRepository(CreateTransport(handler));

        var context = await repository.GetLastMessagesAsync("user-1", "caller-jwt", "session-1", 2);

        Assert.Equal(new[] { "m1", "m2" }, context.Select(message => message.Id));
        Assert.Equal("first", context[0].Content);
        Assert.Equal("c1", context[1].Metadata!.Value.GetProperty("citations")[0].GetProperty("chunk_id").GetString());
        Assert.Equal("Bearer caller-jwt", handler.Authorization);
        Assert.Equal("anon-secret", handler.ApiKey);
        Assert.Contains(paths, path => path.Contains("user_id=eq.user-1", StringComparison.Ordinal));
        Assert.Contains("order=created_at.desc,id.desc&limit=2", handler.PathAndQuery);
    }

    [Fact]
    public async Task DetailedSessions_ReturnBoundedCountAndTruncatedLastUserPreview()
    {
        var paths = new List<string>();
        var handler = new RecordingHandler((request, _) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.EndsWith("/chat_sessions", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":\"session-1\",\"user_id\":\"user-1\",\"title\":\"Chat\",\"created_at\":\"2025-01-01T00:00:00Z\"}]"),
                });
            if (request.RequestUri.AbsolutePath.EndsWith("/chat_messages", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("select=id&", StringComparison.Ordinal))
            {
                var countResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
                countResponse.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0, 3);
                return Task.FromResult(countResponse);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"content\":\"" + new string('x', 120) + "\"}]"),
            });
        });
        var repository = new SupabaseConversationRepository(CreateTransport(handler));

        var page = await repository.ListSessionsAsync("user-1", "caller-jwt", new PageRequest(0, 10), includeDetails: true);

        Assert.Equal(3, page.Items[0].MessageCount);
        Assert.Equal(100, page.Items[0].LastUserMessagePreview!.Length);
        Assert.Contains(paths, path => path.Contains("select=id", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ListSessionsAsync(
            "user-1", "caller-jwt", new PageRequest(0, 101), includeDetails: true));
    }

    [Fact]
    public async Task ConversationMessageInsert_UsesUserScopeAndSerializesJsonMetadata()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Method == HttpMethod.Get
                ? "[{\"id\":\"session-1\",\"user_id\":\"user-1\",\"title\":null,\"created_at\":\"2025-01-01T00:00:00Z\"}]"
                : "[{\"id\":\"message-1\",\"session_id\":\"session-1\",\"role\":\"assistant\",\"content\":\"answer\",\"created_at\":\"2025-01-01T00:00:01Z\",\"metadata\":{\"citations\":[],\"relevant_images\":[]}}]"),
        }));
        var repository = new SupabaseConversationRepository(CreateTransport(handler));
        using var metadata = System.Text.Json.JsonDocument.Parse("{\"citations\":[],\"relevant_images\":[{\"path\":\"image.png\"}]}");

        var inserted = await repository.InsertMessageAsync(
            "user-1", "caller-jwt", new CreateChatMessage("session-1", "assistant", "answer", metadata.RootElement));

        Assert.NotNull(inserted);
        Assert.Equal("message-1", inserted.Id);
        Assert.Contains("\"session_id\":\"session-1\"", handler.Body);
        Assert.Contains("\"metadata\":{\"citations\":[],\"relevant_images\":[{\"path\":\"image.png\"}]}", handler.Body);
        Assert.Equal("Bearer caller-jwt", handler.Authorization);
        Assert.Equal("anon-secret", handler.ApiKey);
    }

    [Fact]
    public async Task SessionDelete_ReportsPartialFailureAfterMessageDelete()
    {
        var operations = new List<string>();
        var handler = new RecordingHandler((request, _) =>
        {
            operations.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":\"session-1\",\"user_id\":\"user-1\",\"title\":null,\"created_at\":\"2025-01-01T00:00:00Z\"}]"),
                });
            if (request.RequestUri!.AbsolutePath.EndsWith("/chat_messages", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("private database detail"),
            });
        });
        var repository = new SupabaseConversationRepository(CreateTransport(handler));

        var exception = await Assert.ThrowsAsync<SessionDeletionPartialFailureException>(() =>
            repository.DeleteSessionAsync("user-1", "caller-jwt", "session-1"));

        Assert.DoesNotContain("private", exception.Message);
        Assert.Equal(3, operations.Count);
        Assert.Contains("user_id=eq.user-1", handler.PathAndQuery);
    }

    [Fact]
    public async Task FeedbackRead_IsUserFilteredOmitsNullRatingsAndBoundsInput()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[{\"message_id\":\"m1\",\"score\":1},{\"message_id\":\"m2\",\"score\":null}]"),
        }));
        var repository = new SupabaseFeedbackRepository(CreateTransport(handler));

        var scores = await repository.GetUserScoresForMessagesAsync("user-1", ["m1", "m2", "m1"]);

        Assert.Equal(1, scores["m1"]);
        Assert.DoesNotContain("m2", scores.Keys);
        Assert.Contains("user_id=eq.user-1", handler.PathAndQuery);
        Assert.Contains("message_id=in.%28%22m1%22%2C%22m2%22%29", handler.PathAndQuery);
        Assert.Contains("score=not.is.null", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetUserScoresForMessagesAsync(
            "user-1", Enumerable.Range(0, 1001).Select(i => i.ToString()).ToArray()));
    }

    [Fact]
    public async Task Invitations_ReadQueriesAreEmailScopedOrderedAndMapNullableRows()
    {
        var responses = new Queue<string>([
            "[{\"id\":\"invite-1\",\"email\":\"a+b@example.test\",\"created_by\":\"admin-1\",\"used_by\":null,\"is_registered\":false,\"created_at\":\"2025-02-03T04:05:06Z\",\"expires_at\":\"2025-02-10T04:05:06Z\"}]",
            "[]",
        ]);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue()),
        }));
        var repository = new SupabaseInvitationRepository(CreateTransport(handler));

        var invite = await repository.GetLatestByEmailAsync("a+b@example.test");
        Assert.NotNull(invite);
        Assert.Equal("invite-1", invite.Id);
        Assert.Equal(DateTimeOffset.Parse("2025-02-10T04:05:06Z"), invite.ExpiresAt);
        Assert.Equal("/rest/v1/invitations?select=id,email,created_by,used_by,is_registered,created_at,expires_at&email=eq.a%2Bb%40example.test&order=created_at.desc,id.asc&limit=1", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        Assert.Equal("service-secret", handler.ApiKey);

        Assert.Null(await repository.GetUnregisteredByEmailAsync("a+b@example.test"));
        Assert.Contains("is_registered=eq.false", handler.PathAndQuery);
    }

    [Fact]
    public async Task InvitationInsertReturnsDatabaseFieldsAndMapsKnownUniqueConflict()
    {
        var successHandler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("[{\"id\":\"invite-1\",\"email\":\"a@example.test\",\"created_by\":\"admin-1\",\"is_registered\":false,\"created_at\":\"2025-01-01T00:00:00Z\",\"expires_at\":\"2025-01-08T00:00:00Z\"}]"),
        }));
        var repository = new SupabaseInvitationRepository(CreateTransport(successHandler));
        var invite = await repository.InsertAsync(new CreateInvitation("a@example.test", "admin-1"));

        Assert.Equal("invite-1", invite.Id);
        Assert.Equal(DateTimeOffset.Parse("2025-01-08T00:00:00Z"), invite.ExpiresAt);
        Assert.Contains("\"email\":\"a@example.test\"", successHandler.Body);
        Assert.Contains("\"created_by\":\"admin-1\"", successHandler.Body);
        Assert.DoesNotContain("expires_at", successHandler.Body);
        Assert.Equal("return=representation", successHandler.Prefer);

        var conflictHandler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"23505\",\"message\":\"duplicate key violates idx_one_active_invite_per_email\",\"details\":\"private email detail\"}"),
        }));
        var conflictRepository = new SupabaseInvitationRepository(CreateTransport(conflictHandler));
        var conflict = await Assert.ThrowsAsync<InvitationConflictException>(() =>
            conflictRepository.InsertAsync(new CreateInvitation("a@example.test", null)));
        Assert.DoesNotContain("private", conflict.Message);
        Assert.DoesNotContain("a@example.test", conflict.Message);
    }

    [Fact]
    public async Task InvitationDeletesByIdAndEmailAreBoundedFilters()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var repository = new SupabaseInvitationRepository(CreateTransport(handler));

        await repository.DeleteByIdAsync("id&select=secret");
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal("/rest/v1/invitations?id=eq.id%26select%3Dsecret", handler.PathAndQuery);
        await repository.DeleteByEmailAsync("a+b@example.test");
        Assert.Equal("/rest/v1/invitations?email=eq.a%2Bb%40example.test", handler.PathAndQuery);
    }

    [Fact]
    public async Task AdminProfileCanFindEmailForInvitationWorkflows()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        }));
        var repository = new SupabaseAdminProfileRepository(CreateTransport(handler));

        Assert.Null(await repository.GetByEmailAsync("a+b@example.test"));
        Assert.Equal("/rest/v1/profiles?select=id,email,full_name,avatar_url,role,status,created_at,deleted_at,deleted_by&email=eq.a%2Bb%40example.test&limit=1", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
    }

    [Fact]
    public async Task AdminSessionRevocation_UsesGlobalScopeEndpoint()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var gateway = new SupabaseAdminIdentityGateway(CreateTransport(handler));

        await gateway.RevokeSessionsAsync("user-1");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/auth/v1/admin/users/user-1/logout", handler.PathAndQuery);
        Assert.Contains("\"scope\":\"global\"", handler.Body);
        Assert.Equal("Bearer service-secret", handler.Authorization);
    }

    [Fact]
    public async Task DynamicQueryValues_AreEscapedBeforeBuildingPostgrestUri()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var repository = new SupabaseFeedbackRepository(CreateTransport(handler));

        await repository.DeleteQueryEventsForMessageAsync("message&select=private");

        Assert.Equal(
            "/rest/v1/chunk_feedback_events?message_id=eq.message%26select%3Dprivate",
            handler.PathAndQuery);
    }

    [Fact]
    public async Task UserScopedRequests_KeepDistinctTokensIsolatedUnderConcurrency()
    {
        var observed = new ConcurrentBag<(string? Authorization, string? ApiKey)>();
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            await Task.Delay(20, cancellationToken);
            observed.Add((request.Headers.Authorization?.ToString(), request.Headers.GetValues("apikey").Single()));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var transport = CreateTransport(handler);

        async Task Send(string token)
        {
            using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri("profiles?select=id"));
            using var response = await transport.SendUserAsync(request, token, CancellationToken.None);
        }

        await Task.WhenAll(Send("user-token-one"), Send("user-token-two"));

        Assert.Equal(2, observed.Count);
        Assert.Contains(("Bearer user-token-one", "anon-secret"), observed);
        Assert.Contains(("Bearer user-token-two", "anon-secret"), observed);
    }

    [Fact]
    public async Task Transport_PreservesCallerCancellationAndBoundsRequestDuration()
    {
        var cancellationHandler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var transport = CreateTransport(cancellationHandler);
        using var callerCancellation = new CancellationTokenSource();
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri("profiles"));
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.SendAnonAsync(request, callerCancellation.Token));

        var timeoutHandler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var shortTimeoutOptions = CreateOptions();
        shortTimeoutOptions.RequestTimeout = TimeSpan.FromMilliseconds(30);
        var timeoutTransport = new SupabaseTransport(
            new StubHttpClientFactory(new HttpClient(timeoutHandler)), shortTimeoutOptions);
        using var timedRequest = SupabaseTransport.Request(HttpMethod.Get, timeoutTransport.RestUri("profiles"));
        var exception = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            timeoutTransport.SendAnonAsync(timedRequest, CancellationToken.None));
        Assert.DoesNotContain("token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StorageUpload_DoesNotTakeOwnershipOfCallerStream()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var storage = new SupabaseObjectStorage(CreateTransport(handler), CreateOptions());
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("payload"));

        await storage.UploadAsync(
            new ObjectKey(StorageArea.Documents, "docs/doc-1/file.txt"),
            input,
            new UploadOptions("text/plain", Overwrite: true));

        Assert.True(input.CanRead);
        Assert.Equal("/storage/v1/object/cfc-docs/docs/doc-1/file.txt", handler.PathAndQuery);
        Assert.Equal("true", handler.Upsert);
    }

    [Fact]
    public async Task Storage_VideoBucketListingIsPagedAndSignedUrlUsesRequestedExpiry()
    {
        var responses = new Queue<HttpResponseMessage>([
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"name\":\"clip.mp4\",\"id\":\"object-id\",\"metadata\":{\"mimetype\":\"video/mp4\",\"size\":42}}]"),
            },
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"signedURL\":\"/object/sign/videos/example/original/clip.mp4?token=short-lived\"}"),
            },
        ]);
        var handler = new RecordingHandler((_, _) => Task.FromResult(responses.Dequeue()));
        var storage = new SupabaseObjectStorage(CreateTransport(handler), CreateOptions());

        var listing = await storage.ListAsync(
            new ObjectPrefix(StorageArea.Videos, "videos/example/original"), new PageRequest(0, 1));
        Assert.Single(listing.Items);
        Assert.Equal("videos/example/original/clip.mp4", listing.Items[0].Key.Path);
        Assert.Equal(StorageArea.Videos, listing.Items[0].Key.Area);
        Assert.True(listing.HasMore);
        Assert.Contains("/storage/v1/object/list/cfc-videos", handler.PathAndQuery);
        Assert.Contains("\"offset\":0", handler.Body);
        Assert.Contains("\"limit\":1", handler.Body);

        var signed = await storage.CreateSignedUrlAsync(
            new ObjectKey(StorageArea.Videos, "videos/example/original/clip.mp4"), TimeSpan.FromMinutes(5));
        Assert.Equal("https://project.supabase.co/storage/v1/object/sign/videos/example/original/clip.mp4?token=short-lived", signed.ToString());
        Assert.Contains("/storage/v1/object/sign/cfc-videos/videos/example/original/clip.mp4", handler.PathAndQuery);
        Assert.Equal("{\"expiresIn\":300}", handler.Body);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => storage.CreateSignedUrlAsync(
            new ObjectKey(StorageArea.Videos, "videos/example/original/clip.mp4"), TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task UserProfile_ReadAndUpdateUseUserTokenAndAllowListedFields()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[{\"id\":\"user-1\",\"email\":\"u@example.test\",\"full_name\":\"Updated\",\"avatar_url\":null,\"role\":\"user\",\"status\":\"active\",\"created_at\":\"2025-01-02T03:04:05Z\",\"deleted_at\":null,\"deleted_by\":null}]"),
        }));
        var repository = new SupabaseUserProfileRepository(CreateTransport(handler));

        var profile = await repository.UpdateCurrentAsync("user-1", "caller-jwt", new UpdateOwnProfile(FullName: "Updated"));

        Assert.NotNull(profile);
        Assert.Equal("user-1", profile.Id);
        Assert.Equal(DateTimeOffset.Parse("2025-01-02T03:04:05Z"), profile.CreatedAt);
        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("Bearer caller-jwt", handler.Authorization);
        Assert.Equal("anon-secret", handler.ApiKey);
        Assert.Contains("full_name", handler.Body);
        Assert.DoesNotContain("role", handler.Body);
        Assert.DoesNotContain("status", handler.Body);
        Assert.Equal("/rest/v1/profiles?select=id,email,full_name,avatar_url,role,status,created_at,deleted_at,deleted_by&id=eq.user-1", handler.PathAndQuery);
    }

    [Fact]
    public async Task UserProfile_ReturnsNullWhenMissingAndRejectsEmptyUpdates()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        }));
        var repository = new SupabaseUserProfileRepository(CreateTransport(handler));

        Assert.Null(await repository.GetCurrentAsync("user-1", "caller-jwt"));
        Assert.Equal("Bearer caller-jwt", handler.Authorization);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateCurrentAsync(
            "user-1", "caller-jwt", new UpdateOwnProfile()));
    }

    [Fact]
    public async Task AdminProfiles_AreServiceScopedPagedAndMissingRowsAreNull()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Method == HttpMethod.Get && request.RequestUri!.Query.Contains("offset=5")
                ? "[{\"id\":\"user-1\",\"email\":\"u@example.test\",\"role\":\"admin\",\"status\":\"active\",\"created_at\":\"2025-01-02T03:04:05Z\"},{\"id\":\"user-2\",\"role\":\"user\",\"status\":\"inactive\",\"created_at\":\"2025-01-01T03:04:05Z\"}]"
                : "[]"),
        }));
        var repository = new SupabaseAdminProfileRepository(CreateTransport(handler));

        var page = await repository.ListAsync(new PageRequest(offset: 5, limit: 1));
        Assert.Single(page.Items);
        Assert.True(page.HasMore);
        Assert.Equal("user-1", page.Items[0].Id);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        Assert.Equal("service-secret", handler.ApiKey);
        Assert.Contains("order=created_at.desc,id.asc&offset=5&limit=2", handler.PathAndQuery);

        Assert.Null(await repository.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task AdminProfileWritesUseOnlyRequestedColumnsAndProviderErrorsAreSanitized()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("private database detail"),
        }));
        var repository = new SupabaseAdminProfileRepository(CreateTransport(handler));

        var exception = await Assert.ThrowsAnyAsync<DependencyFailureException>(() => repository.UpdateRoleAsync("user-1", "admin"));
        Assert.DoesNotContain("private database detail", exception.Message);
        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        Assert.Contains("\"role\":\"admin\"", handler.Body);
        Assert.DoesNotContain("status", handler.Body);
    }

    private static SupabaseTransport CreateTransport(RecordingHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler)), CreateOptions());

    private static SupabaseOptions CreateOptions() => new()
    {
        Url = "https://project.supabase.co",
        AnonKey = "anon-secret",
        ServiceRoleKey = "service-secret",
        DocumentsBucket = "cfc-docs",
        VideosBucket = "cfc-videos",
    };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal HttpMethod? Method { get; private set; }
        internal string? PathAndQuery { get; private set; }
        internal string? Authorization { get; private set; }
        internal string? ApiKey { get; private set; }
        internal string? Upsert { get; private set; }
        internal string? Prefer { get; private set; }
        internal string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            ApiKey = request.Headers.GetValues("apikey").Single();
            Upsert = request.Headers.TryGetValues("x-upsert", out var values) ? values.Single() : null;
            Prefer = request.Headers.TryGetValues("Prefer", out var preferValues) ? preferValues.Single() : null;
            if (request.Content is not null && request.Method != HttpMethod.Get)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return await send(request, cancellationToken);
        }
    }
}
