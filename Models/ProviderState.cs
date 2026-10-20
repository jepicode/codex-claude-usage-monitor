using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexClaudeUsageWidget.Models;

public sealed record QuotaWindow(string Id, string Label, double UsedPercent, DateTimeOffset? ResetsAt)
{
    public string ResetText => ResetsAt is null
        ? "Reset time unavailable"
        : $"Resets {ResetsAt.Value.ToLocalTime():ddd, h:mm tt}";
}
public sealed record QuotaSnapshot(string Provider, DateTimeOffset FetchedAt, IReadOnlyList<QuotaWindow> Windows);

public sealed class ProviderState : INotifyPropertyChanged
{
    private string _status = "Not connected";
    private string _error = "";
    private DateTimeOffset? _fetchedAt;
    private bool _hasCredentials;
    private bool _codeEntryVisible;

    public ProviderState(string name) => Name = name;
    public string Name { get; }
    public ObservableCollection<QuotaWindow> Windows { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        private set { if (_status == value) return; _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(TooltipText)); }
    }

    public string Error
    {
        get => _error;
        private set { if (_error == value) return; _error = value; OnPropertyChanged(); }
    }

    public DateTimeOffset? FetchedAt
    {
        get => _fetchedAt;
        private set { if (_fetchedAt == value) return; _fetchedAt = value; OnPropertyChanged(); }
    }

    public string TooltipText => Windows.Count > 0
        ? $"{Windows.MaxBy(window => window.UsedPercent)!.UsedPercent:0}% used"
        : Status;
    public string EmptyText => Windows.Count > 0
        ? ""
        : Status == "Not connected" ? "Connect your account to see the provider's current quota." : "Quota data is not available yet.";
    public System.Windows.Visibility EmptyVisibility => Windows.Count == 0
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility ErrorVisibility => string.IsNullOrWhiteSpace(Error)
        ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility ConnectVisibility =>
        !_hasCredentials || Status == "Sign-in required" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility DisconnectVisibility => _hasCredentials
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility CodeVisibility => _codeEntryVisible
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public void SetDisconnected()
    {
        _hasCredentials = false;
        _codeEntryVisible = false;
        FetchedAt = null;
        Error = "";
        Status = "Not connected";
        NotifyDerived();
    }

    public void SetWaitingForBrowser()
    {
        Error = "";
        Status = "Waiting for browser";
        NotifyDerived();
    }

    public void SetConnectedNoQuota()
    {
        _hasCredentials = true;
        Error = "";
        Status = "Checking…";
        NotifyDerived();
    }

    public void SetCodeEntryVisible(bool visible)
    {
        _codeEntryVisible = visible;
        OnPropertyChanged(nameof(CodeVisibility));
    }

    public void SetLoading()
    {
        Error = "";
        Status = "Checking…";
        NotifyDerived();
    }

    public void SetError(string error, bool requiresLogin = false)
    {
        Error = error;
        Status = requiresLogin ? "Sign-in required" : "Unavailable";
        NotifyDerived();
    }

    public void SetCachedQuota(QuotaSnapshot snapshot)
    {
        Windows.Clear();
        foreach (var window in snapshot.Windows) Windows.Add(window);
        FetchedAt = snapshot.FetchedAt;
        Error = "";
        Status = "Showing last-known";
        NotifyDerived();
    }

    public void SetQuota(QuotaSnapshot snapshot)
    {
        _hasCredentials = true;
        Windows.Clear();
        foreach (var window in snapshot.Windows) Windows.Add(window);
        FetchedAt = snapshot.FetchedAt;
        Error = "";
        Status = "Connected";
        NotifyDerived();
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(TooltipText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ConnectVisibility));
        OnPropertyChanged(nameof(DisconnectVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(CodeVisibility));
    }


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
