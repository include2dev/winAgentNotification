using System.Windows.Forms;

namespace WinAgentNotification.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public TrayApplicationContext(Action onExitRequested)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => onExitRequested());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WinAgentNotification",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
