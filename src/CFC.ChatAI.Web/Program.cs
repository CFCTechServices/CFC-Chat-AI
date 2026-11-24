using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Configuration;
using CFC.ChatAI.Web.Services.Auth;

namespace CFC.ChatAI.Web;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddChatApi(builder.Configuration);
        builder.Services.AddScoped<SupabaseSessionService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
            app.UseHttpsRedirection();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapGet("/docs", () => Results.LocalRedirect("~/chat"));
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }
}
