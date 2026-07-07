# Luma

Luma is a small always-on-top C# dock for general questions and local programming workflows with Claude Code or Codex. Text-only questions work immediately; screenshots and project folders are optional context.

It can route email, terminal, web-reply, and longer requests into dedicated task workspaces, each confirmed before a window opens. Coding requests work differently: they stay right in the chat panel. Email tasks prepare an unsent reply in classic Outlook; coding tasks produce a reviewable Git diff, shown inline in the chat, and apply it only after explicit approval; terminal tasks show a proposed command and only run it after explicit approval; browser-reply tasks draft text you copy into the page yourself. Providers remain read-only and Luma never sends email, runs tests, commits, pushes, or executes commands automatically.

Coding tasks remember the repository you last used this session - instead of a folder picker every time, a small popup asks whether to reuse it or pick a different one - then the diff review card appears directly in the assistant's chat bubble.

The diff card is a per-file, per-hunk checklist with colored additions/deletions, so you can apply only the changes you want; a raw-patch view is still available for hand-editing. If a proposed patch or terminal command fails, Luma automatically asks the provider for a corrected one (up to two attempts) - it only ever regenerates the proposal, never applies or runs anything on its own. Coding tasks also see a `.gitignore`-aware file listing up front, alongside their own read-only exploration of the repository. After a patch is applied, two more opt-in, explicit-click actions appear on the card: "Run tests" (with an auto-detected or freely editable build/test command) and, if that fails, "Revert".

## Requirements

- .NET 10 SDK (building from source)
- Claude Code (`claude`) and/or Codex CLI (`codex`), already authenticated
- Linux capture helper: `grim` on Wayland or ImageMagick `import` on X11

The CLI runs locally, but the screenshot and question are processed by the selected provider's cloud service.

## Run

```powershell
dotnet run --project src/Luma.App
```

Click the floating ✦ dock, choose Ask, Code, or Command, enter a request, and press Send. Choose a project once for programming tasks; Luma remembers it. Add a screenshot only when visual context is useful. Captures and CLI sessions are temporary and deleted when cleared or the application exits.

The provider CLI runs on your computer, but Claude and Codex process requests using their cloud services. Luma does not currently run an offline model.

## Build and test

```powershell
dotnet test Luma.slnx
dotnet publish src/Luma.App -c Release -r win-x64 --self-contained
```

Use `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64` as the runtime identifier for other platforms. macOS requires Screen Recording permission for `/usr/sbin/screencapture`.
