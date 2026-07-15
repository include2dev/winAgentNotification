using System.Text;
using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class MessageParserTests
{
    private static ParseResult Parse(string json) =>
        MessageParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_ValidFullPayload_ReturnsMessage()
    {
        var result = Parse("""{"title":"Backup done","body":"took 12 minutes","level":"warning"}""");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.Warning);
        Assert.Equal("Backup done", result.Message!.Title);
        Assert.Equal("took 12 minutes", result.Message.Body);
        Assert.Equal(NotificationLevel.Warning, result.Message.Level);
    }

    [Fact]
    public void Parse_TitleOnly_DefaultsBodyEmptyAndLevelInfo()
    {
        var result = Parse("""{"title":"hi"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Message!.Body);
        Assert.Equal(NotificationLevel.Info, result.Message.Level);
    }

    [Fact]
    public void Parse_MissingTitle_Fails()
    {
        var result = Parse("""{"body":"no title here"}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("title", result.Error);
    }

    [Fact]
    public void Parse_WhitespaceTitle_Fails()
    {
        var result = Parse("""{"title":"   "}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("title", result.Error);
    }

    [Fact]
    public void Parse_InvalidJson_Fails()
    {
        var result = Parse("not json at all");

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid JSON", result.Error);
    }

    [Fact]
    public void Parse_NonObjectRoot_Fails()
    {
        var result = Parse("[1,2,3]");

        Assert.False(result.IsSuccess);
        Assert.Contains("object", result.Error);
    }

    [Fact]
    public void Parse_LevelIsCaseInsensitive()
    {
        var result = Parse("""{"title":"t","level":"CRITICAL"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Critical, result.Message!.Level);
    }

    [Fact]
    public void Parse_UnknownLevel_TreatedAsInfoWithWarning()
    {
        var result = Parse("""{"title":"t","level":"panic"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Info, result.Message!.Level);
        Assert.Contains("panic", result.Warning);
    }

    [Fact]
    public void Parse_NumericLevelString_TreatedAsInfoWithWarning()
    {
        var result = Parse("""{"title":"t","level":"5"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Info, result.Message!.Level);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Parse_ExtraFieldsAreIgnored()
    {
        var result = Parse("""{"title":"t","url":"https://example.com","actions":[1]}""");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Warning);
    }
}
