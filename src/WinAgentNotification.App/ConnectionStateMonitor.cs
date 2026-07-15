namespace WinAgentNotification.App;

public sealed class ConnectionStateMonitor
{
    private readonly object _gate = new();

    public bool IsConnected { get; private set; }

    public event Action<bool>? ConnectionStateChanged;

    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            if (IsConnected == connected)
                return;
            IsConnected = connected;
        }

        ConnectionStateChanged?.Invoke(connected);
    }
}
