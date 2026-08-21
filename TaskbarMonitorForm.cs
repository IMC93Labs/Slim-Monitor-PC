using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace SlimMonitorPC;

public sealed class TaskbarMonitorForm : Form
{
    private const string AppName = "Slim Monitor PC";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "SlimMonitorPC";

    private readonly TableLayoutPanel _layout = new();
    private readonly Label _download = new();
    private readonly Label _upload = new();
    private readonly Label _time = new();
    private readonly Label _date = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _adapterItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolTip _tooltip = new();
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _zOrderTimer = new() { Interval = 500 };

    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastReceived;
    private long _lastSent;
    private long _sessionReceived;
    private long _sessionSent;
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private CalendarPopup? _calendar;
    private Rectangle _taskbarRect;

    public TaskbarMonitorForm()
    {
        Text = AppName;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = Padding.Empty;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        ConfigureLayout();

        _adapterItem = new ToolStripMenuItem("Wi-Fi: buscando…") { Enabled = false };
        _startupItem = new ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        _startupItem.CheckedChanged += StartupItem_CheckedChanged;

        var calendarItem = new ToolStripMenuItem("Abrir calendario");
        calendarItem.Click += (_, _) => ToggleCalendar();
        var realignItem = new ToolStripMenuItem("Reajustar a la barra de tareas");
        realignItem.Click += (_, _) => PositionOnTaskbar();
        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => Close();

        _menu.Items.Add(_adapterItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(calendarItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(realignItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);
        ContextMenuStrip = _menu;

        foreach (Control control in _layout.Controls)
        {
            control.ContextMenuStrip = _menu;
            control.MouseUp += ClickSurface_MouseUp;
        }
        _layout.ContextMenuStrip = _menu;
        _layout.MouseUp += ClickSurface_MouseUp;
        MouseUp += ClickSurface_MouseUp;

        _tooltip.AutoPopDelay = 10000;
        _tooltip.InitialDelay = 250;
        _tooltip.ReshowDelay = 100;
        _tooltip.ShowAlways = true;

        ApplyTheme();
        UpdateClock();
        UpdateTooltip();

        _networkTimer.Tick += (_, _) =>
        {
            UpdateClock();
            UpdateNetworkSpeed();
        };
        _zOrderTimer.Tick += (_, _) => EnsureAboveTaskbar();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            PositionOnTaskbar();
            ResetAdapter();
            UpdateNetworkSpeed();
            UpdateClock();
            EnsureAboveTaskbar();
            _networkTimer.Start();
            _zOrderTimer.Start();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private void ConfigureLayout()
    {
        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = new Padding(5, 0, 5, 0);
        _layout.ColumnCount = 2;
        _layout.RowCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureLabel(_download, ContentAlignment.MiddleLeft, 7.4f);
        ConfigureLabel(_upload, ContentAlignment.MiddleLeft, 7.4f);
        ConfigureLabel(_time, ContentAlignment.MiddleRight, 10.2f);
        ConfigureLabel(_date, ContentAlignment.MiddleRight, 9.1f);

        _download.Text = "↓ 0 KB/s";
        _upload.Text = "↑ 0 KB/s";
        _layout.Controls.Add(_download, 0, 0);
        _layout.Controls.Add(_upload, 0, 1);
        _layout.Controls.Add(_time, 1, 0);
        _layout.Controls.Add(_date, 1, 1);
        Controls.Add(_layout);
    }

    private static void ConfigureLabel(Label label, ContentAlignment alignment, float fontSize)
    {
        label.Dock = DockStyle.Fill;
        label.Margin = Padding.Empty;
        label.Padding = Padding.Empty;
        label.TextAlign = alignment;
        label.Font = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Point);
        label.AutoEllipsis = false;
        label.UseMnemonic = false;
        label.Cursor = Cursors.Hand;
        label.BackColor = Color.Transparent;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        _date.Text = now.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    }

    private void ClickSurface_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ToggleCalendar();
    }

    private void ToggleCalendar()
    {
        if (_calendar is { IsDisposed: false, Visible: true })
        {
            _calendar.Close();
            _calendar = null;
            return;
        }

        _calendar?.Dispose();
        _calendar = new CalendarPopup(IsSystemLightTheme());
        _calendar.FormClosed += (_, _) => _calendar = null;
        _calendar.ShowNear(Bounds, _taskbarRect);
    }

    private void UpdateNetworkSpeed()
    {
        try
        {
            var current = FindActiveWifiAdapter();
            if (current is null)
            {
                _adapter = null;
                _adapterId = null;
                _download.Text = "↓ —";
                _upload.Text = "↑ —";
                _adapterItem.Text = "Wi-Fi: sin conexión";
                UpdateTooltip();
                return;
            }

            if (_adapterId != current.Id)
            {
                _adapter = current;
                _adapterId = current.Id;
                var stats = current.GetIPv4Statistics();
                _lastReceived = stats.BytesReceived;
                _lastSent = stats.BytesSent;
                _sessionReceived = 0;
                _sessionSent = 0;
                _lastSampleUtc = DateTime.UtcNow;
                _download.Text = "↓ 0 KB/s";
                _upload.Text = "↑ 0 KB/s";
                _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
                UpdateTooltip();
                return;
            }

            _adapter = current;
            var statsNow = current.GetIPv4Statistics();
            var nowUtc = DateTime.UtcNow;
            var elapsed = Math.Max((nowUtc - _lastSampleUtc).TotalSeconds, 0.1);
            var rxDelta = Math.Max(0, statsNow.BytesReceived - _lastReceived);
            var txDelta = Math.Max(0, statsNow.BytesSent - _lastSent);

            _sessionReceived += rxDelta;
            _sessionSent += txDelta;
            _lastReceived = statsNow.BytesReceived;
            _lastSent = statsNow.BytesSent;
            _lastSampleUtc = nowUtc;

            _download.Text = $"↓ {FormatRate(rxDelta / elapsed)}";
            _upload.Text = $"↑ {FormatRate(txDelta / elapsed)}";
            _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
            UpdateTooltip();
        }
        catch
        {
            _download.Text = "↓ —";
            _upload.Text = "↑ —";
        }
    }

    private void ResetAdapter()
    {
        _adapter = null;
        _adapterId = null;
        _lastReceived = 0;
        _lastSent = 0;
        _sessionReceived = 0;
        _sessionSent = 0;
        _lastSampleUtc = DateTime.UtcNow;
    }

    private static NetworkInterface? FindActiveWifiAdapter()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .ToArray();

        return adapters.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            ?? adapters.FirstOrDefault(n =>
            {
                var text = $"{n.Name} {n.Description}".ToLowerInvariant();
                return text.Contains("wi-fi") || text.Contains("wifi") || text.Contains("wireless") || text.Contains("wlan");
            });
    }

