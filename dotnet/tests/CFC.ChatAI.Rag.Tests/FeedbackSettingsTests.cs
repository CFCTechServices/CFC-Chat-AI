namespace CFC.ChatAI.Rag.Tests;

public class FeedbackSettingsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return key => map.GetValueOrDefault(key);
    }

    [Fact]
    public void NothingSet_UsesPythonDefaults()
    {
        var settings = FeedbackSettings.FromEnvironment(Env());

        Assert.True(settings.Enabled);
        Assert.Equal(0.3, settings.Alpha);
        Assert.Equal(5.0, settings.Scale);
        Assert.Equal(0.15, settings.AlphaQuery);
        Assert.Equal(3.0, settings.ScaleQuery);
        Assert.Equal(0.75, settings.SimThreshold);
    }

    [Fact]
    public void ValuesAreReadFromEnvironment()
    {
        var settings = FeedbackSettings.FromEnvironment(Env(
            ("FEEDBACK_ALPHA", "0.5"), ("FEEDBACK_SCALE", "10"), ("FEEDBACK_ALPHA_QUERY", "0.2"),
            ("FEEDBACK_SCALE_QUERY", "2.5"), ("FEEDBACK_SIM_THRESHOLD", "0.8")));

        Assert.Equal(0.5, settings.Alpha);
        Assert.Equal(10.0, settings.Scale);
        Assert.Equal(0.2, settings.AlphaQuery);
        Assert.Equal(2.5, settings.ScaleQuery);
        Assert.Equal(0.8, settings.SimThreshold);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("", false)]      // Python: "" is not in ("true", "1", "yes") -> disabled
    public void Enabled_FollowsPythonRule(string value, bool expected)
    {
        var settings = FeedbackSettings.FromEnvironment(Env(("FEEDBACK_ENABLED", value)));

        Assert.Equal(expected, settings.Enabled);
    }

    [Fact]
    public void InvalidNumber_FailsLoudly()
    {
        Assert.Throws<FormatException>(() =>
            FeedbackSettings.FromEnvironment(Env(("FEEDBACK_ALPHA", "abc"))));
    }
}