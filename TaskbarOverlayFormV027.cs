using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace SlimMonitorPC;

public sealed class TaskbarOverlayFormV027 : Form
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
    private readonly ToolStripMenuItem _downloadInfoItem;
    private readonly ToolStripMenuItem _uploadInfoItem;
    private readonly ToolStripMenuItem _receivedItem;
    private readonly ToolStripMenuItem _sentItem;
    private readonly ToolStripMenuItem _startupItem;

    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _shellTimer = new() { Interval = 200 };

    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastReceived;
    private long _lastSent;
    private long _sessionReceived;
    private long _sessionSent;
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private double _rxPerSecond;
    private double _txPerSecond;

    private CalendarPopup? _calendar;
    private Rectangle _taskbarRect;
    private Rectangle _lastPositionedTaskbarRect;
    private IntPtr _taskbarOwner;
    private bool _overlayShown;

    public TaskbarOverlayFormV027()
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
        Opacity = 0;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        ConfigureLayout();

        _adapterItem = CreateInfoItem("Wi-Fi: buscando…");
        _downloadInfoItem = CreateInfoItem("Descarga actual: 0 B/s");
        _uploadInfoItem = CreateInfoItem("Subida actual: 0 B/s");
        _receivedItem = CreateInfoItem("Recibido desde que se abrió: 0 B");
        _sentItem = CreateInfoItem("Enviado desde que se abrió: 0 B");

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
        _menu.Items.Add(_downloadInfoItem);
        _menu.Items.Add(_uploadInfoItem);
        _menu.Items.Add(_receivedItem);
        _menu.Items.Add(_sentItem);
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

        ApplyTheme();
        UpdateClock();
        UpdateMenuInfo();

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_SYSCOMMAND &&
            (m.WParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
        {
            return;
        }

        base.WndProc(ref m);

        if (m.Msg == NativeMethods.WM_SIZE && m.WParam.ToInt64() == NativeMethods.SIZE_MINIMIZED)
        {
            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed && IsHandleCreated && !_taskbarRect.IsEmpty)
                {
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                    PositionOnTaskbar(_taskbarRect);
                }
            }));
        }
    }

    private static ToolStripMenuItem CreateInfoItem(string text) => new(text) { Enabled = false };

    private void ConfigureLayout()
    {
        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = new Padding(4, 0, 3, 0);
        _layout.ColumnCount = 2;
        _layout.RowCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureLabel(_download, ContentAlignment.MiddleLeft, 6.8f);
        ConfigureLabel(_upload, ContentAlignment.MiddleLeft, 6.8f);
        ConfigureLabel(_time, ContentAlignment.MiddleRight, 10.4f);
        ConfigureLabel(_date, ContentAlignment.MiddleRight, 9.0f);

        _download.Text = "↓ 0 B/s";
        _upload.Text = "↑ 0 B/s";
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
        if (!_overlayShown)
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
                _rxPerSecond = 0;
                _txPerSecond = 0;
                _download.Text = "↓ —";
                _upload.Text = "↑ —";
                UpdateMenuInfo();
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
                _rxPerSecond = 0;
                _txPerSecond = 0;
                _lastSampleUtc = DateTime.UtcNow;
                _download.Text = "↓ 0 B/s";
                _upload.Text = "↑ 0 B/s";
                UpdateMenuInfo();
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
            _rxPerSecond = rxDelta / elapsed;
            _txPerSecond = txDelta / elapsed;

            _download.Text = $"↓ {FormatRate(_rxPerSecond)}";
            _upload.Text = $"↑ {FormatRate(_txPerSecond)}";
            UpdateMenuInfo();
        }
        catch
        {
            _rxPerSecond = 0;
            _txPerSecond = 0;
            _download.Text = "↓ —";
            _upload.Text = "↑ —";
            UpdateMenuInfo();
        }
    }

    private void UpdateMenuInfo()
    {
        _adapterItem.Text = _adapter is null ? "Wi-Fi: sin conexión" : $"Wi-Fi: {FriendlyAdapterName(_adapter)}";
        _downloadInfoItem.Text = $"Descarga actual: {FormatRate(_rxPerSecond)}";
        _uploadInfoItem.Text = $"Subida actual: {FormatRate(_txPerSecond)}";
        _receivedItem.Text = $"Recibido desde que se abrió: {FormatBytes(_sessionReceived)}";
        _sentItem.Text = $"Enviado desde que se abrió: {FormatBytes(_sessionSent)}";
    }

    private void ResetAdapter()
    {
        _adapter = null;
        _adapterId = null;
        _lastReceived = 0;
        _lastSent = 0;
        _sessionReceived = 0;
        _sessionSent = 0;
        _rxPerSecond = 0;
        _txPerSecond = 0;
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

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero ||
            !NativeMethods.IsWindowVisible(taskbar) ||
            !TryGetVisibleTaskbarRect(taskbar, out var rect) ||
            !IsTaskbarOnScreen(rect))
        {
            HideOverlay();
            return;
        }

        EnsureTaskbarOwner(taskbar);
        _taskbarRect = rect;

        if (IsForeignFullscreenCoveringTaskbar(taskbar, rect))
        {
            HideOverlay();
            return;
        }

        var geometryChanged = rect != _lastPositionedTaskbarRect;
        if (forcePosition || geometryChanged)
        {
            PositionOnTaskbar(rect);
            _lastPositionedTaskbarRect = rect;
        }

        var nativeVisible = NativeMethods.IsWindowVisible(Handle) && !NativeMethods.IsIconic(Handle);
        if (!_overlayShown || !nativeVisible)
        {
            _overlayShown = true;
            Opacity = 1;
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            PositionOnTaskbar(rect);
            return;
        }

        if (!IsOverlayFrontmost())
        {
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
    }

    private void HideOverlay()
    {
        if (!_overlayShown && !NativeMethods.IsWindowVisible(Handle))
            return;

        _overlayShown = false;
        _calendar?.Close();
        _calendar = null;
        NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
    }

    private void EnsureTaskbarOwner(IntPtr taskbar)
    {
        if (_taskbarOwner == taskbar)
            return;

        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, taskbar);
        _taskbarOwner = taskbar;
    }

    private void PositionOnTaskbar(Rectangle taskbar)
    {
        if (taskbar.Width < taskbar.Height)
        {
            var verticalWidth = Math.Max(42, taskbar.Width - 2);
            var verticalHeight = Math.Min(84, taskbar.Height - 4);
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                taskbar.Left + Math.Max(0, (taskbar.Width - verticalWidth) / 2),
                taskbar.Bottom - verticalHeight - 2,
                verticalWidth,
                verticalHeight,
                NativeMethods.SWP_NOACTIVATE);
            return;
        }

        var taskbarHeight = taskbar.Height;
        var showDesktopStrip = Math.Clamp(taskbarHeight / 8, 5, 9);
        var width = Math.Clamp((int)Math.Round(taskbarHeight * 3.30), 150, 160);

        // The visible taskbar rectangle is derived from Screen.WorkingArea rather
        // than the larger transparent Shell_TrayWnd composition bounds. Keep only
        // the 1 px top separator visible and cover the rest, including the old date.
        var topInset = taskbarHeight > 32 ? 1 : 0;
        var height = Math.Max(30, taskbarHeight - topInset);
        var x = Math.Max(taskbar.Left, taskbar.Right - showDesktopStrip - width);
        var y = taskbar.Top + topInset;

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE);
    }

    private bool IsOverlayFrontmost()
    {
        if (!IsHandleCreated || !NativeMethods.IsWindowVisible(Handle) || Width <= 0 || Height <= 0)
            return false;

        var point = new NativeMethods.POINT(Left + Math.Max(1, Width / 2), Top + Math.Max(1, Height / 2));
        var hit = NativeMethods.WindowFromPoint(point);
        if (hit == IntPtr.Zero)
            return false;

        return NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) == Handle;
    }

    private static bool TryGetVisibleTaskbarRect(IntPtr taskbar, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!NativeMethods.GetWindowRect(taskbar, out var native))
            return false;

        var shellRect = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (shellRect.Width <= 0 || shellRect.Height <= 0)
            return false;

        var screen = Screen.FromRectangle(shellRect);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;

        // WorkingArea gives the real visible taskbar boundary on Windows 11. The
        // Shell_TrayWnd rectangle can include a large transparent composition area
        // above the actual bar, which caused v0.2.6 to protrude upward.
        if (work.Bottom < bounds.Bottom)
        {
            rect = Rectangle.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
            return rect.Height > 0;
        }

        if (work.Top > bounds.Top)
        {
            rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
            return rect.Height > 0;
        }

        if (work.Right < bounds.Right)
        {
            rect = Rectangle.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
            return rect.Width > 0;
        }

        if (work.Left > bounds.Left)
        {
            rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
            return rect.Width > 0;
        }

        // Auto-hide does not reserve WorkingArea. Fall back to the shell rectangle;
        // IsTaskbarOnScreen will reject it when Explorer has moved it off-screen.
        rect = shellRect;
        return true;
    }

    private static bool IsTaskbarOnScreen(Rectangle taskbarRect)
    {
        long bestArea = 0;
        foreach (var screen in Screen.AllScreens)
        {
            var intersection = Rectangle.Intersect(taskbarRect, screen.Bounds);
            var area = (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
            bestArea = Math.Max(bestArea, area);
        }

        var totalArea = (long)taskbarRect.Width * taskbarRect.Height;
        return totalArea > 0 && bestArea * 100 >= totalArea * 40;
    }

    private bool IsForeignFullscreenCoveringTaskbar(IntPtr taskbar, Rectangle taskbarRect)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle || foreground == taskbar)
            return false;

        if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            return false;

        NativeMethods.GetWindowThreadProcessId(taskbar, out var taskbarPid);
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
        if (taskbarPid != 0 && taskbarPid == foregroundPid)
            return false;

        if (!TryGetVisibleWindowBounds(foreground, out var bounds))
            return false;

        var taskbarArea = (long)taskbarRect.Width * taskbarRect.Height;
        var taskbarIntersection = Rectangle.Intersect(bounds, taskbarRect);
        var taskbarCoveredArea = (long)Math.Max(0, taskbarIntersection.Width) * Math.Max(0, taskbarIntersection.Height);
        if (taskbarArea <= 0 || taskbarCoveredArea * 100 < taskbarArea * 85)
            return false;

        var screenBounds = Screen.FromRectangle(taskbarRect).Bounds;
        var screenArea = (long)screenBounds.Width * screenBounds.Height;
        var screenIntersection = Rectangle.Intersect(bounds, screenBounds);
        var coveredScreenArea = (long)Math.Max(0, screenIntersection.Width) * Math.Max(0, screenIntersection.Height);

        return screenArea > 0 && coveredScreenArea * 100 >= screenArea * 90;
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
            BeginInvoke((Action)(() =>
            {
                _calendar?.Close();
                MaintainShellState(forcePosition: true);
            }));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() =>
            {
                _calendar?.Close();
                ApplyTheme();
                MaintainShellState(forcePosition: true);
            }));
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
        _menu.Dispose();
        base.OnFormClosed(e);
    }

    private static class NativeMethods
    {
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal const int GWLP_HWNDPARENT = -8;
        internal const int WM_SIZE = 0x0005;
        internal const int WM_SYSCOMMAND = 0x0112;
        internal const int SC_MINIMIZE = 0xF020;
        internal const int SIZE_MINIMIZED = 1;
        internal const int SW_HIDE = 0;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal const uint GA_ROOT = 2;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;

        internal static readonly IntPtr HWND_TOPMOST = new(-1);

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
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT attributeValue, int attributeSize);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct POINT
        {
            public readonly int X;
            public readonly int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
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
