using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;

namespace CodexClaudeUsageWidget;

public partial class App : Application
{
    public static bool IsShuttingDown { get; private set; }
    private NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private Icon? _icon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _window = new MainWindow();
        MainWindow = _window;
        _window.UsageChanged += UpdateTooltip;
        CreateTrayIcon();
        _ = _window.RefreshUsageAsync();
        ShowWindow();
    }

    private void CreateTrayIcon()
    {
        _icon = CreateUsageIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open usage", null, (_, _) => ShowWindow());
        menu.Items.Add("Refresh now", null, async (_, _) =>
        {
            if (_window is not null) await _window.RefreshUsageAsync();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => { IsShuttingDown = true; Shutdown(); });

        _trayIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Codex + Claude usage",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.MouseClick += OnTrayClick;
        _trayIcon.MouseMove += (_, _) => UpdateTooltip();
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Dispatcher.Invoke(() =>
        {
            _window.PositionForTray();
            _window.Show();
            _window.Activate();
        });
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null || _window is null) return;
        var codex = _window.ViewModel.Codex.TooltipText;
        var claude = _window.ViewModel.Claude.TooltipText;
        var text = $"Codex: {codex} | Claude: {claude}";
        _trayIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    private static Icon CreateUsageIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(156, 226, 185)))
        using (var pen = new Pen(Color.FromArgb(20, 44, 34), 3))
        using (var font = new System.Drawing.Font(new System.Drawing.FontFamily("Segoe UI"), 16, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            graphics.DrawEllipse(pen, 1, 1, 30, 30);
            TextRenderer.DrawText(graphics, "U", font, new Rectangle(0, 4, 32, 24), Color.FromArgb(20, 44, 34),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        if (_window is not null) _window.DisposeServices();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _icon?.Dispose();
        base.OnExit(e);
    }
}
