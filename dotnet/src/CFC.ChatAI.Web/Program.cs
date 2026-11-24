using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var fastApiBaseUrl = builder.Configuration["FastApi:BaseUrl"] ?? "http://localhost:8000";
if (!Uri.TryCreate(fastApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var fastApiBaseUri)
    || fastApiBaseUri.Scheme is not ("http" or "https")
    || fastApiBaseUri.UserInfo.Length > 0 || fastApiBaseUri.Query.Length > 0 || fastApiBaseUri.Fragment.Length > 0)
{
    throw new InvalidOperationException("FastApi:BaseUrl must be an absolute HTTP(S) base URL without credentials, query, or fragment.");
}

builder.Services.AddSingleton(new AdminUploadPolicy(builder.Configuration["AdminUploads:VerifiedBackendUrl"],
    builder.Configuration.GetValue<long?>("AdminUploads:MaxFileBytes") ?? AdminUploadPolicy.DefaultMaxBytes));
builder.Services.AddHttpClient<FastApiClient>(client => client.BaseAddress = fastApiBaseUri)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddKeyedTransient<FastApiClient>(FastApiClient.UploadClientKey, (services, _) =>
{
    // Reuse the typed client's base URL and non-redirecting handler. Video transcription can take minutes.
    var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(FastApiClient));
    http.Timeout = TimeSpan.FromMinutes(30);
    return new FastApiClient(http, services.GetRequiredService<AdminUploadPolicy>());
});
builder.Services.AddScoped<IAuthenticationBackend>(services => services.GetRequiredService<FastApiClient>());
builder.Services.AddScoped<SupabaseSessionService>();
builder.Services.AddSingleton(TimeProvider.System);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/docs", () => Results.LocalRedirect("~/chat"));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
