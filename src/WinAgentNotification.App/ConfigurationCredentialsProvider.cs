using Microsoft.Extensions.Options;
using WinAgentNotification.Core;

namespace WinAgentNotification.App;

/// <summary>
/// Supplies NATS credentials from the Nats:Auth configuration section
/// (appsettings.json or environment variables such as Nats__Auth__Token).
/// Falls back to anonymous (null) when the section is empty, so a config
/// without an Auth block behaves exactly like the original POC.
/// </summary>
public sealed class ConfigurationCredentialsProvider : INatsCredentialsProvider
{
    private readonly NatsAuthSettings _auth;

    public ConfigurationCredentialsProvider(IOptions<NatsSettings> settings)
    {
        _auth = settings.Value.Auth;
    }

    public ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var credsFile = string.IsNullOrWhiteSpace(_auth.CredsFile)
            ? null
            : Environment.ExpandEnvironmentVariables(_auth.CredsFile);

        return ValueTask.FromResult(
            NatsCredentials.CreateOrNull(_auth.Token, _auth.Username, _auth.Password, credsFile));
    }
}
