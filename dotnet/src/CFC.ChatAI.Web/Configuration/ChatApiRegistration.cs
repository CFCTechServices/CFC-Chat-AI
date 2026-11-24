using CFC.ChatAI.Web.Services.Admin;
using CFC.ChatAI.Web.Services.Api;
using CFC.ChatAI.Web.Services.Auth;

namespace CFC.ChatAI.Web.Configuration;

internal static class ChatApiRegistration
{
    internal static void AddChatApi(this IServiceCollection services, IConfiguration config)
    {
        var url = config["Backend:BaseUrl"] ?? "http://127.0.0.1:5000";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var backend) || backend.AbsolutePath != "/" ||
            !(backend.Scheme == "https" || (backend.Scheme == "http" && backend.IsLoopback)) ||
            backend.UserInfo.Length != 0 || backend.Query.Length != 0 || backend.Fragment.Length != 0)
            throw new InvalidOperationException("Backend:BaseUrl must be a root HTTPS or loopback HTTP URL without credentials, query or fragment.");

        var limit = config.GetValue<long?>("Document:MaxUploadBytes") ?? AdminUploadPolicy.DefaultMaxBytes;
        if (limit <= 0 || limit > long.MaxValue - 1024 * 1024)
            throw new InvalidOperationException("Document:MaxUploadBytes must be positive and leave room for multipart overhead.");

        services.AddSingleton(new AdminUploadPolicy(limit));
        services.AddHttpClient<ChatApiClient>(client =>
        {
            client.BaseAddress = backend;
            client.Timeout = TimeSpan.FromMinutes(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        services.AddScoped<IAuthenticationBackend>(sp => sp.GetRequiredService<ChatApiClient>());
    }
}
