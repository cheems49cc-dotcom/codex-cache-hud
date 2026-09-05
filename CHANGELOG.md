# Changelog

## 3.4.1

- Restore the compact left display (`123K tokens`), keeping the context-growth explanation in the tooltip. No counting or cache-hit algorithm changes.

## 3.4.0

- Label launch-scoped context growth explicitly and explain that it is not billed consumption.
- Keep latest-call percentages and curve colors consistent on repeated transparent redraws.
- Match completion/abort events to the active turn, including startup tail hydration.
- Show hover status and last-call age; distinguish idle, waiting and paused states.
- Retain the latest 30 main/subagent calls without time-based synthetic samples.
- Add synthetic WPF rendering checks alongside the 19 Core checks.
- Publish reviewed source with fresh history, retained upstream MIT attribution, and a portable Windows build with source-path mapping and no debug symbols.

Based on Codex Monitor HUD v3.1.0, commit `ace7a89bdb2692eec4f974ef8323865c22a4e68c`. See the upstream repository for the original project's history.
