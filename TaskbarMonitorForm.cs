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
    private readonly System.Windows.Forms.Timer _shellTimer = new() { Interval = 250 };

    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastReceived;
    private long _lastSent;
    private long _sessionReceived;
    private long _sessionSent;
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private CalendarPopup? _calendar;
    private Rectangle _taskbarRect;
    private bool _shellVisible;
    private DateTime _lastZOrderRefreshUtc = DateTime.MinValue;

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

        // Prevent a one-frame flash over a fullscreen app while the initial
        // taskbar/fullscreen state is being detected.
        Opacity = 0;

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
        realignItem.Click += (_, _) => MaintainShellState(forcePosition: true);
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
        _shellTimer.Tick += (_, _) => MaintainShellState();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            ResetAdapter();
            UpdateNetworkSpeed();
            UpdateClock();
            MaintainShellState(forcePosition: true);
            _networkTimer.Start();
            _shellTimer.Start();
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
        if (!_shellVisible)
            return;

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

    private void MaintainShellState(bool forcePosition = false)
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        var taskbarVisible = TryGetVisibleTaskbar(out var taskbarRect);
        var fullscreenForeground = taskbarVisible && IsForegroundFullscreen();
        var shouldShow = taskbarVisible && !fullscreenForeground;

        if (!shouldShow)
        {
            _shellVisible = false;
            if (_calendar is { IsDisposed: false })
            {
                _calendar.Close();
                _calendar = null;
            }
            if (Visible)
                Hide();
            return;
        }

        var stateChanged = !_shellVisible;
        _shellVisible = true;
        _taskbarRect = taskbarRect;

        if (forcePosition || stateChanged)
            PositionOnTaskbar(taskbarRect);

        if (!Visible)
            Show();

        if (Opacity < 1)
            Opacity = 1;

        // Do not use SWP_SHOWWINDOW repeatedly. That was the source of the
        // visible blink when Windows' Show desktop button changed shell Z-order.
        // Refresh only the Z-order, and at a low frequency while the taskbar is visible.
        if (stateChanged || forcePosition || DateTime.UtcNow - _lastZOrderRefreshUtc >= TimeSpan.FromSeconds(2))
        {
            EnsureAboveTaskbar();
            _lastZOrderRefreshUtc = DateTime.UtcNow;
        }
    }

    private void PositionOnTaskbar(Rectangle taskbar)
    {
        _taskbarRect = taskbar;

        if (taskbar.Width < taskbar.Height)
        {
            var verticalWidth = Math.Max(44, taskbar.Width - 4);
            var verticalHeight = Math.Min(84, taskbar.Height - 8);
            SetBoundsTopMost(
                taskbar.Left + (taskbar.Width - verticalWidth) / 2,
                taskbar.Bottom - verticalHeight - 4,
                verticalWidth,
                verticalHeight);
            return;
        }

        // GetWindowRect returns physical screen pixels. Do not apply DeviceDpi here.
        var showDesktopStrip = Math.Clamp(taskbar.Height / 8, 5, 9);
        var right = taskbar.Right - showDesktopStrip;

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
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE);
    }

    private void EnsureAboveTaskbar()
    {
        if (!IsHandleCreated || IsDisposed || !_shellVisible)
            return;

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE);
    }

    private bool TryGetVisibleTaskbar(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar))
            return false;

        if (!NativeMethods.GetWindowRect(taskbar, out var native))
            return false;

        rect = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        // An auto-hidden taskbar often remains a valid Shell_TrayWnd but is moved
        // almost completely outside the monitor, leaving only a 1-2 px activation edge.
        // Treat that state as hidden as well.
        var bestIntersection = Rectangle.Empty;
        long bestArea = 0;
        foreach (var screen in Screen.AllScreens)
        {
            var intersection = Rectangle.Intersect(rect, screen.Bounds);
            var area = (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
            if (area > bestArea)
            {
                bestArea = area;
                bestIntersection = intersection;
            }
        }

        var totalArea = (long)rect.Width * rect.Height;
        if (totalArea <= 0 || bestArea * 100 < totalArea * 40)
            return false;

        if (rect.Width >= rect.Height && bestIntersection.Height < Math.Min(8, Math.Max(1, rect.Height / 3)))
            return false;
        if (rect.Height > rect.Width && bestIntersection.Width < Math.Min(8, Math.Max(1, rect.Width / 3)))
            return false;

        return true;
    }

    private bool IsForegroundFullscreen()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle)
            return false;

        var className = NativeMethods.GetClassName(foreground);
        if (className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            return false;

        if (!TryGetVisibleWindowBounds(foreground, out var bounds))
            return false;

        var screen = Screen.FromHandle(foreground).Bounds;
        const int tolerance = 2;
        return bounds.Left <= screen.Left + tolerance &&
               bounds.Top <= screen.Top + tolerance &&
               bounds.Right >= screen.Right - tolerance &&
               bounds.Bottom >= screen.Bottom - tolerance;
    }

    private static bool TryGetVisibleWindowBounds(IntPtr window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var extended = new NativeMethods.RECT();
        var hr = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out extended,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0 && extended.Right > extended.Left && extended.Bottom > extended.Top)
        {
            bounds = Rectangle.FromLTRB(extended.Left, extended.Top, extended.Right, extended.Bottom);
            return true;
        }

        if (!NativeMethods.GetWindowRect(window, out var native))
            return false;

        bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static Rectangle? TryGetClockRect()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return null;

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

        if (found == IntPtr.Zero || !NativeMethods.GetWindowRect(found, out var native))
            return null;

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
            BeginInvoke((Action)(() => { _calendar?.Close(); MaintainShellState(forcePosition: true); }));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() => { _calendar?.Close(); ApplyTheme(); MaintainShellState(forcePosition: true); }));
    }

    private void StartupItem_CheckedChanged(object? sender, EventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (_startupItem.Checked)
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exe))
                    throw new InvalidOperationException();
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
        _shellTimer.Stop();
        _calendar?.Close();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _tooltip.Dispose();
        _menu.Dispose();
        base.OnFormClosed(e);
    }

    private static class NativeMethods
    {
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal static readonly IntPtr HWND_TOPMOST = new(-1);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

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

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT attributeValue, int attributeSize);

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
