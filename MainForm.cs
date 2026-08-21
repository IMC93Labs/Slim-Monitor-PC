using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace SlimMonitorPC;

public sealed class MainForm : Form
{
    private const string AppName = "Slim Monitor PC";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "SlimMonitorPC";

    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _zOrderTimer;
    private readonly ToolTip _toolTip;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _adapterItem;
    private readonly ToolStripMenuItem _startupItem;

    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastReceived;
    private long _lastSent;
    private long _sessionReceived;
    private long _sessionSent;
    private DateTime _lastSampleUtc;
    private double _rxPerSecond;
    private double _txPerSecond;
    private CalendarPopup? _calendar;
    private Rectangle _taskbarRect;

    public MainForm()
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
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // The icon is already embedded into the executable.
        }

        ApplyTheme();

        _toolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 250,
            ReshowDelay = 100,
            ShowAlways = true
        };

        _menu = new ContextMenuStrip();
        _adapterItem = new ToolStripMenuItem("Wi-Fi: buscando…") { Enabled = false };
        _startupItem = new ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        _startupItem.CheckedChanged += StartupItem_CheckedChanged;

        var calendarItem = new ToolStripMenuItem("Abrir calendario");
        calendarItem.Click += (_, _) => ToggleCalendar();

        var repositionItem = new ToolStripMenuItem("Reajustar a la barra de tareas");
        repositionItem.Click += (_, _) => PositionOnTaskbar();

        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => Close();

        _menu.Items.Add(_adapterItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(calendarItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(repositionItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);
        ContextMenuStrip = _menu;

        MouseUp += MainForm_MouseUp;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            UpdateNetworkSpeed();
            Invalidate();
        };

        _zOrderTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _zOrderTimer.Tick += (_, _) => EnsureAboveTaskbar();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            PositionOnTaskbar();
            ResetAdapter();
            UpdateNetworkSpeed();
            EnsureAboveTaskbar();
            UpdateTooltip();
            _timer.Start();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        var scale = DeviceDpi / 96f;
        var outerPad = Math.Max(4, (int)Math.Round(6 * scale));
        var gap = Math.Max(3, (int)Math.Round(5 * scale));
        var rowHeight = ClientSize.Height / 2;

        var networkWidth = Math.Clamp((int)Math.Round(ClientSize.Width * 0.43), ScalePx(54), ScalePx(76));
        var clockWidth = ClientSize.Width - outerPad * 2 - gap - networkWidth;
        if (clockWidth < ScalePx(58))
        {
            var shortage = ScalePx(58) - clockWidth;
            networkWidth = Math.Max(ScalePx(48), networkWidth - shortage);
            clockWidth = ClientSize.Width - outerPad * 2 - gap - networkWidth;
        }

        var networkRect = new Rectangle(outerPad, 0, networkWidth, ClientSize.Height);
        var clockRect = new Rectangle(networkRect.Right + gap, 0, Math.Max(1, clockWidth), ClientSize.Height);

        using var clockFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var rateFont = new Font("Segoe UI", 7.7f, FontStyle.Regular, GraphicsUnit.Point);

        var time = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
        var date = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);

        var topClock = new Rectangle(clockRect.Left, 0, clockRect.Width, rowHeight);
        var bottomClock = new Rectangle(clockRect.Left, rowHeight, clockRect.Width, ClientSize.Height - rowHeight);
        var topNetwork = new Rectangle(networkRect.Left, 0, networkRect.Width, rowHeight);
        var bottomNetwork = new Rectangle(networkRect.Left, rowHeight, networkRect.Width, ClientSize.Height - rowHeight);

        var baseFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, time, clockFont, topClock, ForeColor, baseFlags | TextFormatFlags.Right);
        TextRenderer.DrawText(g, date, clockFont, bottomClock, ForeColor, baseFlags | TextFormatFlags.Right);

        DrawRate(g, topNetwork, $"↓ {FormatRate(_rxPerSecond)}", rateFont, baseFlags);
        DrawRate(g, bottomNetwork, $"↑ {FormatRate(_txPerSecond)}", rateFont, baseFlags);
    }

    private void DrawRate(Graphics g, Rectangle bounds, string text, Font initialFont, TextFormatFlags flags)
    {
        var font = initialFont;
        Font? smaller = null;
        var measured = TextRenderer.MeasureText(g, text, font, Size.Empty, flags);
        if (measured.Width > bounds.Width)
        {
            var ratio = Math.Max(0.72f, (float)bounds.Width / Math.Max(1, measured.Width));
            smaller = new Font(font.FontFamily, Math.Max(6.2f, font.Size * ratio), font.Style, GraphicsUnit.Point);
            font = smaller;
        }

        TextRenderer.DrawText(g, text, font, bounds, ForeColor, flags | TextFormatFlags.Left);
        smaller?.Dispose();
    }

    private int ScalePx(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _zOrderTimer.Stop();
        _calendar?.Close();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _menu.Dispose();
        _toolTip.Dispose();
        base.OnFormClosed(e);
    }

    private void MainForm_MouseUp(object? sender, MouseEventArgs e)
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
            EnsureAboveTaskbar();
            return;
        }

        _calendar?.Dispose();
        _calendar = new CalendarPopup(IsSystemLightTheme());
        _calendar.FormClosed += (_, _) =>
        {
            _calendar = null;
            EnsureAboveTaskbar();
        };

        _calendar.ShowNear(Bounds, _taskbarRect);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() =>
            {
                _calendar?.Close();
                PositionOnTaskbar();
            }));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke((Action)(() =>
        {
            _calendar?.Close();
            ApplyTheme();
            PositionOnTaskbar();
            Invalidate();
        }));
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
                _adapterItem.Text = "Wi-Fi: sin conexión";
                UpdateTooltip();
                return;
            }

            if (_adapterId != current.Id)
            {
                _adapter = current;
                _adapterId = current.Id;
                var initial = current.GetIPv4Statistics();
                _lastReceived = initial.BytesReceived;
                _lastSent = initial.BytesSent;
                _sessionReceived = 0;
                _sessionSent = 0;
                _rxPerSecond = 0;
                _txPerSecond = 0;
                _lastSampleUtc = DateTime.UtcNow;
                _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
                UpdateTooltip();
                return;
            }

            _adapter = current;
            var stats = current.GetIPv4Statistics();
            var now = DateTime.UtcNow;
            var elapsed = Math.Max((now - _lastSampleUtc).TotalSeconds, 0.1);

            var rxDelta = Math.Max(0, stats.BytesReceived - _lastReceived);
            var txDelta = Math.Max(0, stats.BytesSent - _lastSent);

            _sessionReceived += rxDelta;
            _sessionSent += txDelta;
            _rxPerSecond = rxDelta / elapsed;
            _txPerSecond = txDelta / elapsed;

            _lastReceived = stats.BytesReceived;
            _lastSent = stats.BytesSent;
            _lastSampleUtc = now;

            _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
            UpdateTooltip();
        }
        catch
        {
            _rxPerSecond = 0;
            _txPerSecond = 0;
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
        _rxPerSecond = 0;
        _txPerSecond = 0;
        _lastSampleUtc = DateTime.UtcNow;
    }

    private static NetworkInterface? FindActiveWifiAdapter()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .ToArray();

        var wifi = adapters.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        if (wifi is not null)
            return wifi;

        return adapters.FirstOrDefault(n =>
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
        string text;
        if (_adapter is null)
        {
            text = "Wi-Fi sin conexión\nClic: calendario\nClic derecho: opciones";
        }
        else
        {
            text = $"{FriendlyAdapterName(_adapter)}\n" +
                   $"Descarga actual: {FormatRate(_rxPerSecond)}\n" +
                   $"Subida actual: {FormatRate(_txPerSecond)}\n" +
                   $"Recibido desde que se abrió: {FormatBytes(_sessionReceived)}\n" +
                   $"Enviado desde que se abrió: {FormatBytes(_sessionSent)}\n" +
                   "Clic: calendario\nClic derecho: opciones";
        }

        _toolTip.SetToolTip(this, text);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:0} B/s";

        var kb = bytesPerSecond / 1024d;
        if (kb < 1024)
            return kb < 10 ? $"{kb:0.0} KB/s" : $"{kb:0} KB/s";

        var mb = kb / 1024d;
        if (mb < 1024)
            return mb < 10 ? $"{mb:0.0} MB/s" : $"{mb:0} MB/s";

        var gb = mb / 1024d;
        return $"{gb:0.00} GB/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.0} KB";
        var mb = kb / 1024d;
        if (mb < 1024) return $"{mb:0.0} MB";
        var gb = mb / 1024d;
        return $"{gb:0.00} GB";
    }

    private void PositionOnTaskbar()
    {
        if (!IsHandleCreated)
            return;

        if (!TryGetTaskbarRect(out var taskbarRect))
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            var fallbackHeight = ScalePx(40);
            var fallbackWidth = ScalePx(144);
            _taskbarRect = Rectangle.FromLTRB(area.Left, area.Bottom, area.Right, area.Bottom + fallbackHeight);
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                area.Right - fallbackWidth - ScalePx(6),
                area.Bottom,
                fallbackWidth,
                fallbackHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            return;
        }

        _taskbarRect = taskbarRect;
        var horizontal = taskbarRect.Width >= taskbarRect.Height;
        if (!horizontal)
        {
            var verticalWidth = Math.Max(ScalePx(48), taskbarRect.Width - ScalePx(4));
            var verticalHeight = Math.Min(ScalePx(84), taskbarRect.Height - ScalePx(8));
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                taskbarRect.Left + (taskbarRect.Width - verticalWidth) / 2,
                taskbarRect.Bottom - verticalHeight - ScalePx(6),
                verticalWidth,
                verticalHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            return;
        }

        var showDesktopStrip = Math.Clamp(taskbarRect.Height / 8, ScalePx(4), ScalePx(9));
        var right = taskbarRect.Right - showDesktopStrip;

        var clockRect = TryGetClockRect();
        int width;
        if (clockRect.HasValue && clockRect.Value.Width > 0)
        {
            width = clockRect.Value.Width + Math.Clamp(taskbarRect.Height - ScalePx(4), ScalePx(34), ScalePx(54));
        }
        else
        {
            width = (int)Math.Round(taskbarRect.Height * 3.0);
        }

        width = Math.Clamp(width, ScalePx(126), ScalePx(168));
        var height = Math.Max(ScalePx(32), taskbarRect.Height - ScalePx(2));
        var x = Math.Max(taskbarRect.Left, right - width);
        var y = taskbarRect.Top + (taskbarRect.Height - height) / 2;

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static Rectangle? TryGetClockRect()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return null;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(taskbar, (hwnd, _) =>
        {
            var className = NativeMethods.GetClassName(hwnd);
            if (className.Equals("TrayClockWClass", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero || !NativeMethods.GetWindowRect(found, out var rect))
            return null;

        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private void EnsureAboveTaskbar()
    {
        if (!IsHandleCreated || IsDisposed)
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
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);
    }

    private static bool TryGetTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var nativeRect))
            return false;

        rect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
        return rect.Width > 0 && rect.Height > 0;
    }

    private void ApplyTheme()
    {
        var light = IsSystemLightTheme();
        BackColor = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
        ForeColor = light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
        _menu.RenderMode = ToolStripRenderMode.System;
    }

    internal static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("SystemUsesLightTheme");
            return value is int intValue && intValue != 0;
        }
        catch
        {
            return false;
        }
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
                    throw new InvalidOperationException("Executable path could not be resolved.");

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
            MessageBox.Show(
                "No se pudo cambiar el inicio automático.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    internal static class NativeMethods
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
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        internal static string GetClassName(IntPtr hWnd)
        {
            var builder = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
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
