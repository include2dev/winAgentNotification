namespace WinAgentNotification.App;

public sealed class NatsSettings
{
    public string Url { get; set; } = "nats://localhost:4222";

    public string[] Subjects { get; set; } =
        ["notify.all", "notify.host.{hostname}", "notify.user.{username}"];
}
