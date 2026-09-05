# Codex Cache HUD

A small, local-only Windows overlay for Codex cache-hit activity. Modified from [LH-03/codex-monitor-hud](https://github.com/LH-03/codex-monitor-hud), upstream commit `ace7a89bdb2692eec4f974ef8323865c22a4e68c` (v3.1.0), under the MIT license. This community tool is not affiliated with or endorsed by OpenAI.

[中文说明](README.zh-CN.md) · [Privacy](PRIVACY.md) · [License](LICENSE)

## Download and run

Download the Windows x64 ZIP from [Releases](../../releases), extract the whole folder, and open `CodexCacheHUD.exe`. Keep the files together. Windows 10/11 x64 is supported; the self-contained build does not need a separate .NET runtime. A local Codex session directory is required. The binary is currently unsigned.

No API key, account sign-in, installer, administrator access, startup task, or Codex configuration change is required. Exit from the system tray. Do not disable antivirus protections if a download is flagged; report the detection name and release hash instead.

## What the numbers mean

- **Left — context growth since launch:** each observed session starts with a baseline; positive changes in its latest `last_token_usage.total_tokens` are added. Newly created sessions start from zero. Compaction does not subtract prior growth. Closing and reopening the HUD resets this in-memory counter. This measures observed context growth, **not billed tokens, API cost, or subscription usage**.
- **Right — latest call cache hit:** `cached_input_tokens / input_tokens` for the last chart point. No lifetime averaging.
- **Curve:** the most recent 30 real calls across discovered main and subagent sessions. Points are equally spaced by call order, not elapsed time. No synthetic calls are added to make the chart move.
- **Idle, paused, or waiting for a new turn's first result:** the percentage reads `--`; history remains dimmed. Hover over the percentage to see status and the latest call time.
- **Colors:** at least 98% green; 95–98% yellow; 90–95% red; below 90% dark red. The number and curve use the same latest-call color.

The overlay follows records written locally by Codex; it cannot show an inference's final token counts before Codex emits them. Logs are not a billing ledger. Discovery is bounded (30-minute recent window by default); missing, locked, unsupported, or delayed local records can limit coverage. Quota percentages in the diagnostic tooltip are locally observed snapshots, not a live account query.

## Controls

- Size: 20%, 40%, 60%, 78%, or 100%.
- Background: fully transparent, or 20–100% visibility without fading the text.
- In fully transparent mode only the top text-height strip accepts the mouse and reveals the controls; the chart area lets clicks through to the app beneath it.
- Hover over the left value for the formula and token breakdown. The visible label is `上下文增长` (context growth); the compact overlay currently uses Chinese labels.
- Right-click or use the tray for pause, settings, and exit. Settings belong to the HUD only, in `%LOCALAPPDATA%\CodexMonitorHUD`.

## Build and verify

Install an official .NET 10 SDK compatible with `global.json`, then run from the repository root on Windows:

```powershell
dotnet run --project tests-dotnet/CodexMonitorHud.Core.Tests -c Release
dotnet run --project tests-dotnet/CodexMonitorHud.App.Tests -c Release
pwsh -File scripts/Publish-Windows.ps1
```

The Core checks use synthetic temporary sessions. The WPF checks exercise the real view without opening a window or reading your Codex sessions. The publishing script emits a portable ZIP and SHA-256 checksum under `artifacts/`. It excludes debug symbols, user settings, session files, and local SDKs. Builds map source paths to `/_/`.

## Attribution and scope

The upstream project supplies the WPF/Core foundation. This derivative focuses on the single cache curve, transparent compact controls, launch-scoped growth, main/subagent call history, and lifecycle-aware status. Original copyright and permission notices remain in [LICENSE](LICENSE); palette references remain in [COLOR_ATTRIBUTION.md](COLOR_ATTRIBUTION.md). Legacy plugin/MCP installation scripts, screenshots, and private development history are not distributed in this repository.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development and [SECURITY.md](SECURITY.md) for safe issue reporting.
