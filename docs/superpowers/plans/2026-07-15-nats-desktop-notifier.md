# WinAgentNotification POC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A resident C# tray app for Windows workstations that subscribes to NATS subjects and shows a Windows toast notification for each valid message.

**Architecture:** WinForms `ApplicationContext` shell (tray icon only) hosting a .NET Generic Host. NATS subscription runs as a `BackgroundService`; parsing/subject logic lives in a cross-platform Core library so it can be unit-tested on Linux. Spec: `docs/superpowers/specs/2026-07-15-nats-desktop-notifier-design.md`.

**Tech Stack:** .NET 8 (LTS), C# 12, `NATS.Net`, `Microsoft.Toolkit.Uwp.Notifications`, `Microsoft.Extensions.Hosting`, Serilog (rolling file), xUnit.

## Global Constraints

- All code, comments, docs, and commit messages are written in English.
- `src/WinAgentNotification.Core` targets `net8.0` and MUST NOT reference any Windows-only API or package.
- `src/WinAgentNotification.App` targets `net8.0-windows10.0.17763.0` with `<UseWindowsForms>true</UseWindowsForms>`, `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, `<SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>`.
- The dev/CI environment is Linux: it can build everything and run Core tests, but cannot run the App or show toasts. Toast display is verified manually on Windows (Task 9 checklist).
- If `dotnet` was installed by Task 1 into `$HOME/.dotnet`, every shell must first run: `export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"`.
- All commands run from the repository root.
- POC exclusions (do NOT implement): authentication logic beyond the provider seam, JetStream, toast click actions/buttons, installer/auto-start, auto-update, localization.
- Message contract (from spec): JSON object; `title` required non-empty string; `body` optional string, default `""`; `level` optional `info|warning|critical` (case-insensitive), default `info`, unknown values treated as `info` and logged.
- Subject sanitization (from spec): lowercase; whitespace, `.`, `*`, `>` replaced with `-`.

---

### Task 1: Environment and solution scaffold

**Files:**
- Create: `WinAgentNotification.sln`
- Create: `src/WinAgentNotification.Core/WinAgentNotification.Core.csproj`
- Create: `tests/WinAgentNotification.Core.Tests/WinAgentNotification.Core.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: a building solution; `WinAgentNotification.Core` project referenced by the test project. Namespace root for the library is `WinAgentNotification.Core`.

- [ ] **Step 1: Install .NET 8 SDK if missing**

```bash
command -v dotnet >/dev/null 2>&1 || {
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0
}
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet --version
```

Expected: prints `8.0.x`.

- [ ] **Step 2: Create solution, Core library, and test project**

```bash
dotnet new sln -n WinAgentNotification
dotnet new classlib -n WinAgentNotification.Core -o src/WinAgentNotification.Core -f net8.0
rm src/WinAgentNotification.Core/Class1.cs
dotnet new xunit -n WinAgentNotification.Core.Tests -o tests/WinAgentNotification.Core.Tests -f net8.0
dotnet add tests/WinAgentNotification.Core.Tests reference src/WinAgentNotification.Core
dotnet sln add src/WinAgentNotification.Core tests/WinAgentNotification.Core.Tests
dotnet new gitignore
```

Expected: each command reports success; `.gitignore` created at repo root.

- [ ] **Step 3: Build and run the template test**

```bash
dotnet build && dotnet test
```

Expected: `Build succeeded.` and `Passed! - Failed: 0, Passed: 1` (the template `UnitTest1`).

- [ ] **Step 4: Commit**

```bash
git add .gitignore WinAgentNotification.sln src tests
git commit -m "chore: scaffold solution with Core library and test project"
```

---

### Task 2: NotificationMessage model and MessageParser

**Files:**
- Create: `src/WinAgentNotification.Core/NotificationLevel.cs`
- Create: `src/WinAgentNotification.Core/NotificationMessage.cs`
- Create: `src/WinAgentNotification.Core/ParseResult.cs`
- Create: `src/WinAgentNotification.Core/MessageParser.cs`
- Create: `tests/WinAgentNotification.Core.Tests/MessageParserTests.cs`
- Delete: `tests/WinAgentNotification.Core.Tests/UnitTest1.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 7):
  - `enum NotificationLevel { Info, Warning, Critical }`
  - `sealed record NotificationMessage(string Title, string Body, NotificationLevel Level)`
  - `ParseResult` with `NotificationMessage? Message`, `string? Error`, `string? Warning`, `bool IsSuccess`
  - `static ParseResult MessageParser.Parse(ReadOnlyMemory<byte> payload)`

