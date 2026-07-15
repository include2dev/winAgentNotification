using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class AnonymousCredentialsProviderTests
{
    [Fact]
    public async Task GetCredentialsAsync_ReturnsNull()
    {
        var provider = new AnonymousCredentialsProvider();

        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        Assert.Null(credentials);
    }
}
