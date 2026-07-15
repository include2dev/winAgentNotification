using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using WinAgentNotification.Core;

namespace WinAgentNotification.App;

public sealed class NatsSubscriberService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<NatsSubscriberService> _logger;
    private readonly NatsSettings _settings;
    private readonly IToastNotifier _notifier;
    private readonly ConnectionStateMonitor _monitor;
    private readonly INatsCredentialsProvider _credentialsProvider;

    public NatsSubscriberService(
        ILogger<NatsSubscriberService> logger,
        IOptions<NatsSettings> settings,
        IToastNotifier notifier,
        ConnectionStateMonitor monitor,
        INatsCredentialsProvider credentialsProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _notifier = notifier;
        _monitor = monitor;
        _credentialsProvider = credentialsProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subjects = SubjectResolver.Resolve(
            _settings.Subjects, Environment.MachineName, Environment.UserName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(subjects, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _monitor.SetConnected(false);
                _logger.LogWarning(ex, "NATS connection failed; retrying in {Delay}s", RetryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _monitor.SetConnected(false);
    }

    private async Task RunConnectionAsync(IReadOnlyList<string> subjects, CancellationToken ct)
    {
        var credentials = await _credentialsProvider.GetCredentialsAsync(ct);
        var authOpts = credentials is null
            ? NatsAuthOpts.Default
            : NatsAuthOpts.Default with
            {
                Token = credentials.Token,
                Username = credentials.Username,
                Password = credentials.Password,
            };

        var opts = NatsOpts.Default with
        {
            Url = _settings.Url,
            Name = "WinAgentNotification",
            MaxReconnectRetry = -1,
            AuthOpts = authOpts,
        };

        await using var connection = new NatsConnection(opts);
        connection.ConnectionOpened += (_, _) =>
        {
            _monitor.SetConnected(true);
            return ValueTask.CompletedTask;
        };
        connection.ConnectionDisconnected += (_, _) =>
        {
            _monitor.SetConnected(false);
            return ValueTask.CompletedTask;
        };

        await connection.ConnectAsync();
        _logger.LogInformation(
            "Connected to {Url}, subscribing to: {Subjects}", _settings.Url, string.Join(", ", subjects));

        var loops = subjects
            .Select(subject => SubscribeLoopAsync(connection, subject, ct))
            .ToArray();
        await Task.WhenAll(loops);
    }

    private async Task SubscribeLoopAsync(NatsConnection connection, string subject, CancellationToken ct)
    {
        await foreach (var msg in connection.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
        {
            HandleMessage(subject, msg.Data);
        }
    }

    private void HandleMessage(string subject, byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            _logger.LogWarning("Dropping empty payload on subject {Subject}", subject);
            return;
        }

        var result = MessageParser.Parse(payload);
        if (!result.IsSuccess)
        {
            var raw = Encoding.UTF8.GetString(payload);
            _logger.LogWarning(
                "Dropping bad message on {Subject}: {Error}; payload: {Payload}",
                subject, result.Error, raw.Length <= 500 ? raw : raw[..500]);
            return;
        }

        if (result.Warning is not null)
            _logger.LogWarning("Message on {Subject}: {Warning}", subject, result.Warning);

        _notifier.Show(result.Message!);
    }
}
