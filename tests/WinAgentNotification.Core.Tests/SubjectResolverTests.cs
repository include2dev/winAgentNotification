using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class SubjectResolverTests
{
    [Fact]
    public void Resolve_ExpandsHostnameAndUsername()
    {
        var result = SubjectResolver.Resolve(
            new[] { "notify.all", "notify.host.{hostname}", "notify.user.{username}" },
            "DESKTOP-01", "Alice");

        Assert.Equal(
            new[] { "notify.all", "notify.host.desktop-01", "notify.user.alice" },
            result);
    }

    [Fact]
    public void SanitizeToken_LowercasesValue()
    {
        Assert.Equal("desktop-01", SubjectResolver.SanitizeToken("DESKTOP-01"));
    }

    [Theory]
    [InlineData("john.doe", "john-doe")]
    [InlineData("a b\tc", "a-b-c")]
    [InlineData("x*y>z", "x-y-z")]
    public void SanitizeToken_ReplacesInvalidCharacters(string input, string expected)
    {
        Assert.Equal(expected, SubjectResolver.SanitizeToken(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeToken_EmptyOrWhitespace_ReturnsUnknown(string input)
    {
        Assert.Equal("unknown", SubjectResolver.SanitizeToken(input));
    }

    [Fact]
    public void Resolve_RemovesDuplicates()
    {
        var result = SubjectResolver.Resolve(
            new[] { "notify.all", "notify.all" }, "h", "u");

        Assert.Single(result);
    }

    [Fact]
    public void Resolve_TrimsSurroundingWhitespaceInToken()
    {
        Assert.Equal("host", SubjectResolver.SanitizeToken("  HOST  "));
    }
}
