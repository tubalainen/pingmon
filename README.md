# PingMon

A lightweight Windows system tray application that continuously monitors network hosts via ICMP ping or HTTP/HTTPS requests and alerts you when they go down or become slow.

## Features

- Monitor up to 10 hosts simultaneously via **ICMP ping** or **HTTP/HTTPS**
- System tray icon changes color to reflect overall network health
- Balloon tip notifications when hosts go down or recover
- Live stats graph with per-host latency history (up to 2000 data points)
- Configurable ping interval, timeout, failure threshold, and latency alert
- Custom display names per host
- Dark and light theme support (follows Windows system setting)
- Auto-start with Windows option
- First-run setup wizard with automatic traceroute-based host discovery
- Zero external dependencies

## Requirements

- Windows 10 or 11
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) (included in Windows 10/11 by default)

## Installation

PingMon is a single executable — no installer required.

1. Download `PingMon.exe` from the [Releases](../../releases) page.
2. Place the executable anywhere you like (e.g. `C:\Tools\PingMon\PingMon.exe`).
3. Run it. The first-run wizard will guide you through initial setup.

To build from source:

```bash
dotnet build PingMon.csproj -c Release
```

The output is written to `bin\Release\net48\PingMon.exe`.

## How to Use

### First Run

On first launch, the setup wizard opens automatically. It can:

- **Discover hosts via traceroute** — runs a traceroute to a destination you specify and lists every hop as a candidate host to monitor.
- **Manual entry** — type any hostname, IP address, or URL (e.g. `https://example.com/health`) directly.

Select the hosts you want to monitor and click **Finish**. PingMon starts immediately and appears in the system tray.

### System Tray Icon

| Icon color | Meaning |
|---|---|
| Green | All enabled hosts are reachable |
| Yellow | At least one host has elevated latency |
| Red | At least one host is down |
| Gray | No hosts configured or monitoring paused |

- **Left-click** the tray icon to open the Stats window.
- **Right-click** the tray icon to open the context menu.

### Context Menu

The right-click menu shows a live status line for each configured host, followed by:

- **Stats** — open/bring-to-front the stats window
- **Configure** — open the configuration dialog
- **Exit** — quit PingMon

### Stats Window

Displays a real-time latency graph for all enabled hosts. Each host is represented by a colored line. Timeouts appear as an X marker near the top of the graph.

- Use the checkboxes at the top to show/hide individual hosts.
- Check **Always on top** to keep the window above other applications.
- The window remembers its last position between sessions.

### Configuration

Open **Configure** from the tray menu to adjust settings.

#### Per-host settings

| Field | Description |
|---|---|
| En | Enable/disable monitoring for this host |
| Host/IP/URL | Hostname, IP address, or full URL (e.g. `https://example.com/health`) |
| Name | Optional display name (shown in tray and stats) |
| Type | **Ping** — ICMP ping; **HTTP** — HTTP/HTTPS GET request |
| Fail # | Consecutive failures before the host is considered down (default: 3) |
| Latency (ms) | Alert threshold in milliseconds; 0 = disabled (default: 0) |

**HTTP checks** issue a GET request to the URL and treat any successful response (2xx after redirects) as up; 4xx/5xx responses and SSL errors count as down. The measured value is the total response time in milliseconds. URLs beginning with `http://` or `https://` are automatically detected as HTTP checks.

To reduce load on monitored servers, HTTP checks use a separate, longer interval configured via **HTTP interval (sec)** (default: 30 seconds). Each request rotates through a pool of realistic browser User-Agent strings and headers, and opens a fresh TCP connection every time to avoid leaving a detectable session pattern.

#### Global settings

| Field | Description |
|---|---|
| Ping interval | Seconds between ICMP ping cycles (default: 10, minimum: 1) |
| Timeout | Milliseconds to wait for each check reply (default: 2000) |
| HTTP interval | Seconds between HTTP checks (default: 30, minimum: 10) — independent of ping interval |

#### Other options

- **Start with Windows** — adds PingMon to the Windows startup registry key.
- **Erase All Settings** — removes all configuration and restarts the setup wizard.

### Notifications

PingMon shows a Windows balloon tip notification when:

- A host goes down (after reaching its failure threshold).
- A previously down host recovers.

### Configuration File

Settings are stored at:

```
%APPDATA%\PingMon\config.json
```

You can back up or copy this file to transfer your configuration to another machine.
