namespace Luma.App.Services;

/// <summary>Dual-provider “split-brain” answer with keep A / B / both selection.</summary>
public sealed class SplitBrainResult : System.ComponentModel.INotifyPropertyChanged
{
    private string? _chosen; // "A", "B", "Both"

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public required string ProviderA { get; init; }
    public required string ProviderB { get; init; }
    public required string TextA { get; init; }
    public required string TextB { get; init; }

    public string? Chosen
    {
        get => _chosen;
        private set
        {
            if (_chosen == value) return;
            _chosen = value;
            PropertyChanged?.Invoke(this, new(nameof(Chosen)));
            PropertyChanged?.Invoke(this, new(nameof(HasChosen)));
            PropertyChanged?.Invoke(this, new(nameof(MergedText)));
            PropertyChanged?.Invoke(this, new(nameof(CanChoose)));
        }
    }

    public bool HasChosen => !string.IsNullOrEmpty(Chosen);
    public bool CanChoose => !HasChosen;

    public string MergedText => Chosen switch
    {
        "A" => TextA,
        "B" => TextB,
        "Both" => $"## {ProviderA}\n{TextA.Trim()}\n\n## {ProviderB}\n{TextB.Trim()}",
        _ => $"## {ProviderA}\n{TextA.Trim()}\n\n## {ProviderB}\n{TextB.Trim()}",
    };

    public void Choose(string side)
    {
        if (side is not ("A" or "B" or "Both")) return;
        Chosen = side;
    }
}

public static class SplitBrainPrompts
{
    public static string Explainer(string userPrompt) =>
        "SPLIT-BRAIN ROLE: Explainer. Do not edit files. Focus on understanding, trade-offs, and a clear recommendation.\n\n" +
        "User request:\n" + userPrompt.Trim();

    public static string Implementer(string userPrompt) =>
        "SPLIT-BRAIN ROLE: Implementer. Be concrete and action-oriented. Prefer exact steps, code, or file-level changes. " +
        "If you would edit files, describe the patch clearly (you may still use tools if available).\n\n" +
        "User request:\n" + userPrompt.Trim();
}
