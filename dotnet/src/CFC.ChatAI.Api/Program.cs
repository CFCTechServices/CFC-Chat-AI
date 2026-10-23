using System.Text.Json;
using System.Text.Json.Serialization;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Features.Health;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Api:Urls"] ?? "http://127.0.0.1:5000");

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

var app = builder.Build();
app.MapHealth();
app.Run();

public partial class Program;
