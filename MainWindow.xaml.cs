using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.IO;
using System.Net.Http;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using CodexClaudeUsageWidget.Models;
using CodexClaudeUsageWidget.Services;
using MessageBox = System.Windows.MessageBox;

namespace CodexClaudeUsageWidget;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly LocalStorage _storage = new();
    private readonly ProviderClient _providerClient;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, DateTimeOffset> _rateLimitUntil = [];
    private int _refreshInProgress;

    public MainViewModel ViewModel { get; } = new();
    public event Action? UsageChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _providerClient = new ProviderClient(_storage);

        ViewModel.Codex.PropertyChanged += OnProviderChanged;
        ViewModel.Claude.PropertyChanged += OnProviderChanged;
        try
        {
            var cachedUsage = _storage.ReadQuotaCache();
            if (_providerClient.IsConnected("codex"))
            {
                ViewModel.Codex.SetConnectedNoQuota();
                if (cachedUsage.TryGetValue("codex", out var codexCache)) ViewModel.Codex.SetCachedQuota(codexCache);
            }
            if (_providerClient.IsConnected("claude"))
            {
                ViewModel.Claude.SetConnectedNoQuota();
                if (cachedUsage.TryGetValue("claude", out var claudeCache)) ViewModel.Claude.SetCachedQuota(claudeCache);
            }
        }
        catch (Exception)
        {
            ViewModel.Codex.SetError("Saved local sign-in data could not be read. Reconnect the provider.");
            ViewModel.Claude.SetError("Saved local sign-in data could not be read. Reconnect the provider.");
        }

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();
        _refreshTimer.Start();
    }

    public async Task RefreshUsageAsync()
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0) return;
        try
        {
            await Task.WhenAll(
                RefreshProviderAsync("codex", ViewModel.Codex),
                RefreshProviderAsync("claude", ViewModel.Claude));
            var latest = new[] { ViewModel.Codex.FetchedAt, ViewModel.Claude.FetchedAt }
                .Where(value => value.HasValue).Max();
            ViewModel.UpdatedText = latest.HasValue
                ? $"Updated {latest.Value.ToLocalTime():h:mm tt}"
                : "Not refreshed yet";
            UsageChanged?.Invoke();
        }
        finally { Interlocked.Exchange(ref _refreshInProgress, 0); }
    }

    private async Task RefreshProviderAsync(string provider, ProviderState state)
    {
        bool connected;
        try { connected = _providerClient.IsConnected(provider); }
        catch (Exception)
        {
            await Dispatcher.InvokeAsync(() => state.SetError("Saved local sign-in data could not be read.", requiresLogin: true));
            return;
        }
        if (_rateLimitUntil.TryGetValue(provider, out var retryAfter))
        {
            if (retryAfter > DateTimeOffset.UtcNow)
            {
                await Dispatcher.InvokeAsync(() => state.SetError($"Provider requested a cooldown. Retry after {retryAfter.ToLocalTime():h:mm tt}."));
                return;
            }
            _rateLimitUntil.Remove(provider);
        }
        if (!connected)
        {
            await Dispatcher.InvokeAsync(state.SetDisconnected);
            return;
        }
        await Dispatcher.InvokeAsync(state.SetLoading);
        try
        {
            var quota = await _providerClient.GetUsageAsync(provider);
            await Dispatcher.InvokeAsync(() => state.SetQuota(quota));
            try { _storage.WriteQuotaCache(provider, quota); }
            catch (IOException exception) { Trace.TraceWarning("Could not cache the latest {0} quota: {1}", provider, exception.Message); }
        }
        catch (Exception exception)
        {
            var requiresLogin = exception is UnauthorizedAccessException || exception.Message.Contains("sign in again", StringComparison.OrdinalIgnoreCase);
            var message = exception.Message;
            if (message.Contains("rate-limited", StringComparison.OrdinalIgnoreCase))
            {
                var cooldownUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                _rateLimitUntil[provider] = cooldownUntil;
                message = $"Provider rate-limited the usage check. Retry after {cooldownUntil.ToLocalTime():h:mm tt}.";
            }
            await Dispatcher.InvokeAsync(() => state.SetError(message, requiresLogin));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshUsageAsync();

    private async void CodexLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Codex.SetWaitingForBrowser();
            UsageChanged?.Invoke();
            await _providerClient.StartCodexLoginAsync();
            ViewModel.Codex.SetConnectedNoQuota();
            await RefreshUsageAsync();
        }
        catch (Exception exception)
        {
            ViewModel.Codex.SetError(exception.Message);
        }
        UsageChanged?.Invoke();
    }

    private void ClaudeLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var message = _providerClient.BeginClaudeLogin();
            ViewModel.Claude.SetCodeEntryVisible(true);
            ViewModel.Claude.SetWaitingForBrowser();
            MessageBox.Show(this, message, "Claude sign-in", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ViewModel.Claude.SetError(exception.Message);
        }
        UsageChanged?.Invoke();
    }

    private async void ClaudeComplete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _providerClient.CompleteClaudeLoginAsync(ClaudeCallback.Text);
            ClaudeCallback.Clear();
            ViewModel.Claude.SetCodeEntryVisible(false);
            ViewModel.Claude.SetConnectedNoQuota();
            await RefreshUsageAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or CryptographicException or JsonException)
        {
            ViewModel.Claude.SetError(exception.Message);
        }
        UsageChanged?.Invoke();
    }


    private void ClearStoredCredentials_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "This removes the Codex and Claude sign-ins saved by the widget and their cached quotas. Continue?",
            "Clear widget sign-ins",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            _storage.DeleteCredentials();
            _storage.DeleteQuotaCache("codex");
            _storage.DeleteQuotaCache("claude");
            ViewModel.Codex.SetDisconnected();
            ViewModel.Claude.SetDisconnected();
            UsageChanged?.Invoke();
        }
        catch (IOException exception)
        {
            MessageBox.Show(this, exception.Message, "Could not clear saved sign-ins", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void CodexDisconnect_Click(object sender, RoutedEventArgs e) => Disconnect("codex", ViewModel.Codex);
    private void ClaudeDisconnect_Click(object sender, RoutedEventArgs e) => Disconnect("claude", ViewModel.Claude);

    private void Disconnect(string provider, ProviderState state)
    {
        try
        {
            _providerClient.Disconnect(provider);
            _storage.DeleteQuotaCache(provider);
            state.SetDisconnected();
            UsageChanged?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or CryptographicException)
        {
            MessageBox.Show(this, exception.Message, "Could not disconnect", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CodexUsage_Click(object sender, RoutedEventArgs e) => OpenUrl("https://chatgpt.com/codex/settings/usage");
    private void ClaudeUsage_Click(object sender, RoutedEventArgs e) => OpenUrl("https://claude.ai/settings/usage");
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void PositionForTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, workArea.Right - Width - 8);
        Top = Math.Max(workArea.Top, workArea.Bottom - Height - 8);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.IsShuttingDown) return;
        e.Cancel = true;
        Hide();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e) => UsageChanged?.Invoke();

    public void DisposeServices()
    {
        _refreshTimer.Stop();
        _providerClient.Dispose();
    }
}
