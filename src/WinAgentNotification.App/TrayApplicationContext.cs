using System.Windows.Forms;

namespace WinAgentNotification.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const int MaxTrayTextLength = 63;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ConnectionStateMonitor _monitor;
    private readonly string _serverUrl;
    private readonly SynchronizationContext _syncContext;

    public TrayApplicationContext(
        ConnectionStateMonitor monitor, string serverUrl, Action onExitRequested)
    {
        _monitor = monitor;
        _serverUrl = serverUrl;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _statusItem = new ToolStripMenuItem("Disconnected") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExitRequested());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Error,
            Text = "WinAgentNotification",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _monitor.ConnectionStateChanged += OnConnectionStateChanged;
        UpdateState(_monitor.IsConnected);
    }

    private void OnConnectionStateChanged(bool connected) =>
        _syncContext.Post(_ => UpdateState(connected), null);

    private void UpdateState(bool connected)
    {
        var stateText = connected ? "Connected" : "Disconnected";
        _statusItem.Text = $"{stateText} — {_serverUrl}";
        _trayIcon.Icon = connected
            ? System.Drawing.SystemIcons.Information
            : System.Drawing.SystemIcons.Error;
        _trayIcon.Text = Truncate(
            $"WinAgentNotification — {stateText} — {_serverUrl}", MaxTrayTextLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.ConnectionStateChanged -= OnConnectionStateChanged;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
