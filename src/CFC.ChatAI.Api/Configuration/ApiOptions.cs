namespace CFC.ChatAI.Api.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string Title { get; set; } = "CFC Animal Feed Software Chatbot API";
    public string Version { get; set; } = "1.0.0";
}
