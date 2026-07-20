using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class NatsCredentialsTests
{
    [Fact]
    public void CreateOrNull_AllEmpty_ReturnsNull()
    {
        Assert.Null(NatsCredentials.CreateOrNull(null, null, null, null));
        Assert.Null(NatsCredentials.CreateOrNull("", "  ", "", "   "));
    }

    [Fact]
    public void CreateOrNull_TokenOnly_ReturnsTokenCredentials()
    {
        var credentials = NatsCredentials.CreateOrNull("s3cret", null, null, null);

        Assert.NotNull(credentials);
        Assert.Equal("s3cret", credentials!.Token);
        Assert.Null(credentials.Username);
        Assert.Null(credentials.Password);
        Assert.Null(credentials.CredsFile);
    }

    [Fact]
    public void CreateOrNull_CredsFileOnly_ReturnsCredsFileCredentials()
    {
        var credentials = NatsCredentials.CreateOrNull(null, null, null, @"C:\secrets\agent.creds");

        Assert.NotNull(credentials);
        Assert.Equal(@"C:\secrets\agent.creds", credentials!.CredsFile);
        Assert.Null(credentials.Token);
    }

    [Fact]
    public void CreateOrNull_BlankFieldsAreNormalizedToNull()
    {
        var credentials = NatsCredentials.CreateOrNull("  ", "user", "", null);

        Assert.NotNull(credentials);
        Assert.Null(credentials!.Token);
        Assert.Equal("user", credentials.Username);
        Assert.Null(credentials.Password);
    }
}
