# Luma

Luma is a small always-on-top C# dock for general questions and local programming workflows with Claude Code, Codex, or Grok Code. Text-only questions work immediately; screenshots and project folders are optional context.

Opening the dock captures the screen as ambient context and asks the selected provider for a few short suggested prompts based on what is visible - shown as one-click chips in the empty panel. Suggestions are pre-warmed once at launch so the first open shows chips instantly, then regenerate from a fresh capture every time the panel opens (existing chips stay visible while the new batch loads). They run on a fast model (Claude Haiku by default) with a downscaled copy of the capture; an optional reuse window in settings trades freshness for fewer calls. The capture is visible as a preview, removable with one click, and deleted when cleared or when the application exits. If you prefer not to send the ambient capture, remove it from the preview before asking; region snips always take priority as the question's focus.

When a fresh capture is substantially different from the previous screen and a conversation already exists, Luma asks whether to start a new chat. Running provider and shell processes are shown in the panel; while one is active, drag the collapsed dock onto the red STOP target to cancel every process started by Luma.

After each completed response, Luma uses the configured low-cost suggestion model to offer short one-click replies beneath the conversation. This follow-up pass runs separately and never delays the main answer.

Everything above is configurable from the settings window (the sliders icon in the panel header): toggle screen capture, suggestions, and launch pre-warming entirely; choose how many suggestions to offer, how long to reuse them, and how much screenshot detail to send; and override the model used for chat and suggestions per provider using model names accepted by the corresponding CLI. Codex requests use `gpt-5.4-mini` by default, including screenshot-context requests, instead of the CLI default. Settings persist in `%LocalAppData%/Luma/settings.json`.

At startup Luma checks each installed provider. It keeps your preferred provider when available, otherwise selects the first authenticated provider automatically and generates screen suggestions only after that check completes.

It can route email, terminal, web-reply, and longer requests into dedicated task workspaces, each confirmed before a window opens. Coding requests work differently: they stay right in the chat panel. Email tasks prepare an unsent reply in classic Outlook; coding tasks produce a reviewable Git diff, shown inline in the chat, and apply it only after explicit approval; terminal tasks show a proposed command and only run it after explicit approval; browser-reply tasks draft text you copy into the page yourself. Providers remain read-only and Luma never sends email, runs tests, commits, pushes, or executes commands automatically.

Coding tasks remember the repository you last used this session - instead of a folder picker every time, a small popup asks whether to reuse it or pick a different one - then the diff review card appears directly in the assistant's chat bubble.

The diff card is a per-file, per-hunk checklist with colored additions/deletions, so you can apply only the changes you want; a raw-patch view is still available for hand-editing. If a proposed patch or terminal command fails, Luma automatically asks the provider for a corrected one (up to two attempts) - it only ever regenerates the proposal, never applies or runs anything on its own. Coding tasks also see a `.gitignore`-aware file listing up front, alongside their own read-only exploration of the repository. After a patch is applied, two more opt-in, explicit-click actions appear on the card: "Run tests" (with an auto-detected or freely editable build/test command) and, if that fails, "Revert".

## Requirements

- .NET 9 SDK or newer (building from source; supported by Visual Studio 2022 17.12+)
- Claude Code (`claude`), Codex CLI (`codex`), and/or Grok Code (`grok`), already authenticated
- Linux capture helper: `grim` on Wayland or ImageMagick `import` on X11

The CLI runs locally, but the screenshot and question are processed by the selected provider's cloud service.

## Run

```powershell
dotnet run --project src/Luma.App
```

Click the floating ✦ dock: Luma grabs the screen for context, suggests a few prompts you can send with one click, and automatically routes each request to chat, coding, or command handling. Choose a project once for programming tasks; Luma remembers it. Snip a specific region when a close-up is more useful than the full screen. Captures and CLI sessions are temporary and deleted when cleared or the application exits.

Use **Explain** to drag around any unfamiliar error, chart, control, message, or other screen region and receive an immediate contextual explanation without typing a prompt.
Use **Explain this screen** from the empty panel for the same zero-input workflow across the full display.
The persistent toolbar keeps full-screen explanation, context snipping, and select-and-explain available throughout a conversation; repository selection appears only when automatic routing detects a coding task.
On Windows, **Ctrl+Shift+E** starts the select-and-explain flow globally from any application. The shortcut can be disabled in settings and is shown in Luma only when Windows registers it successfully.

The provider CLI runs on your computer, but Claude and Codex process requests using their cloud services. Luma does not currently run an offline model.

## Build and test

```powershell
dotnet test Luma.slnx
dotnet publish src/Luma.App -c Release -r win-x64 --self-contained
```

Use `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64` as the runtime identifier for other platforms. macOS requires Screen Recording permission for `/usr/sbin/screencapture`.
