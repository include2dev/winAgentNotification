using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using WinAgentNotification.Core;

namespace WinAgentNotification.App;

public sealed class ToastNotifier : IToastNotifier
{
    private readonly ILogger<ToastNotifier> _logger;

    public ToastNotifier(ILogger<ToastNotifier> logger)
    {
        _logger = logger;
    }

    public void Show(NotificationMessage message)
    {
        try
        {
            var title = message.Level == NotificationLevel.Warning
                ? "⚠ " + message.Title
                : message.Title;

            var builder = new ToastContentBuilder().AddText(title);

            if (!string.IsNullOrEmpty(message.Body))
                builder.AddText(message.Body);

            if (message.Level == NotificationLevel.Critical)
                builder.SetToastDuration(ToastDuration.Long);

            builder.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show toast for '{Title}'", message.Title);
        }
    }
}
