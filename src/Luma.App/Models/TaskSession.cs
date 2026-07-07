using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Luma.App.Models;

public enum TaskSessionState { Preparing, Asking, Working, ReadyForApproval, Applied, Failed, Cancelled, Reverted }

public sealed class TaskSession(TaskKind kind, string title, AiProvider provider) : INotifyPropertyChanged
{
    private TaskSessionState _state = TaskSessionState.Preparing;
    private string _status = "Preparing task";
    private string? _question;
    private string _answer = string.Empty;
    private string _artifact = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public TaskKind Kind { get; } = kind;
    public string Title { get; } = title;
    public AiProvider Provider { get; } = provider;
    public ObservableCollection<string> Timeline { get; } = [];
    public List<ChatMessage> History { get; } = [];
    public TaskSessionState State { get => _state; set => Set(ref _state, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string? Question { get => _question; set => Set(ref _question, value); }
    public string Answer { get => _answer; set => Set(ref _answer, value); }
    public string Artifact { get => _artifact; set => Set(ref _artifact, value); }

    public void AddStatus(string status)
    {
        Status = status;
        Timeline.Add(status);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
