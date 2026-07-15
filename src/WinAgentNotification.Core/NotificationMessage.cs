namespace WinAgentNotification.Core;

public sealed record NotificationMessage(string Title, string Body, NotificationLevel Level);
