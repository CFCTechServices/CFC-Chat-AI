using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace CFC.ChatAI.Api.Tests;

public sealed class DotEnvStartupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServerLoadsOptionalDotEnvFromWorkingDirectory(bool createEnvFile)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cfc-dotenv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        if (createEnvFile)
            await File.WriteAllTextAsync(Path.Combine(directory, ".env"),
                $"# Test settings\nDOTNET_API_PORT={port}\nSUPABASE_URL=\"https://dotenv.example\"\nSUPABASE_ANON_KEY=file-key\nSUPABASE_SERVICE_ROLE_KEY=service-key\n");

        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.Environment.Remove("SUPABASE_URL");
        start.Environment.Remove("SUPABASE_SERVICE_ROLE_KEY");
        start.Environment.Remove("PINECONE_API_KEY");
        start.Environment.Remove("API_PORT");
        start.Environment.Remove("DOTNET_API_PORT");
        start.Environment["SUPABASE_ANON_KEY"] = "environment-key";
        if (!createEnvFile)
            start.Environment["DOTNET_API_PORT"] = port.ToString();

        using var process = Process.Start(start)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage? response = null;
            for (var i = 0; i < 100 && response is null && !process.HasExited; i++)
            {
                try { response = await client.GetAsync(createEnvFile ? "/api/auth/config" : "/api/health"); }
                catch (HttpRequestException) { await Task.Delay(100); }
            }

            Assert.NotNull(response);
            using (response)
            {
                response.EnsureSuccessStatusCode();
                if (createEnvFile)
                {
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal("https://dotenv.example", json.GetProperty("supabaseUrl").GetString());
                    Assert.Equal("environment-key", json.GetProperty("supabaseKey").GetString());
                }
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
