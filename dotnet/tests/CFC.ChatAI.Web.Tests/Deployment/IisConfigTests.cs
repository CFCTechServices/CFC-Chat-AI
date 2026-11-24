using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CFC.ChatAI.Web.Tests.Deployment;

// These assertions cover the checked-in routing policy, not native IIS normalization or ARR behavior.
public sealed class IisConfigTests
{
    private static readonly XElement Server = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Deployment", "iis-web.config"))
        .Root!.Element("location")!.Element("system.webServer")!;
    private static IEnumerable<XElement> Rules => Server.Element("rewrite")!.Element("rules")!.Elements("rule");

    [Fact]
    public void SiteUsesTheBlazorExecutableAndOnlyProxiesToTheDotNetApi()
    {
        var handler = Assert.Single(Server.Element("handlers")!.Elements("add"));
        Assert.Equal("AspNetCoreModuleV2", (string?)handler.Attribute("modules"));
        Assert.Equal("*", (string?)handler.Attribute("path"));
        Assert.Equal(@".\CFC.ChatAI.Web.exe", (string?)Server.Element("aspNetCore")!.Attribute("processPath"));
        var filtering = Server.Element("security")!.Element("requestFiltering")!;
        Assert.Equal("false", (string?)filtering.Attribute("allowDoubleEscaping"));
        Assert.Equal(51 * 1024 * 1024, (int?)filtering.Element("requestLimits")!.Attribute("maxAllowedContentLength"));

        foreach (var rule in Rules)
        {
            Assert.Equal("true", (string?)rule.Attribute("stopProcessing"));
            var action = rule.Element("action")!;
            if ((string?)action.Attribute("type") == "Rewrite")
            {
                Assert.Equal("http://127.0.0.1:5000/{R:0}", (string?)action.Attribute("url"));
                Assert.Equal("true", (string?)action.Attribute("appendQueryString"));
                Assert.Equal("{REQUEST_METHOD}", (string?)rule.Element("conditions")!.Element("add")!.Attribute("input"));
            }
            else Assert.Equal("404", (string?)action.Attribute("statusCode"));
        }
    }

    [Theory]
    [InlineData("GET", "api/health", "Rewrite")]
    [InlineData("GET", "api/chat/content/images/docs/!u-a/images/a.png", "Rewrite")]
    [InlineData("GET", "content/images/docs/a/images/a.png", "Rewrite")]
    [InlineData("POST", "api/chat/message", "Rewrite")]
    [InlineData("POST", "api/files/upload", "Rewrite")]
    [InlineData("PATCH", "api/profile/me", "Rewrite")]
    [InlineData("PUT", "api/admin/documents/doc/replace", "Rewrite")]
    [InlineData("DELETE", "api/admin/documents/doc", "Rewrite")]
    [InlineData("GET", "api/health/detailed", "CustomResponse")]
    [InlineData("POST", "API/Ingest/DEV/", "CustomResponse")]
    [InlineData("DELETE", "api/ingest/dev/item", "CustomResponse")]
    [InlineData("POST", "api/health", "CustomResponse")]
    [InlineData("POST", "api/videos/upload", "CustomResponse")]
    [InlineData("POST", "api/ingest/document", "CustomResponse")]
    [InlineData("GET", "", null)]
    [InlineData("GET", "ui/", null)]
    [InlineData("GET", "chat/session-id", null)]
    [InlineData("GET", "_framework/blazor.web.js", null)]
    [InlineData("GET", "_blazor", null)]
    [InlineData("POST", "_blazor/negotiate", null)]
    public void OnlyApprovedRoutesAreProxiedOtherRequestsCanReachBlazor(string method, string path, string? expected)
    {
        var rule = Rules.FirstOrDefault(rule =>
            Regex.IsMatch(path, (string)rule.Element("match")!.Attribute("url")!, RegexOptions.IgnoreCase) &&
            ((string?)rule.Element("conditions")?.Element("add")?.Attribute("pattern") is not { } pattern || Regex.IsMatch(method, pattern)));
        Assert.Equal(expected, (string?)rule?.Element("action")?.Attribute("type"));
    }
}
