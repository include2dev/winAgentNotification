namespace WinAgentNotification.Core;

public sealed record ParseResult
{
    public NotificationMessage? Message { get; init; }

    public string? Error { get; init; }

    public string? Warning { get; init; }

    public bool IsSuccess => Message is not null;

    public static ParseResult Ok(NotificationMessage message, string? warning = null) =>
        new() { Message = message, Warning = warning };

    public static ParseResult Fail(string error) => new() { Error = error };
}
