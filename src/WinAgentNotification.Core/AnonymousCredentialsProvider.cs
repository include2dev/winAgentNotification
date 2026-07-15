namespace WinAgentNotification.Core;

public sealed class AnonymousCredentialsProvider : INatsCredentialsProvider
{
    public ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<NatsCredentials?>(null);
}
