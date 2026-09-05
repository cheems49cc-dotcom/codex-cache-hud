# Contributing

Keep the overlay small and the metrics explicit. Please describe a reproducible problem before adding controls or dependencies. Use synthetic records for tests; do not commit actual Codex sessions, credentials, user paths, settings, or screenshots containing conversations.

Run both commands from README on Windows. Any parser/lifecycle fix should include a synthetic regression; any rendering fix should exercise repeated redraw and idle/waiting states. Changes must preserve shared read-only access, main/subagent coverage, latest-call percentages, and click-through below the control strip.

Maintain README.md, README.zh-CN.md, PRIVACY.md and CHANGELOG.md when their facts change. Keep upstream MIT attribution. Publish only through the reviewed build script; never archive an entire development directory. Use a GitHub noreply commit email if you want to keep your personal email private.
