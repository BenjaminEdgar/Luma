using System.ComponentModel;
using System.Runtime.CompilerServices;
using Luma.App.Models;

namespace Luma.App.Services;

/// <summary>Window-free orchestration for a coding task carried inline in the chat: prepares repo
/// context, runs AI turns, parses/validates diffs, bounded auto-retry on validation failure, and
/// explicit apply/verify/revert. Mutates the owning ChatMessage's Text/Question/IsQuestion directly
/// for the prose/question part (so the existing bubble template and clarifying-question popup keep
/// working unchanged); exposes its own bindable state for a DiffCardControl to render.</summary>
public sealed class CodeChatSession : INotifyPropertyChanged
{
    public const int MaxAutoRetries = 2;

    private readonly ChatMessage _message;
    private readonly IAiClientFactory _clients;
    private readonly AiProvider _provider;
    private readonly IGitService _git;
    private readonly IShellService _shell;
    private readonly string _workingDirectory;
    private readonly string? _imagePath;
    private readonly string? _contextImagePath;
    private readonly List<ChatMessage> _history = [];
    private IReadOnlySet<string> _protectedPaths = new HashSet<string>();
    private string? _persistentContext;
    private string? _appliedDiff;
    private int _retryCount;

    private DiffDocument? _document;
    private string _rawPatch = string.Empty;
    private bool _showStructured = true;
    private string _statusMessage = string.Empty;
    private bool _canApply;
    private bool _canVerify;
    private bool _canRevert;
    private string _verifyCommand = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DiffDocument? Document { get => _document; private set => Set(ref _document, value); }
    public string RawPatch { get => _rawPatch; private set => Set(ref _rawPatch, value); }
    public bool ShowStructured { get => _showStructured; private set => Set(ref _showStructured, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public bool CanApply { get => _canApply; private set => Set(ref _canApply, value); }
    public bool CanVerify { get => _canVerify; private set => Set(ref _canVerify, value); }
    public bool CanRevert { get => _canRevert; private set => Set(ref _canRevert, value); }
    public string VerifyCommand { get => _verifyCommand; set => Set(ref _verifyCommand, value); }

    public CodeChatSession(ChatMessage message, IAiClientFactory clients, AiProvider provider,
        IGitService git, IShellService shell, string workingDirectory, string? imagePath, string? contextImagePath)
    {
        _message = message;
        _clients = clients;
        _provider = provider;
        _git = git;
        _shell = shell;
        _workingDirectory = workingDirectory;
        _imagePath = imagePath;
        _contextImagePath = contextImagePath;
    }

    public async Task RunAsync(string prompt, CancellationToken token)
    {
        if (!await _git.IsRepositoryAsync(_workingDirectory, token))
            throw new InvalidOperationException("Choose a valid Git repository.");
        _protectedPaths = (await _git.GetStatusAsync(_workingDirectory, token)).ChangedPaths;
        var files = await _git.ListFilesAsync(_workingDirectory, token);
        var listing = RepoContextFormatter.BuildFileListSummary(files);
        _persistentContext =
            $"Repository root: {_workingDirectory}\n" +
            $"Existing changed paths are protected and must not be included: {string.Join(", ", _protectedPaths)}\n" +
            "Repository file listing (respects .gitignore; use this to decide which files to inspect further; " +
            $"this does not replace your own read-tool exploration):\n{listing}";
        await RunTurnAsync(prompt, _persistentContext, token);
    }

    /// <summary>Resumes after a clarifying question (asked via ASK_USER) has been answered.</summary>
    public async Task ContinueAsync(string answer, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        _retryCount = 0;
        _message.IsQuestion = false;
        await RunTurnAsync(answer, null, token);
    }

    private async Task RunTurnAsync(string prompt, string? context, CancellationToken token)
    {
        var priorHistory = _history.ToArray();
        var aiRequest = new AiRequest(prompt, _imagePath, _contextImagePath, priorHistory)
        {
            TaskKind = TaskKind.Code,
            TaskContext = context ?? _persistentContext,
            WorkingDirectory = _workingDirectory,
        };

        var raw = await _clients.Create(_provider).AskAsync(aiRequest,
            partial =>
            {
                _message.IsPending = false;
                _message.IsStreaming = true;
                _message.Text = TextSanitizer.Clean(partial);
            },
            token);

        var parsed = TaskResponseParser.Parse(raw, TaskKind.Code);
        _history.Add(new ChatMessage("user", TextSanitizer.Clean(prompt)));
        _history.Add(new ChatMessage("assistant", parsed.Question is null ? parsed.Text : $"{parsed.Text}\nQuestion: {parsed.Question}"));
        _message.Text = TextSanitizer.Clean(parsed.Text);

        if (parsed.Question is not null)
        {
            _message.Question = TextSanitizer.Clean(parsed.Question);
            _message.IsQuestion = true;
            StatusMessage = "Waiting for your answer";
            return;
        }
        if (!string.IsNullOrWhiteSpace(parsed.Artifact)) await IngestArtifactAsync(parsed.Artifact, token);
    }

    private async Task IngestArtifactAsync(string artifact, CancellationToken token)
    {
        var validation = await _git.ValidateDiffAsync(_workingDirectory, artifact, _protectedPaths, token);
        StatusMessage = validation.Message;
        ShowStructuredIfPossible(artifact);

        if (validation.IsValid)
        {
            _retryCount = 0;
            CanApply = true;
            return;
        }
        CanApply = false;
        var retried = await RetryWithFeedbackAsync(
            $"The proposed patch failed validation: {validation.Message}\n" +
            "Produce a corrected, complete unified diff that fixes this specific problem. " +
            $"Repository root: {_workingDirectory}. Paths must remain repository-relative.", token);
        if (!retried) StatusMessage = "Patch blocked - edit manually or ask again";
    }

    /// <summary>Parses the artifact into a structured diff for the checkbox-driven review UI;
    /// falls back to the raw editable text if parsing yields nothing usable, so a parser gap never
    /// leaves the user with less capability than before.</summary>
    private void ShowStructuredIfPossible(string artifact)
    {
        RawPatch = TextSanitizer.Clean(artifact);
        var document = DiffParser.Parse(artifact);
        if (document.Files.Count == 0)
        {
            Document = null;
            ShowStructured = false;
            return;
        }
        Document = document;
        ShowStructured = true;
    }

    /// <summary>Starts another AI turn asking for a corrected artifact, bounded to MaxAutoRetries.
    /// Never applies/executes anything itself - it only calls RunTurnAsync, whose normal completion
    /// path (IngestArtifactAsync) is what re-renders the proposal and re-enables Apply.</summary>
    private async Task<bool> RetryWithFeedbackAsync(string feedbackPrompt, CancellationToken token)
    {
        if (!RetryPolicy.ShouldRetry(_retryCount, MaxAutoRetries)) return false;
        _retryCount++;
        StatusMessage = $"Retrying ({_retryCount}/{MaxAutoRetries})...";
        await RunTurnAsync(feedbackPrompt, null, token);
        return true;
    }

    /// <summary>Called when a DiffView checkbox is toggled: rebuilds the patch from the current
    /// selection and re-validates it.</summary>
    public async Task OnSelectionChangedAsync(CancellationToken token)
    {
        if (Document is null) return;
        var patch = Document.BuildPatch();
        RawPatch = patch;
        if (string.IsNullOrWhiteSpace(patch))
        {
            CanApply = false;
            StatusMessage = "Select at least one change to apply.";
            return;
        }
        var validation = await _git.ValidateDiffAsync(_workingDirectory, patch, _protectedPaths, token);
        StatusMessage = validation.Message;
        CanApply = validation.IsValid;
    }

    /// <summary>Toggles between the checkbox-driven structured view and the raw editable patch text.</summary>
    public void ToggleRawView()
    {
        if (ShowStructured) { ShowStructured = false; return; }
        var reparsed = DiffParser.Parse(RawPatch);
        if (reparsed.Files.Count > 0) Document = reparsed;
        ShowStructured = true;
    }

    public void SetRawPatch(string text) => RawPatch = text;

    public async Task ApplyAsync(CancellationToken token)
    {
        var currentDiff = RawPatch;
        var validation = await _git.ValidateDiffAsync(_workingDirectory, currentDiff, _protectedPaths, token);
        if (!validation.IsValid)
        {
            StatusMessage = validation.Message;
            CanApply = false;
            return;
        }
        await _git.ApplyDiffAsync(_workingDirectory, currentDiff, token);
        _appliedDiff = currentDiff;
        StatusMessage = "Patch applied locally";
        CanApply = false;
        CanVerify = true;
        if (string.IsNullOrWhiteSpace(VerifyCommand)) VerifyCommand = TestCommandDetector.Detect(_workingDirectory) ?? string.Empty;
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        var command = VerifyCommand.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            StatusMessage = "Enter a build/test command first.";
            return;
        }
        var result = await _shell.RunAsync(_workingDirectory, command, token);
        var output = string.Join('\n', new[] { result.Output, result.Error }.Where(s => !string.IsNullOrWhiteSpace(s)));
        StatusMessage = string.IsNullOrWhiteSpace(output) ? $"Exit code {result.ExitCode}" : output;
        CanRevert = result.ExitCode != 0;
    }

    public async Task RevertAsync(CancellationToken token)
    {
        if (_appliedDiff is null) return;
        await _git.RevertDiffAsync(_workingDirectory, _appliedDiff, token);
        StatusMessage = "Reverted";
        CanApply = true;
        CanVerify = false;
        CanRevert = false;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
