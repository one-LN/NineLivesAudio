# Nine Lives Audio — Agent Instructions

Native Windows desktop client for Audiobookshelf with a cosmic dark theme, offline downloads, and sync. WinUI 3, .NET 9.

Read `CLAUDE.md` in this repo for project context.

Before shipping a change, run `python tools/verify.py` (offline static self-check: localization key parity,
XAML wiring, DI graph, packaging consistency). Build/publish: `tools/package.ps1`. See
`docs/中文支持与优化说明.md` for the localization architecture.

Studio-wide agent and routing instructions, voice, and conduct live in the global `~/.codex/AGENTS.md` (Claude twin: `~/.claude/CLAUDE.md`). Brand, style, and lore canon lives in `~/.claude/BRAND.md`.
