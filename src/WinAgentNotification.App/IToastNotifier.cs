using WinAgentNotification.Core;

namespace WinAgentNotification.App;

public interface IToastNotifier
{
    void Show(NotificationMessage message);
}
