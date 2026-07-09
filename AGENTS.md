# Luma — agent context (compact)

Always-on-top **Avalonia 12** / **.NET 10** dock (`src/Luma.App`, assembly `Luma`). Talks to **Claude Code**, **Codex**, **Grok Build** CLIs for chat, screen explain, coding, shell/email/browser tasks.

## Layout

| Path | Role |
|------|------|
| `src/Luma.App/` | App — `MainWindow.axaml(.cs)`, `ViewModels/MainWindowViewModel.cs`, `Models/`, `Services/`, `Controls/` |
| `tests/Luma.Tests/` | xUnit |
| `Luma.slnx` | Solution |
| Settings | `%LocalAppData%/Luma/settings.json` |
| Outcome memory | `%LocalAppData%/Luma/outcome-memory.json` |
| MCP installs | `%LocalAppData%/Luma/mcp-installs.json` → syncs `~/.grok/config.toml` managed block |

**TFM:** `net10.0` (not windows-only). **Theme:** `LumaTheme` + `App.axaml` (violet glass). Secondary windows are code-built with `LumaTheme`.

## Core chat path

- **VM:** `MainWindowViewModel` — `RunTurnAsync`, `RunCodeTurnAsync`, compose `+` menu, chaos, split-brain.
- **Stream:** `ChatStreamTextPolicy` + `StreamPartialCoalescer` + `ChatStreamUiBridge` — progressive text; **ASK_USER** / **IsQuestion** only on finalize.
- **Captures:** text-first chat by default (`attachCaptures: false`); explain screen/part/suggestions attach. **NEED_SCREEN** → capture + retry. Tiny in-chat thumbs via `ChatMessage.AttachImage` (no top preview box).
- **Directives:** `ASK_USER:`, `NEED_SCREEN:`, `SHOW_WHERE: label \| x,y,w,h` (0–1 screen fractions) → `ShowWhereParser` + `GhostCursorWindow`.
- **History:** limits in `AppSettings`; built in `CliAiClient.BuildPrompt`.

## Providers (`AiClients.cs`)

| Provider | Tools / sandbox notes |
|----------|------------------------|
| Claude | `Read,Glob,Grep,Write,Edit` + `dontAsk` (not on Suggest/FollowUp/Route) |
| Grok | `read_file,grep,list_dir,search_replace` + `--always-approve` — **no** bare `write` tool id (breaks agent build) |
| Codex | `workspace-write` + `-c approval_policy="never"` — **no** `--ask-for-approval` on `codex exec` |

Working directory is passed on chat turns when set. Agents may read/write under project root; prompts forbid destructive shell.

## Major features (by area)

**Screen:** open-dock capture, difference → new-chat confirm + **screen digest** (`ScreenDigestParser`), explain full / region, global Ctrl+Shift+E, kill-drag STOP target.

**Context:** project folder (`+` menu), clipboard pin, attach files, `@path` / `@"path with spaces"` (`ContextAttachments`).

**Trust:** `WorkspaceWriteAuditor` post-turn file list + **Undo**; coding still has inline `CodeChatSession` / `DiffCardControl`.

**Live pair:** while an agent writes (project folder set), `LivePairMap` polls workspace diffs into a dock mini-map with +/− heat bars; click a file → focus matching audit row (`JumpLivePairCommand`).

**Outcome memory:** undo/write notes → suggestion chips (“Avoid:…”, “Retry:…”).

**Split-brain:** toggle → next send dual providers (explainer + implementer) + Keep A/B/Both.

**Chaos Mode:** settings/`+` — tone ELI5/staff, roast UI, dual debate, pomodoro focus lock on Explain.

**MCP:** `McpMarketplaceWindow` — curated + official registry browse, install/enable/remove, sync Grok `config.toml`.

## UI map

- Header: New chat, provider, settings, close.
- Quick actions: Explain this screen / Explain this part (no standalone snip tool).
- Chat bubbles: markdown, questions card, file audit, split-brain card, SHOW_WHERE button, digest action chips.
- Live pair strip (above next-steps): file mini-map + heat while/after agent writes.
- Compose: context chips + `+` (folder, clipboard, files, chaos, split-brain, MCP).

## Conventions

- Prefer small pure helpers in `Services/` (testable without Avalonia window).
- `dotnet test tests/Luma.Tests -c Release` (Debug may lock if `Luma.exe` is running).
- Don’t invent OCR (removed). Don’t re-add top capture preview.
- Grok usage: interactive `grok` then `/usage show` — not shown in Luma.

## Build / run

```powershell
dotnet test Luma.slnx -c Release
dotnet run --project src/Luma.App
```

Requires authenticated `claude` / `codex` / `grok` on PATH as needed.
