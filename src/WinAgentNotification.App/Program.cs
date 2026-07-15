using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WinAgentNotification.Core;

namespace WinAgentNotification.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(
            initiallyOwned: true, @"Local\WinAgentNotification.SingleInstance", out var createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();

        IConfiguration configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var logDirectory = Environment.ExpandEnvironmentVariables(
                configuration["Logging:Directory"] ?? @"%LOCALAPPDATA%\WinAgentNotification\logs");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "agent-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WinAgentNotification failed to start: {ex.Message}",
                "WinAgentNotification",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            Log.Information("WinAgentNotification starting");

            using var host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(builder => builder.AddConfiguration(configuration))
                .ConfigureServices(services => ConfigureServices(services, configuration))
                .Build();

            host.Start();

            var monitor = host.Services.GetRequiredService<ConnectionStateMonitor>();
            var natsSettings = host.Services.GetRequiredService<IOptions<NatsSettings>>().Value;

            using var trayContext = new TrayApplicationContext(
                monitor, natsSettings.Url, Application.Exit);
            Application.Run(trayContext);

            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error, shutting down");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NatsSettings>(configuration.GetSection("Nats"));
        services.AddSingleton<ConnectionStateMonitor>();
        services.AddSingleton<INatsCredentialsProvider, AnonymousCredentialsProvider>();
        services.AddSingleton<IToastNotifier, ToastNotifier>();
        services.AddHostedService<NatsSubscriberService>();
    }
}