- [ ] **Step 1: Write failing tests**

Create `tests/WinAgentNotification.Core.Tests/MessageParserTests.cs`:

```csharp
using System.Text;
using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class MessageParserTests
{
    private static ParseResult Parse(string json) =>
        MessageParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_ValidFullPayload_ReturnsMessage()
    {
        var result = Parse("""{"title":"Backup done","body":"took 12 minutes","level":"warning"}""");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.Warning);
        Assert.Equal("Backup done", result.Message!.Title);
        Assert.Equal("took 12 minutes", result.Message.Body);
        Assert.Equal(NotificationLevel.Warning, result.Message.Level);
    }

    [Fact]
    public void Parse_TitleOnly_DefaultsBodyEmptyAndLevelInfo()
    {
        var result = Parse("""{"title":"hi"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Message!.Body);
        Assert.Equal(NotificationLevel.Info, result.Message.Level);
    }

    [Fact]
    public void Parse_MissingTitle_Fails()
    {
        var result = Parse("""{"body":"no title here"}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("title", result.Error);
    }

    [Fact]
    public void Parse_WhitespaceTitle_Fails()
    {
        var result = Parse("""{"title":"   "}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("title", result.Error);
    }

    [Fact]
    public void Parse_InvalidJson_Fails()
    {
        var result = Parse("not json at all");

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid JSON", result.Error);
    }

    [Fact]
    public void Parse_NonObjectRoot_Fails()
    {
        var result = Parse("[1,2,3]");

        Assert.False(result.IsSuccess);
        Assert.Contains("object", result.Error);
    }

    [Fact]
    public void Parse_LevelIsCaseInsensitive()
    {
        var result = Parse("""{"title":"t","level":"CRITICAL"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Critical, result.Message!.Level);
    }

    [Fact]
    public void Parse_UnknownLevel_TreatedAsInfoWithWarning()
    {
        var result = Parse("""{"title":"t","level":"panic"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Info, result.Message!.Level);
        Assert.Contains("panic", result.Warning);
    }

    [Fact]
    public void Parse_NumericLevelString_TreatedAsInfoWithWarning()
    {
        var result = Parse("""{"title":"t","level":"5"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationLevel.Info, result.Message!.Level);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Parse_ExtraFieldsAreIgnored()
    {
        var result = Parse("""{"title":"t","url":"https://example.com","actions":[1]}""");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Warning);
    }
}
```

Delete the template test:

```bash
rm tests/WinAgentNotification.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test 2>&1 | tail -5
```

Expected: build FAILS with `CS0246` (type `MessageParser` / `ParseResult` not found). That is the expected "red" state.

- [ ] **Step 3: Implement the model and parser**

Create `src/WinAgentNotification.Core/NotificationLevel.cs`:

```csharp
namespace WinAgentNotification.Core;

public enum NotificationLevel
{
    Info,
    Warning,
    Critical,
}
```

Create `src/WinAgentNotification.Core/NotificationMessage.cs`:

```csharp
namespace WinAgentNotification.Core;

public sealed record NotificationMessage(string Title, string Body, NotificationLevel Level);
```

Create `src/WinAgentNotification.Core/ParseResult.cs`:

```csharp
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
```

Create `src/WinAgentNotification.Core/MessageParser.cs`:

