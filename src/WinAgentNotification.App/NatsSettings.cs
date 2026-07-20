namespace WinAgentNotification.App;

public sealed class NatsSettings
{
    public string Url { get; set; } = "nats://localhost:4222";

    public string[] Subjects { get; set; } =
        ["notify.all", "notify.host.{hostname}", "notify.user.{username}"];

    public NatsAuthSettings Auth { get; set; } = new();
}

/// <summary>
/// Optional authentication values for the NATS connection. Leave every field
/// empty for an anonymous connection. Exactly one mechanism should be set:
/// a .creds file path, a token, or username/password.
/// </summary>
public sealed class NatsAuthSettings
{
    public string? CredsFile { get; set; }

    public string? Token { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}
