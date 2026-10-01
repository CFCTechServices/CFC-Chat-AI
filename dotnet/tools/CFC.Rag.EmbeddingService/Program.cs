using CFC.Rag;

//Test bridge: exposes the C# embedder over HTTP so the existing Python app can call it
// Run from the dotnet/ folder: dotnet run --project tools/CFC.Rag.EmbeddingService

var builder = WebApplication.CreateBuilder(args);

// Default: dotnet/models, resolved from this project's folder so it works however the app is launched
var modelsDir = builder.Configuration["ModelsDir"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "models"));
builder.Services.AddSingleton(_ => new OnnxEmbeddingModel(
    Path.Combine(modelsDir, "model.onnx"),
    Path.Combine(modelsDir, "vocab.txt")));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", dimension = OnnxEmbeddingModel.Dimension}));

app.MapPost("/embed", (EmbedRequest req, OnnxEmbeddingModel model ) =>
    Results.Ok(new EmbedResponse(model.Encode(req.Texts))));

app.Run("http://localhost:5100");

record EmbedRequest(List<string> Texts);
record EmbedResponse(List<float[]> Embeddings);