    private static string FriendlyAdapterName(NetworkInterface adapter)
    {
        var name = adapter.Name.Trim();
        return name.Length <= 30 ? name : name[..27] + "…";
    }

    private void UpdateTooltip()
    {
        var text = _adapter is null
            ? "Wi-Fi sin conexión\nClic: calendario\nClic derecho: opciones"
            : $"{FriendlyAdapterName(_adapter)}\n" +
              $"Recibido desde que se abrió: {FormatBytes(_sessionReceived)}\n" +
              $"Enviado desde que se abrió: {FormatBytes(_sessionSent)}\n" +
              "Clic: calendario\nClic derecho: opciones";

        foreach (var control in new Control[] { _layout, _download, _upload, _time, _date })
            _tooltip.SetToolTip(control, text);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B/s";
        var kb = bytesPerSecond / 1024d;
        if (kb < 1024) return kb < 10 ? $"{kb:0.0} KB/s" : $"{kb:0} KB/s";
        var mb = kb / 1024d;
        if (mb < 1024) return mb < 10 ? $"{mb:0.0} MB/s" : $"{mb:0} MB/s";
        return $"{mb / 1024d:0.00} GB/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.0} KB";
        var mb = kb / 1024d;
        if (mb < 1024) return $"{mb:0.0} MB";
        return $"{mb / 1024d:0.00} GB";
    }