```csharp
using System.Text.Json;

namespace WinAgentNotification.Core;

public static class MessageParser
{
    public static ParseResult Parse(ReadOnlyMemory<byte> payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ParseResult.Fail("payload is not a JSON object");

            if (!root.TryGetProperty("title", out var titleElement)
                || titleElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                return ParseResult.Fail("missing required field 'title'");
            }

            var title = titleElement.GetString()!;

            var body = root.TryGetProperty("body", out var bodyElement)
                       && bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString()!
                : string.Empty;

            var level = NotificationLevel.Info;
            string? warning = null;
            if (root.TryGetProperty("level", out var levelElement)
                && levelElement.ValueKind == JsonValueKind.String)
            {
                var raw = levelElement.GetString()!;
                if (Enum.TryParse<NotificationLevel>(raw, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                    && !char.IsDigit(raw[0]))
                {
                    level = parsed;
                }
                else
                {
                    warning = $"unknown level '{raw}', treated as info";
                }
            }

            return ParseResult.Ok(new NotificationMessage(title, body, level), warning);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 10`.

- [ ] **Step 5: Commit**

```bash
git add src/WinAgentNotification.Core tests/WinAgentNotification.Core.Tests
git commit -m "feat: add notification message model and JSON parser"
```

---

### Task 3: SubjectResolver

