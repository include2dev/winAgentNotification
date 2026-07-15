namespace WinAgentNotification.Core;

/// <summary>
/// Supplies NATS credentials at (re)connect time. The POC uses the anonymous
/// implementation; a future implementation can exchange a user token for a
/// NATS token without touching connection code.
/// </summary>
public interface INatsCredentialsProvider
{
    ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken);
}