    private void PositionOnTaskbar()
    {
        if (!IsHandleCreated)
            return;

        if (!TryGetTaskbarRect(out var taskbar))
        {
            var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            const int fallbackHeight = 40;
            const int fallbackWidth = 160;
            _taskbarRect = new Rectangle(screen.Left, screen.Bottom - fallbackHeight, screen.Width, fallbackHeight);
            SetBoundsTopMost(screen.Right - fallbackWidth - 6, screen.Bottom - fallbackHeight + 2, fallbackWidth, fallbackHeight - 4);
            return;
        }

        _taskbarRect = taskbar;
        if (taskbar.Width < taskbar.Height)
        {
            var verticalWidth = Math.Max(44, taskbar.Width - 4);
            var verticalHeight = Math.Min(84, taskbar.Height - 8);
            SetBoundsTopMost(taskbar.Left + (taskbar.Width - verticalWidth) / 2, taskbar.Bottom - verticalHeight - 4, verticalWidth, verticalHeight);
            return;
        }

        // GetWindowRect already returns physical screen pixels. Do not apply DeviceDpi
        // here, otherwise Windows scaling makes the overlay much wider than intended.
        var showDesktopStrip = Math.Clamp(taskbar.Height / 8, 5, 9);
        var right = taskbar.Right - showDesktopStrip;

        // Keep the whole app inside the native clock/date area plus a small traffic
        // column. On a normal 40-48 px Windows 11 taskbar this is about 150-170 px.
        var nativeClock = TryGetClockRect();
        var clockWidth = nativeClock?.Width ?? (int)Math.Round(taskbar.Height * 1.9);
        var networkWidth = Math.Clamp((int)Math.Round(taskbar.Height * 1.75), 66, 82);
        var width = Math.Clamp(clockWidth + networkWidth, 146, 176);

        var insetY = Math.Clamp(taskbar.Height / 20, 1, 3);
        var height = Math.Max(30, taskbar.Height - insetY * 2);
        var x = Math.Max(taskbar.Left, right - width);
        var y = taskbar.Top + insetY;

        SetBoundsTopMost(x, y, width, height);
    }

    private void SetBoundsTopMost(int x, int y, int width, int height)
    {
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void EnsureAboveTaskbar()
    {
        if (!IsHandleCreated || IsDisposed) return;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static bool TryGetTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var native)) return false;
        rect = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return rect.Width > 0 && rect.Height > 0;
    }

    private static Rectangle? TryGetClockRect()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return null;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(taskbar, (hwnd, _) =>
        {
            if (NativeMethods.GetClassName(hwnd).Equals("TrayClockWClass", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero || !NativeMethods.GetWindowRect(found, out var native)) return null;
        return Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
    }

    private void ApplyTheme()
    {
        var light = IsSystemLightTheme();
        var background = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
        var foreground = light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
        BackColor = background;
        ForeColor = foreground;
        _layout.BackColor = background;
        foreach (var label in new[] { _download, _upload, _time, _date })
            label.ForeColor = foreground;
        _menu.RenderMode = ToolStripRenderMode.System;
    }

    internal static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() => { _calendar?.Close(); PositionOnTaskbar(); }));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() => { _calendar?.Close(); ApplyTheme(); PositionOnTaskbar(); }));
    }

    private void StartupItem_CheckedChanged(object? sender, EventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (_startupItem.Checked)
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exe)) throw new InvalidOperationException();
                key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            _startupItem.CheckedChanged -= StartupItem_CheckedChanged;
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.CheckedChanged += StartupItem_CheckedChanged;
            MessageBox.Show("No se pudo cambiar el inicio automático.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _networkTimer.Stop();
        _zOrderTimer.Stop();
        _calendar?.Close();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _tooltip.Dispose();
        _menu.Dispose();
        base.OnFormClosed(e);
    }

    private static class NativeMethods
    {
        internal static readonly IntPtr HWND_TOPMOST = new(-1);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

        internal static string GetClassName(IntPtr hWnd)
        {
            var text = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