**Files:**
- Create: `src/WinAgentNotification.Core/SubjectResolver.cs`
- Create: `tests/WinAgentNotification.Core.Tests/SubjectResolverTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 7):
  - `static IReadOnlyList<string> SubjectResolver.Resolve(IEnumerable<string> templates, string hostname, string username)`
  - `static string SubjectResolver.SanitizeToken(string value)`

- [ ] **Step 1: Write failing tests**

Create `tests/WinAgentNotification.Core.Tests/SubjectResolverTests.cs`:

```csharp
using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class SubjectResolverTests
{
    [Fact]
    public void Resolve_ExpandsHostnameAndUsername()
    {
        var result = SubjectResolver.Resolve(
            new[] { "notify.all", "notify.host.{hostname}", "notify.user.{username}" },
            "DESKTOP-01", "Alice");

        Assert.Equal(
            new[] { "notify.all", "notify.host.desktop-01", "notify.user.alice" },
            result);
    }

    [Fact]
    public void SanitizeToken_LowercasesValue()
    {
        Assert.Equal("desktop-01", SubjectResolver.SanitizeToken("DESKTOP-01"));
    }

    [Theory]
    [InlineData("john.doe", "john-doe")]
    [InlineData("a b\tc", "a-b-c")]
    [InlineData("x*y>z", "x-y-z")]
    public void SanitizeToken_ReplacesInvalidCharacters(string input, string expected)
    {
        Assert.Equal(expected, SubjectResolver.SanitizeToken(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeToken_EmptyOrWhitespace_ReturnsUnknown(string input)
    {
        Assert.Equal("unknown", SubjectResolver.SanitizeToken(input));
    }

    [Fact]
    public void Resolve_RemovesDuplicates()
    {
        var result = SubjectResolver.Resolve(
            new[] { "notify.all", "notify.all" }, "h", "u");

        Assert.Single(result);
    }

    [Fact]
    public void Resolve_TrimsSurroundingWhitespaceInToken()
    {
        Assert.Equal("host", SubjectResolver.SanitizeToken("  HOST  "));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test 2>&1 | tail -5
```

Expected: build FAILS with `CS0246` (`SubjectResolver` not found).

- [ ] **Step 3: Implement SubjectResolver**

Create `src/WinAgentNotification.Core/SubjectResolver.cs`:

```csharp
namespace WinAgentNotification.Core;

public static class SubjectResolver
{
    public static IReadOnlyList<string> Resolve(
        IEnumerable<string> templates, string hostname, string username)
    {
        var host = SanitizeToken(hostname);
        var user = SanitizeToken(username);

        return templates
            .Select(t => t.Replace("{hostname}", host).Replace("{username}", user))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
    }

    public static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var sanitized = value.Trim().ToLowerInvariant()
            .Select(c => char.IsWhiteSpace(c) || c is '.' or '*' or '>' ? '-' : c);

        return new string(sanitized.ToArray());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 19` (10 from Task 2 + 9 here, counting Theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/WinAgentNotification.Core/SubjectResolver.cs tests/WinAgentNotification.Core.Tests/SubjectResolverTests.cs
git commit -m "feat: add subject resolver with placeholder expansion and sanitization"
```

---

### Task 4: Credentials provider seam

**Files:**
- Create: `src/WinAgentNotification.Core/NatsCredentials.cs`
- Create: `src/WinAgentNotification.Core/INatsCredentialsProvider.cs`
- Create: `src/WinAgentNotification.Core/AnonymousCredentialsProvider.cs`
- Create: `tests/WinAgentNotification.Core.Tests/AnonymousCredentialsProviderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 7):
  - `sealed record NatsCredentials(string? Token, string? Username, string? Password)`
  - `interface INatsCredentialsProvider { ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken); }`
  - `sealed class AnonymousCredentialsProvider : INatsCredentialsProvider` — always returns `null` (anonymous connection).

- [ ] **Step 1: Write failing test**

Create `tests/WinAgentNotification.Core.Tests/AnonymousCredentialsProviderTests.cs`:

```csharp
using WinAgentNotification.Core;
using Xunit;

namespace WinAgentNotification.Core.Tests;

public class AnonymousCredentialsProviderTests
{
    [Fact]
    public async Task GetCredentialsAsync_ReturnsNull()
    {
        var provider = new AnonymousCredentialsProvider();

        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        Assert.Null(credentials);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test 2>&1 | tail -5
```

Expected: build FAILS with `CS0246` (`AnonymousCredentialsProvider` not found).

- [ ] **Step 3: Implement the seam**

Create `src/WinAgentNotification.Core/NatsCredentials.cs`:

```csharp
namespace WinAgentNotification.Core;

public sealed record NatsCredentials(string? Token, string? Username, string? Password);
```

Create `src/WinAgentNotification.Core/INatsCredentialsProvider.cs`:

```csharp
namespace WinAgentNotification.Core;

/// <summary>
/// Supplies NATS credentials at (re)connect time. The POC uses the anonymous
/// implementation; a future implementation can exchange a user token for a
/// NATS token without touching connection code.
/// </summary>
public interface INatsCredentialsProvider
{
    ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken);
}
```

Create `src/WinAgentNotification.Core/AnonymousCredentialsProvider.cs`:

```csharp
namespace WinAgentNotification.Core;

public sealed class AnonymousCredentialsProvider : INatsCredentialsProvider
{
    public ValueTask<NatsCredentials?> GetCredentialsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<NatsCredentials?>(null);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 20`.

- [ ] **Step 5: Commit**

```bash
git add src/WinAgentNotification.Core tests/WinAgentNotification.Core.Tests
git commit -m "feat: add credentials provider seam with anonymous POC implementation"
```

---

### Task 5: App project scaffold (tray shell + host bootstrap)

**Files:**
- Create: `src/WinAgentNotification.App/WinAgentNotification.App.csproj`
- Create: `src/WinAgentNotification.App/Program.cs`
- Create: `src/WinAgentNotification.App/TrayApplicationContext.cs`
- Create: `src/WinAgentNotification.App/appsettings.json`

**Interfaces:**
- Consumes: `WinAgentNotification.Core` project reference.
- Produces (used by Tasks 6–8): App project with namespace `WinAgentNotification.App`; `Program.ConfigureServices(IServiceCollection, IConfiguration)` as the single DI registration point; `TrayApplicationContext(Action onExitRequested)` (constructor is extended in Task 8).

- [ ] **Step 1: Create the project file**

Create `src/WinAgentNotification.App/WinAgentNotification.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <AssemblyName>WinAgentNotification</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinAgentNotification.Core\WinAgentNotification.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add NuGet packages and register in solution**

```bash
dotnet sln add src/WinAgentNotification.App
dotnet add src/WinAgentNotification.App package NATS.Net
dotnet add src/WinAgentNotification.App package Microsoft.Toolkit.Uwp.Notifications
dotnet add src/WinAgentNotification.App package Microsoft.Extensions.Hosting --version '8.*'
dotnet add src/WinAgentNotification.App package Serilog.Extensions.Hosting
dotnet add src/WinAgentNotification.App package Serilog.Sinks.File
```

Expected: each `add package` reports the package was added. If a package restore fails on the Linux proxy, retry once before investigating.

- [ ] **Step 3: Create appsettings.json**

Create `src/WinAgentNotification.App/appsettings.json`:

```json
{
  "Nats": {
    "Url": "nats://localhost:4222",
    "Subjects": [ "notify.all", "notify.host.{hostname}", "notify.user.{username}" ]
  },
  "Logging": {
    "Directory": "%LOCALAPPDATA%\\WinAgentNotification\\logs"
  }
}
```

- [ ] **Step 4: Create the minimal tray context**

Create `src/WinAgentNotification.App/TrayApplicationContext.cs`:

```csharp
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
```

- [ ] **Step 5: Create Program.cs**

Create `src/WinAgentNotification.App/Program.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

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

        var configuration = new ConfigurationBuilder()
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

        try
        {
            Log.Information("WinAgentNotification starting");

            using var host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(builder => builder.AddConfiguration(configuration))
                .ConfigureServices(services => ConfigureServices(services, configuration))
                .Build();

            host.Start();

            using var trayContext = new TrayApplicationContext(Application.Exit);
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
    }
}
```

- [ ] **Step 6: Build to verify it compiles on Linux**

```bash
dotnet build src/WinAgentNotification.App 2>&1 | tail -3
```

Expected: `Build succeeded.` (warnings about Windows-only APIs are acceptable; errors are not).

- [ ] **Step 7: Commit**

```bash
git add WinAgentNotification.sln src/WinAgentNotification.App
git commit -m "feat: add WinForms tray shell with generic host bootstrap"
```

---

### Task 6: ToastNotifier

**Files:**
- Create: `src/WinAgentNotification.App/IToastNotifier.cs`
- Create: `src/WinAgentNotification.App/ToastNotifier.cs`
- Modify: `src/WinAgentNotification.App/Program.cs` (register service in `ConfigureServices`)

**Interfaces:**
- Consumes: `NotificationMessage`, `NotificationLevel` from Core (Task 2).
- Produces (used by Task 7): `interface IToastNotifier { void Show(NotificationMessage message); }` registered as singleton.

Note: no unit test — the class is a thin wrapper over the Windows-only toast
API and cannot run on Linux. It is covered by the manual E2E checklist
(Task 9). Verification here is compile-only.

- [ ] **Step 1: Create the interface**

Create `src/WinAgentNotification.App/IToastNotifier.cs`:

```csharp
using WinAgentNotification.Core;

namespace WinAgentNotification.App;

public interface IToastNotifier
{
    void Show(NotificationMessage message);
}
```

- [ ] **Step 2: Create the implementation**

Create `src/WinAgentNotification.App/ToastNotifier.cs`:

```csharp
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
```

- [ ] **Step 3: Register in DI**

In `src/WinAgentNotification.App/Program.cs`, replace the empty `ConfigureServices` method with:

```csharp
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IToastNotifier, ToastNotifier>();
    }
```

- [ ] **Step 4: Build to verify it compiles**

```bash
dotnet build src/WinAgentNotification.App 2>&1 | tail -3
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/WinAgentNotification.App
git commit -m "feat: add toast notifier mapping notification levels to toast styles"
```

---

### Task 7: NATS subscriber service

**Files:**
- Create: `src/WinAgentNotification.App/NatsSettings.cs`
- Create: `src/WinAgentNotification.App/ConnectionStateMonitor.cs`
- Create: `src/WinAgentNotification.App/NatsSubscriberService.cs`
- Modify: `src/WinAgentNotification.App/Program.cs` (register services)

**Interfaces:**
- Consumes: `MessageParser.Parse`, `SubjectResolver.Resolve`, `INatsCredentialsProvider`, `AnonymousCredentialsProvider` (Tasks 2–4); `IToastNotifier` (Task 6).
- Produces (used by Task 8):
  - `sealed class ConnectionStateMonitor` with `bool IsConnected { get; }`, `event Action<bool>? ConnectionStateChanged`, `void SetConnected(bool connected)` (raises the event only on state change).
  - `sealed class NatsSettings { string Url; string[] Subjects; }` bound from config section `"Nats"`.

- [ ] **Step 1: Create NatsSettings**

Create `src/WinAgentNotification.App/NatsSettings.cs`:

```csharp
namespace WinAgentNotification.App;

public sealed class NatsSettings
{
    public string Url { get; set; } = "nats://localhost:4222";

    public string[] Subjects { get; set; } =
        ["notify.all", "notify.host.{hostname}", "notify.user.{username}"];
}
```

- [ ] **Step 2: Create ConnectionStateMonitor**

Create `src/WinAgentNotification.App/ConnectionStateMonitor.cs`:

```csharp
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
```

- [ ] **Step 3: Create NatsSubscriberService**

Create `src/WinAgentNotification.App/NatsSubscriberService.cs`:

```csharp
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
```

- [ ] **Step 4: Register services in DI**

In `src/WinAgentNotification.App/Program.cs`, add `using WinAgentNotification.Core;` to the using block and replace `ConfigureServices` with:

```csharp
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NatsSettings>(configuration.GetSection("Nats"));
        services.AddSingleton<ConnectionStateMonitor>();
        services.AddSingleton<INatsCredentialsProvider, AnonymousCredentialsProvider>();
        services.AddSingleton<IToastNotifier, ToastNotifier>();
        services.AddHostedService<NatsSubscriberService>();
    }
```

- [ ] **Step 5: Build and run all tests**

```bash
dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3
```

Expected: `Build succeeded.` and `Passed! - Failed: 0, Passed: 20`.

Note: if the NATS.Net event API differs from `ConnectionOpened` /
`ConnectionDisconnected` (`AsyncEventHandler` style shown above), check the
installed package's `NatsConnection` members and adjust the two handler
registrations — the monitor calls stay identical.

- [ ] **Step 6: Commit**

```bash
git add src/WinAgentNotification.App
git commit -m "feat: add NATS subscriber service with reconnect and state monitor"
```

---

### Task 8: Tray connection-state binding

**Files:**
- Modify: `src/WinAgentNotification.App/TrayApplicationContext.cs` (full replacement below)
- Modify: `src/WinAgentNotification.App/Program.cs` (pass monitor and URL into the tray context)

**Interfaces:**
- Consumes: `ConnectionStateMonitor`, `NatsSettings` (Task 7).
- Produces: `TrayApplicationContext(ConnectionStateMonitor monitor, string serverUrl, Action onExitRequested)` — final constructor shape.

- [ ] **Step 1: Replace TrayApplicationContext**

Replace the entire content of `src/WinAgentNotification.App/TrayApplicationContext.cs` with:

```csharp
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
```

- [ ] **Step 2: Update Program.cs to wire the tray context**

In `src/WinAgentNotification.App/Program.cs`, add these usings if not present:

```csharp
using Microsoft.Extensions.Options;
using WinAgentNotification.Core;
```

Then replace the two lines

```csharp
            using var trayContext = new TrayApplicationContext(Application.Exit);
            Application.Run(trayContext);
```

with:

```csharp
            var monitor = host.Services.GetRequiredService<ConnectionStateMonitor>();
            var natsSettings = host.Services.GetRequiredService<IOptions<NatsSettings>>().Value;

            using var trayContext = new TrayApplicationContext(
                monitor, natsSettings.Url, Application.Exit);
            Application.Run(trayContext);
```

- [ ] **Step 3: Build and run all tests**

```bash
dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3
```

Expected: `Build succeeded.` and `Passed! - Failed: 0, Passed: 20`.

- [ ] **Step 4: Commit**

```bash
git add src/WinAgentNotification.App
git commit -m "feat: reflect NATS connection state in tray icon and menu"
```

---

### Task 9: README with manual E2E checklist and final verification

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: everything built in Tasks 1–8.
- Produces: user-facing documentation; the manual E2E checklist that serves as POC acceptance.

- [ ] **Step 1: Write README.md**

Create `README.md` at the repository root:

````markdown
# WinAgentNotification

A resident Windows tray agent that subscribes to NATS subjects and shows a
Windows toast notification when a message arrives. Built for company-internal
workstations: internal systems publish to NATS, every workstation running
the agent receives broadcast or targeted notifications.

Design spec: `docs/superpowers/specs/2026-07-15-nats-desktop-notifier-design.md`

## How it works

```
Publisher → NATS server → WinAgentNotification (tray app) → Windows toast
```

Each workstation subscribes to three subjects:

| Subject | Purpose |
| --- | --- |
| `notify.all` | broadcast to every workstation |
| `notify.host.<computername>` | targeted at one machine |
| `notify.user.<username>` | targeted at one user |

Machine/user names are lowercased; whitespace, `.`, `*`, `>` become `-`.

## Message contract

```json
{
  "title": "Database backup finished",
  "body": "nightly backup OK, took 12 minutes",
  "level": "info"
}
```

- `title` — required.
- `body` — optional, defaults to empty.
- `level` — optional: `info` (default) | `warning` | `critical`.
  Unknown values are treated as `info` and logged.
- Extra fields are ignored (forward compatibility).

Toast styles: `info` standard; `warning` title prefixed with `⚠`;
`critical` long-duration toast.

## Configuration

`appsettings.json` next to the executable:

```json
{
  "Nats": {
    "Url": "nats://nats.internal.example:4222",
    "Subjects": [ "notify.all", "notify.host.{hostname}", "notify.user.{username}" ]
  },
  "Logging": { "Directory": "%LOCALAPPDATA%\\WinAgentNotification\\logs" }
}
```

`{hostname}` / `{username}` are expanded at startup. Logs roll daily,
7 days retained. The POC connects anonymously; a credentials-provider seam
(`INatsCredentialsProvider`) is in place for a future token-exchange flow.

## Build

```bash
dotnet build            # full solution (on Linux needs EnableWindowsTargeting, already set)
dotnet test             # Core unit tests, run anywhere
```

Publish a self-contained exe (on Windows):

```powershell
dotnet publish src/WinAgentNotification.App -c Release -r win-x64 --self-contained
```

## Run / auto-start (POC)

Run `WinAgentNotification.exe` directly, or put a shortcut into
`shell:startup` so it starts after login. A tray icon shows connection
state (info icon = connected, error icon = disconnected); right-click →
Exit to quit. Only one instance runs per user session.

## Manual E2E acceptance checklist (Windows)

1. Start a local NATS server: `nats-server`
2. Point `appsettings.json` at `nats://localhost:4222` and start the app.
3. Tray icon appears and shows Connected.
4. `nats pub notify.all '{"title":"hello","body":"world"}'` → standard toast.
5. `nats pub notify.all '{"title":"disk","level":"warning"}'` → toast with `⚠` title.
6. `nats pub notify.all '{"title":"down","level":"critical"}'` → long-duration toast.
7. `nats pub notify.host.<your-computername-lowercase> '{"title":"targeted"}'` → toast.
8. `nats pub notify.all 'not json'` → no toast; warning in the log file.
9. Stop `nats-server` → tray icon flips to Disconnected.
10. Restart `nats-server` → icon flips back to Connected; publishing works again.
11. Right-click tray icon → Exit → process ends, icon disappears.

## POC scope exclusions

Authentication (seam only), JetStream/offline catch-up, toast click
actions, installer/auto-start tooling, auto-update, localization.
````

- [ ] **Step 2: Final full verification**

```bash
dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3
```

Expected: `Build succeeded.` and `Passed! - Failed: 0, Passed: 20`.

- [ ] **Step 3: Commit and push**

```bash
git add README.md
git commit -m "docs: add README with usage, config, and manual E2E checklist"
git push -u origin claude/init-superpowers-skill-6b6tqm
```

Expected: push succeeds to `claude/init-superpowers-skill-6b6tqm`.
