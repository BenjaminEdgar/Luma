using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Luma.App.Services;

namespace Luma.App.Models;

public sealed class ChatMessage(string role, string text, bool isPending = false) : INotifyPropertyChanged, IDisposable
{
    private string _text = text;
    private bool _isPending = isPending;
    private bool _isError;
    private string? _caption;
    private string? _elapsed;
    private string? _turnMeta;
    private bool _isQuestion;
    private string? _question;
    private string _questionAnswer = string.Empty;
    private IReadOnlyList<string> _questionChoices = [];
    private bool _isStreaming;
    private CodeChatSession? _codeSession;
    private Bitmap? _attachmentImage;
    private string? _attachmentPath;
    private string? _imageLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Role { get; } = role;
    public bool IsUser => Role == "user";

    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (!Set(ref _isPending, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSpeaking)));
        }
    }
    public bool IsError { get => _isError; set => Set(ref _isError, value); }
    /// <summary>Small muted line above assistant text, e.g. "* Claude - 6.2 s".</summary>
    public string? Caption { get => _caption; set => Set(ref _caption, value); }
    public string? Elapsed { get => _elapsed; set => Set(ref _elapsed, value); }
    /// <summary>Compact per-turn trust metadata: provider, mode, and attached context.</summary>
    public string? TurnMeta
    {
        get => _turnMeta;
        set
        {
            if (!Set(ref _turnMeta, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTurnMeta)));
        }
    }
    public bool HasTurnMeta => !string.IsNullOrWhiteSpace(_turnMeta);
    /// <summary>In-chat screenshot thumbnail (owned copy; disposed with the message).</summary>
    public Bitmap? AttachmentImage
    {
        get => _attachmentImage;
        private set
        {
            if (ReferenceEquals(_attachmentImage, value)) return;
            _attachmentImage?.Dispose();
            _attachmentImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
        }
    }
    /// <summary>True when a capture file is attached (bitmap may still be loading on some hosts).</summary>
    public bool HasImage => _attachmentPath is not null || _attachmentImage is not null;
    public string? ImageLabel { get => _imageLabel; private set => Set(ref _imageLabel, value); }
    /// <summary>Durable copy path used for the bubble thumbnail (for tests / diagnostics).</summary>
    public string? AttachmentPath => _attachmentPath;

    /// <summary>Copies <paramref name="sourcePath"/> into a durable chat-image file and shows it in the bubble.</summary>
    public void AttachImage(string sourcePath, string label)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Luma", "chat-images");
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var dest = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
            File.Copy(sourcePath, dest, overwrite: true);
            if (_attachmentPath is not null)
            {
                try { File.Delete(_attachmentPath); } catch { /* ignore */ }
            }
            _attachmentPath = dest;
            ImageLabel = label;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
            try
            {
                // Decode to a small width so chat thumbnails stay light and sharp at chip size.
                using var stream = File.OpenRead(dest);
                AttachmentImage = Bitmap.DecodeToWidth(stream, 240);
            }
            catch
            {
                // Path is still attached; UI may show label-only if decode fails off the UI thread.
            }
        }
        catch
        {
            // Image is a bonus; chat text still works if copy fails.
        }
    }

    public void Dispose()
    {
        AttachmentImage = null;
        if (_attachmentPath is null) return;
        try { File.Delete(_attachmentPath); } catch { /* ignore */ }
        _attachmentPath = null;
        ImageLabel = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentPath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
    }
    /// <summary>True while this assistant message is a clarifying question awaiting the user's answer.</summary>
    public bool IsQuestion
    {
        get => _isQuestion;
        set
        {
            if (!Set(ref _isQuestion, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowQuestionCard)));
        }
    }
    public string? Question
    {
        get => _question;
        set
        {
            if (!Set(ref _question, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowQuestionCard)));
        }
    }
    public string QuestionAnswer { get => _questionAnswer; set => Set(ref _questionAnswer, value); }
    public IReadOnlyList<string> QuestionChoices
    {
        get => _questionChoices;
        set
        {
            if (!Set(ref _questionChoices, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasQuestionChoices)));
        }
    }
    public bool HasQuestionChoices => _questionChoices.Count > 0;
    /// <summary>In-chat card is visible while a clarifying question is outstanding.</summary>
    public bool ShowQuestionCard => _isQuestion && !string.IsNullOrWhiteSpace(_question);
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSpeaking)));
        }
    }
    /// <summary>True while the model is thinking or streaming — drives the shared speaking line.</summary>
    public bool IsSpeaking => !IsUser && (_isPending || _isStreaming);
    /// <summary>Present when this assistant message carries a coding-task diff review card.</summary>
    public CodeChatSession? CodeSession
    {
        get => _codeSession;
        set { Set(ref _codeSession, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCodeSession))); }
    }
    public bool HasCodeSession => CodeSession is not null;

    /// <summary>Agent file writes detected after a turn (with optional undo).</summary>
    public ObservableCollection<FileChangeRecord> FileChanges { get; } = [];
    public bool HasFileChanges => FileChanges.Count > 0;

    /// <summary>Extra action chips (e.g. screen-change digest next steps).</summary>
    public ObservableCollection<string> ActionChips { get; } = [];
    public bool HasActionChips => ActionChips.Count > 0;

    public void SetFileChanges(IEnumerable<FileChangeRecord> changes)
    {
        FileChanges.Clear();
        foreach (var change in changes) FileChanges.Add(change);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFileChanges)));
    }

    public void SetActionChips(IEnumerable<string> chips)
    {
        ActionChips.Clear();
        foreach (var chip in chips) ActionChips.Add(chip);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActionChips)));
    }

    private ShowWhereTarget? _showWhere;
    private SplitBrainResult? _splitBrain;

    /// <summary>Normalized screen rect the assistant wants to point at.</summary>
    public ShowWhereTarget? ShowWhere
    {
        get => _showWhere;
        set
        {
            if (ReferenceEquals(_showWhere, value)) return;
            _showWhere = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWhere)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasShowWhere)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWhereButtonLabel)));
        }
    }
    public bool HasShowWhere => _showWhere is not null;
    public string ShowWhereButtonLabel =>
        _showWhere?.Label is { Length: > 0 } label ? $"Show: {label}" : "Show me where";

    public SplitBrainResult? SplitBrain
    {
        get => _splitBrain;
        set
        {
            if (ReferenceEquals(_splitBrain, value)) return;
            _splitBrain = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SplitBrain)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSplitBrain)));
        }
    }
    public bool HasSplitBrain => _splitBrain is not null;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public enum AiProvider { Claude, Codex, Grok }

public enum TaskKind { Chat, Email, Code, Generic, Shell, Browser, Suggest, FollowUp, Route }

public sealed record TaskLaunchRequest(
    TaskKind Kind, string Prompt, AiProvider Provider, string? ImagePath, string? ContextImagePath);

/// <param name="ImagePath">Optional close-up of the region the user selected.</param>
/// <param name="ContextImagePath">Optional full-screen shot grabbed when the panel opened.</param>
public sealed record AiRequest(string Question, string? ImagePath, string? ContextImagePath, IReadOnlyList<ChatMessage> History)
{
    public string? WorkingDirectory { get; init; }
    public TaskKind TaskKind { get; init; } = TaskKind.Chat;
    public string? TaskContext { get; init; }
}
