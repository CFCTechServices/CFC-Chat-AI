using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CFC.ChatAI.Api.Tests;

public sealed class IisRoutingTests
{
    private static XElement Server => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "iis-web.config"))
        .Root!.Element("system.webServer")!;

    [Theory]
    [InlineData("GET", "api/health", "DotNet_Health")]
    [InlineData("GET", "api/health/detailed", "DotNet_HealthDetailed")]
    [InlineData("GET", "api/auth/config", "DotNet_AuthConfig")]
    [InlineData("GET", "api/visibility/vector-store", "DotNet_VectorStoreVisibility")]
    [InlineData("POST", "api/auth/forgot-password", "DotNet_AuthForgotPassword")]
    [InlineData("POST", "api/auth/check-email", "DotNet_AuthCheckEmail")]
    [InlineData("GET", "api/profile/me", "DotNet_ProfileMeGet")]
    [InlineData("PATCH", "api/profile/me", "DotNet_ProfileMePatch")]
    [InlineData("POST", "api/profile/complete", "DotNet_ProfileComplete")]
    [InlineData("DELETE", "api/profile/me", "ReverseProxyAPI")]
    [InlineData("POST", "api/profile/me", "ReverseProxyAPI")]
    [InlineData("GET", "api/profile/complete", "ReverseProxyAPI")]
    [InlineData("GET", "api/auth/forgot-password", "ReverseProxyAPI")]
    [InlineData("GET", "api/auth/check-email", "ReverseProxyAPI")]
    [InlineData("POST", "api/auth/config", "ReverseProxyAPI")]
    [InlineData("GET", "api/admin/users", "ReverseProxyAPI")]
    [InlineData("GET", "api/profile/me/extra", "ReverseProxyAPI")]
    [InlineData("POST", "api/auth/login", "ReverseProxyAPI")]
    public void ExactPathsAndMethodsRouteBeforePythonFallback(string method, string path, string expected)
    {
        var rule = Server.Element("rewrite")!.Element("rules")!.Elements("rule").First(r =>
            Regex.IsMatch(path, (string)r.Element("match")!.Attribute("url")!) &&
            (r.Element("conditions")?.Elements("add").All(c =>
                (string?)c.Attribute("input") == "{REQUEST_METHOD}" &&
                Regex.IsMatch(method, (string)c.Attribute("pattern")!)) ?? true));
        Assert.Equal(expected, (string?)rule.Attribute("name"));
        Assert.Equal("true", (string?)rule.Attribute("stopProcessing"));
        var action = rule.Element("action")!;
        Assert.Equal("Rewrite", (string?)action.Attribute("type"));
        if (expected.StartsWith("DotNet_"))
        {
            Assert.Equal("http://127.0.0.1:5000/" + path, (string?)action.Attribute("url"));
            Assert.NotEqual("false", (string?)action.Attribute("appendQueryString"));
        }
        else
            Assert.Equal("http://127.0.0.1:8000/api/{R:1}", (string?)action.Attribute("url"));
    }

    [Fact]
    public void BackendErrorBodiesPassThroughIis()
    {
        Assert.Equal("PassThrough", (string?)Server.Element("httpErrors")!.Attribute("existingResponse"));
    }
}
