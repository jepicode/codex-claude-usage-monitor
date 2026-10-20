using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexClaudeUsageWidget.Models;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _updatedText = "Not refreshed yet";
    public ProviderState Codex { get; } = new("Codex");
    public ProviderState Claude { get; } = new("Claude");
    public event PropertyChangedEventHandler? PropertyChanged;

    public string UpdatedText
    {
        get => _updatedText;
        set { if (_updatedText == value) return; _updatedText = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
