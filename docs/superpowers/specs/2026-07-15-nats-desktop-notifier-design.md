# WinAgentNotification — NATS-to-Windows-Toast Notifier (Design)

Date: 2026-07-15
Status: Approved (POC scope)

## Purpose

A resident Windows agent for company workstations. It subscribes to NATS
subjects and shows a Windows toast notification when a matching message
arrives. Internal systems publish notifications to NATS; every employee
workstation running the agent receives broadcast or targeted messages.

## Confirmed requirements

1. Deployment target: multiple company-internal Windows workstations.
2. Runtime model: starts after user login and stays resident in the user
   session. A true Windows Service is NOT used — Session 0 isolation would
   prevent showing toasts, and pre-login operation is not required.
3. Language/stack: C# on .NET 8 (LTS).
4. Subscription model: broadcast + targeted subjects per machine.
5. Message format: JSON with basic fields (`title`, `body`, `level`).
6. Delivery: core NATS pub/sub only. No offline catch-up (no JetStream) in
   the POC; messages missed while offline are dropped by design.
7. Authentication: none for the POC. A credentials-provider seam is kept so
   a future "exchange user token for NATS token" flow can plug in without
   restructuring the connection code.

## Architecture overview

```
Publisher (any internal system)
    │  publish JSON
    ▼
NATS Server (company-internal, no auth for POC)
    │  subjects: notify.all / notify.host.<machine> / notify.user.<account>
    ▼
WinAgentNotification.exe   ← resident in user session (system tray)
    ├─ NatsSubscriberService   subscribe + auto-reconnect
    ├─ MessageParser           JSON validation; bad messages logged & dropped
    ├─ ToastNotifier           maps level → Windows toast
    └─ TrayShell               tray icon: connection status + exit
```

Single executable running in the user session. No server-side component is
built in this project; the NATS server is existing infrastructure.

Chosen approach: WinForms `ApplicationContext` shell (tray icon only, no
window) hosting a .NET Generic Host; the NATS subscription logic runs as a
`BackgroundService`. A headless console worker and a Windows Service +
user-agent split were considered and rejected (the former is not the final
product shape; the latter adds IPC complexity with no benefit for
post-login-only requirements).

## Project structure

```
WinAgentNotification.sln
├─ src/WinAgentNotification.Core/     # net8.0 (cross-platform) — unit-testable logic
│   ├─ NotificationMessage.cs         # record: Title, Body, Level (info|warning|critical)
│   ├─ MessageParser.cs               # bytes → NotificationMessage? (null + reason on failure)
│   ├─ SubjectResolver.cs             # expands "notify.host.{hostname}" placeholders
│   └─ INatsCredentialsProvider.cs    # auth seam; POC implementation = anonymous
├─ src/WinAgentNotification.App/      # net8.0-windows — WinForms tray shell
│   ├─ Program.cs                     # single-instance mutex + Generic Host bootstrap
│   ├─ TrayApplicationContext.cs      # NotifyIcon, context menu (status / exit)
│   ├─ NatsSubscriberService.cs       # BackgroundService: connect, subscribe, receive
│   ├─ ToastNotifier.cs               # level → toast style, sends the toast
│   └─ appsettings.json               # configuration shipped next to the exe
└─ tests/WinAgentNotification.Core.Tests/   # xUnit, runs on any platform
```

Key packages: `NATS.Net` (official client) and
`Microsoft.Toolkit.Uwp.Notifications` (toast support for unpackaged desktop
apps). `WinAgentNotification.Core` deliberately has no Windows dependency so
parsing and subject-expansion logic can be built and tested on Linux CI.

## Message contract and subject conventions

Published JSON payload:

```json
{
  "title": "Database backup finished",
  "body": "nightly backup OK, took 12 minutes",
  "level": "info"
}
```

- `title` — required.
- `body` — optional, defaults to empty.
- `level` — optional: `info` | `warning` | `critical`. Defaults to `info`;
  unknown values are treated as `info` and logged.
- Extra fields are ignored (forward compatibility: future `url`, `actions`,
  etc. will not break older agents).

Subjects subscribed at startup (three per machine):

- `notify.all` — broadcast to every workstation.
- `notify.host.<COMPUTERNAME>` — targeted at one machine.
- `notify.user.<username>` — targeted at one user.

Machine and user names are lowercased and characters that are invalid in
NATS subject tokens (whitespace, `.`, `*`, `>`) are replaced with `-`.

Toast presentation: `info` → standard toast; `warning` → title prefixed
with `⚠ `; `critical` → long-duration/urgent style with sound
(exact styling tuned to Windows version capabilities at implementation
time).

## Configuration (appsettings.json)

```json
{
  "Nats": {
    "Url": "nats://nats.internal.example:4222",
    "Subjects": [ "notify.all", "notify.host.{hostname}", "notify.user.{username}" ]
  },
  "Logging": { "Directory": "%LOCALAPPDATA%\\WinAgentNotification\\logs" }
}
```

- `{hostname}` / `{username}` placeholders are expanded at startup; IT
  deployment only needs to change `Url`.
- No auth block exists in the POC. A future `Nats.Auth` section plus a new
  `INatsCredentialsProvider` implementation (e.g. exchanging a user token
  for a NATS token) plugs in without touching connection code. The provider
  is consulted on every (re)connect, which also accommodates future token
  expiry/renewal.

## Error handling and resilience

- **Reconnection**: delegated to NATS.Net built-in auto-reconnect
  (exponential backoff, retry forever). Subscriptions resume automatically
  after reconnect.
- **Visible connection state**: tray icon has two states
  (connected / disconnected); tooltip shows current state and server URL.
- **Malformed messages**: parse failure → log warning (with first 500
  characters of the raw payload) → drop. Never crash on bad input.
- **Toast failure**: log error and continue. (Focus-assist / do-not-disturb
  suppression is handled by Windows itself and is not an error.)
- **Duplicate launch**: a named mutex prevents a second instance.
- **Logging**: rolling file, 7-day retention, default level Information.

## Testing strategy

- **Unit tests** (xUnit; run on CI and locally on any OS):
  - `MessageParser`: valid payloads, invalid JSON, missing required fields,
    unknown `level` values, oversized/extra fields.
  - `SubjectResolver`: placeholder expansion and character sanitization.
- **Manual E2E** (POC acceptance): run a local `nats-server`, publish with
  `nats pub notify.all '{"title":"hi","level":"critical"}'`, verify the
  toast appears, the tray icon reflects disconnect/reconnect, and messages
  resume after reconnection.
- Environment note: this repository's CI (and the development session) runs
  on Linux, which can build the solution (`EnableWindowsTargeting`) and run
  the Core tests. Actual toast display must be verified on a Windows
  machine.

## Out of scope for the POC

- Authentication (seam is in place; see Configuration).
- Offline catch-up / JetStream durable consumers.
- Toast click actions, buttons, deep links.
- Installer and auto-start (manual shortcut in `shell:startup` for the POC;
  MSI/Intune later).
- Auto-update.
- Localization.
