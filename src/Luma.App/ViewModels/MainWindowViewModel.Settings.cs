using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ToggleLeanChat()
    {
        AppSettings.Current.LeanChatMode = !AppSettings.Current.LeanChatMode;
        AppSettings.Current.Save();
        NotifyLeanChatChanged();
    }

    private void NotifyLeanChatChanged()
    {
        OnPropertyChanged(nameof(LeanChatEnabled));
        OnPropertyChanged(nameof(LeanChatMenuLabel));
        OnPropertyChanged(nameof(LeanChatChipLabel));
        NotifySurfaceStateChanged();
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(HasAssistantMemory));
        OnPropertyChanged(nameof(AssistantMemoryPreview));
        SelectedEffortIndex = AppSettings.EffortToIndex(AppSettings.Current.ChatReasoningEffort);
        NotifyChaosChanged();
        NotifyLeanChatChanged();
        NotifyModelPickerChanged();
    }

    private void SelectProvider(object? parameter)
    {
        if (!TryParseMenuIndex(parameter, out var index)) return;
        SelectedProviderIndex = Math.Clamp(index, 0, Providers.Count - 1);
        AppSettings.Current.Provider = SelectedProviderIndex;
        AppSettings.Current.Save();
    }

    private void SelectEffort(object? parameter)
    {
        if (!TryParseMenuIndex(parameter, out var index)) return;
        SelectedEffortIndex = index;
    }

    private static bool TryParseMenuIndex(object? parameter, out int index)
    {
        switch (parameter)
        {
            case int i:
                index = i;
                return true;
            case string s when int.TryParse(s, out index):
                return true;
            default:
                index = 0;
                return false;
        }
    }

    private void NotifyModelPickerChanged()
    {
        OnPropertyChanged(nameof(ModelPickerLabel));
        OnPropertyChanged(nameof(ProviderMenuClaude));
        OnPropertyChanged(nameof(ProviderMenuCodex));
        OnPropertyChanged(nameof(ProviderMenuGrok));
        OnPropertyChanged(nameof(EffortMenuLow));
        OnPropertyChanged(nameof(EffortMenuMedium));
        OnPropertyChanged(nameof(EffortMenuHigh));
    }

    private static string MenuCheck(bool selected, string label) => selected ? $"✓  {label}" : $"    {label}";
}
