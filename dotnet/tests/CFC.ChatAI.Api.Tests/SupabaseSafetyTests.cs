using System.Collections.Concurrent;
using System.Net;
using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Models;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Infrastructure.Supabase;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Api.Tests;

public sealed class SupabaseSafetyTests
{
    [Fact]
    public void Options_LegacyMapping_HierarchicalPrecedence_IndependentTimeout()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SUPABASE_URL"] = "https://legacy.example", ["Supabase:Url"] = "https://preferred.example",
            ["SUPABASE_ANON_KEY"] = "anon", ["SUPABASE_SERVICE_ROLE_KEY"] = "private",
            ["FRONTEND_BASE_URL"] = "http://localhost:1234/ui/", ["Supabase:RequestTimeoutSeconds"] = "0.25",
            ["Diagnostics:RequestTimeoutSeconds"] = "60"
        }).Build();
        var options = SupabaseOptions.FromConfiguration(config);
        Assert.Equal("https://preferred.example", options.Url);
        Assert.Equal("anon", options.AnonKey);
        Assert.Equal("private", options.ServiceRoleKey);
        Assert.Equal("http://localhost:1234/ui/", options.FrontendBaseUrl);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.RequestTimeout);
        var defaults = SupabaseOptions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal("http://localhost:8000/ui", defaults.FrontendBaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(5), defaults.RequestTimeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("61")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("bad")]
    public void Options_RejectUnboundedDeadlines(string seconds)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Supabase:RequestTimeoutSeconds"] = seconds }).Build();
        Assert.Throws<InvalidOperationException>(() => SupabaseOptions.FromConfiguration(config));
    }

    private static WebApplicationFactory<Program> Factory(HttpMessageHandler handler, Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<SupabaseOptions>();
            services.AddSingleton(new SupabaseOptions { Url = "https://example.supabase.co", AnonKey = "private-anon-marker",
                ServiceRoleKey = "private-service-marker", RequestTimeout = TimeSpan.FromMilliseconds(80) });
            services.AddHttpClient(SupabaseRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            configure?.Invoke(services);
        }));

    [Fact]
    public async Task Deadline_CoversResponseBodyParsing_AndCallerCancellationPropagates()
    {
        using var factory = Factory(new BodyHandler());
        var identities = factory.Services.GetRequiredService<IIdentityGateway>();
        var result = await identities.ValidateAsync("private-token-marker", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Outcome.Unavailable, result.Outcome);
        using var ct = new CancellationTokenSource();
        var pending = identities.ValidateAsync("private-token-marker", ct.Token);
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Logs_ContainClassificationButNotSecretsProviderBodiesOrPii()
    {
        using var logs = new CaptureLogs();
        using var factory = Factory(new FailingHandler(), services => services.AddSingleton<ILoggerProvider>(logs));
        var client = factory.CreateClient();
        await client.PostAsync("/api/auth/check-email", new StringContent("{\"email\":\"private-email-marker@example.com\"}", System.Text.Encoding.UTF8, "application/json"));
        await client.PostAsync("/api/auth/forgot-password", new StringContent("{\"email\":\"private-email-marker@example.com\"}", System.Text.Encoding.UTF8, "application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/profile/me");
        request.Headers.Authorization = new("Bearer", "private-token-marker");
        request.Content = new StringContent("{\"full_name\":\"private-name-marker\",\"avatar_url\":\"private-avatar-marker\"}", System.Text.Encoding.UTF8, "application/json");
        await client.SendAsync(request);
        var messages = string.Join('\n', logs.Messages);
        Assert.Contains("Invitation eligibility lookup failed: Unavailable", messages);
        foreach (var secret in new[] { "private-email-marker", "private-token-marker", "private-anon-marker", "private-service-marker",
            "private-provider-body-marker", "private-name-marker", "private-avatar-marker" })
            Assert.DoesNotContain(secret, messages);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            { Content = new StringContent("private-provider-body-marker") });
    }
    private sealed class BodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) });
    }
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class CaptureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Messages);
        public void Dispose() { }
        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
}
