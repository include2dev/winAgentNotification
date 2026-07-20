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
7 days retained.

### Authentication

Without an `Auth` section the agent connects anonymously. To use a
dedicated credential (interim setup for the dev NATS environment until the
user-token-exchange flow lands), add `Nats.Auth` with exactly one
mechanism — a NATS `.creds` file path, a token, or username/password:

```json
{
  "Nats": {
    "Url": "nats://nats.dev.example:4222",
    "Auth": { "CredsFile": "%LOCALAPPDATA%\\WinAgentNotification\\agent.creds" }
  }
}
```

Values can also come from environment variables instead of the file, e.g.
`Nats__Auth__Token` (double underscore separators), which keeps secrets out
of `appsettings.json`. Environment variables in the `CredsFile` path are
expanded at startup.

Security notes for the interim credential:
- Do not commit the `.creds` file or tokens to source control.
- Restrict the file's ACL to the account running the agent.
- The credential is read on every (re)connect via
  `INatsCredentialsProvider`, so replacing this config-based provider with
  the future token-exchange implementation requires no connection-code
  changes.

## Build

```bash
dotnet build            # full solution (on Linux needs EnableWindowsTargeting, already set)
dotnet test             # Core unit tests, run anywhere
```

On Linux, building the full solution also requires a Microsoft-built .NET SDK
(e.g. installed via the `dotnet-install` script or the `packages.microsoft.com`
apt feed). Distro source-built SDKs — such as Ubuntu's `dotnet-sdk-8.0`
package — lack `Sdks/Microsoft.NET.Sdk.WindowsDesktop` and fail with MSB4019
on the App project's WindowsDesktop-targeted build.

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
