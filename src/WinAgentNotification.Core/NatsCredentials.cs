namespace WinAgentNotification.Core;

public sealed record NatsCredentials(
    string? Token, string? Username, string? Password, string? CredsFile = null)
{
    /// <summary>
    /// Builds credentials from raw configuration values. Returns null when
    /// every field is blank, which callers treat as an anonymous connection.
    /// </summary>
    public static NatsCredentials? CreateOrNull(
        string? token, string? username, string? password, string? credsFile)
    {
        var normalized = new NatsCredentials(
            NullIfBlank(token), NullIfBlank(username), NullIfBlank(password), NullIfBlank(credsFile));

        return normalized is { Token: null, Username: null, Password: null, CredsFile: null }
            ? null
            : normalized;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
