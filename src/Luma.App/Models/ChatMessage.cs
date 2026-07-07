using System.ComponentModel;
using System.Runtime.CompilerServices;
using Luma.App.Services;

namespace Luma.App.Models;

public sealed class ChatMessage(string role, string text, bool isPending = false) : INotifyPropertyChanged
{
    private string _text = text;
    private bool _isPending = isPending;
    private bool _isError;
    private string? _caption;
    private string? _elapsed;
    private bool _isQuestion;
    private string? _question;
    private string _questionAnswer = string.Empty;
    private bool _isStreaming;
    private CodeChatSession? _codeSession;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Role { get; } = role;
    public bool IsUser => Role == "user";

    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsPending { get => _isPending; set => Set(ref _isPending, value); }
    public bool IsError { get => _isError; set => Set(ref _isError, value); }
    /// <summary>Small muted line above assistant text, e.g. "* Claude - 6.2 s".</summary>
    public string? Caption { get => _caption; set => Set(ref _caption, value); }
    public string? Elapsed { get => _elapsed; set => Set(ref _elapsed, value); }
    /// <summary>True while this assistant message is a clarifying question awaiting the user's answer.</summary>
    public bool IsQuestion { get => _isQuestion; set => Set(ref _isQuestion, value); }
    public string? Question { get => _question; set => Set(ref _question, value); }
    public string QuestionAnswer { get => _questionAnswer; set => Set(ref _questionAnswer, value); }
    public bool IsStreaming { get => _isStreaming; set => Set(ref _isStreaming, value); }
    /// <summary>Present when this assistant message carries a coding-task diff review card.</summary>
    public CodeChatSession? CodeSession
    {
        get => _codeSession;
        set { Set(ref _codeSession, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCodeSession))); }
    }
    public bool HasCodeSession => CodeSession is not null;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public enum AiProvider { Claude, Codex }

public enum TaskKind { Chat, Email, Code, Generic, Shell, Browser }

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
