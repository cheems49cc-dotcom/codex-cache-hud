# Privacy

The normal HUD process reads local Codex session JSONL, session identity/index metadata, and selected metadata from the local state SQLite database. Files are opened for shared read access. The app does not request an API key, read `auth.json`, proxy requests, call a model, or upload telemetry. It does not modify Codex configuration or its updater.

Only the latest bounded call samples and launch-scoped counters are held in memory. The HUD reads log lines to extract numeric/lifecycle fields; it does not persist prompt, reply, tool body, or raw session content. Session identifiers, local paths and workspace/title metadata may be present in in-memory state and the local task registry inherited from upstream. They are not uploaded. Do not share the registry or settings without reviewing them.

The HUD stores its own settings and overwrites a small heartbeat/task registry in `%LOCALAPPDATA%\CodexMonitorHUD`. Diagnostic logging is off by default. If `--debug-log` is explicitly enabled, output can include local paths; keep it private. Exiting ends monitoring and discards call history and launch counters.

Optional user actions such as opening a task can hand a URI to another local application. Downloading the program and using GitHub are separate network operations, not background telemetry from the HUD. The portable release contains no installer, background service, automatic update downloader, credentials, or copied user configuration.

The release source and package are reviewed for private paths, identifiers and credential patterns. Such checks reduce disclosure risk but cannot prove the absence of every possible secret. Please do not upload real Codex logs, account details, keys, or private screenshots in issues.
