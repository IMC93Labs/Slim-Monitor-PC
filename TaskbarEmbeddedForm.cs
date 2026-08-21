using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace SlimMonitorPC;

public sealed class TaskbarEmbeddedForm : Form
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
    private readonly System.Windows.Forms.Timer _shellTimer = new() { Interval = 1000 };

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
    private IntPtr _taskbarHandle = IntPtr.Zero;
    private Rectangle _taskbarScreenRect;
    private Size _lastTaskbarClientSize;

    public TaskbarEmbeddedForm()
    {
        Text = AppName;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = Padding.Empty;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

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
        realignItem.Click += (_, _) => MaintainTaskbarAttachment(forcePosition: true);

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
        _shellTimer.Tick += (_, _) => MaintainTaskbarAttachment();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            ResetAdapter();
            UpdateNetworkSpeed();
            UpdateClock();
            MaintainTaskbarAttachment(forcePosition: true);
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

    private static ToolStripMenuItem CreateInfoItem(string text) => new(text) { Enabled = false };

    private void ConfigureLayout()
    {
        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = new Padding(8, 0, 4, 0);
        _layout.ColumnCount = 2;
        _layout.RowCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureLabel(_download, ContentAlignment.MiddleLeft, 7.3f);
        ConfigureLabel(_upload, ContentAlignment.MiddleLeft, 7.3f);
        ConfigureLabel(_time, ContentAlignment.MiddleRight, 10.4f);
        ConfigureLabel(_date, ContentAlignment.MiddleRight, 9.2f);

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
        if (_calendar is { IsDisposed: false, Visible: true })
        {
            _calendar.Close();
            _calendar = null;
            return;
        }

        if (!TryGetScreenBounds(out var anchor) || _taskbarScreenRect.IsEmpty)
            return;

        _calendar?.Dispose();
        _calendar = new CalendarPopup(IsSystemLightTheme());
        _calendar.FormClosed += (_, _) => _calendar = null;
        _calendar.ShowNear(anchor, _taskbarScreenRect);
    }

    private bool TryGetScreenBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsHandleCreated || !NativeMethods.GetWindowRect(Handle, out var native))
            return false;
        bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
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

    private void MaintainTaskbarAttachment(bool forcePosition = false)
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            _taskbarHandle = IntPtr.Zero;
            return;
        }

        if (!NativeMethods.GetWindowRect(taskbar, out var screenRectNative) ||
            !NativeMethods.GetClientRect(taskbar, out var clientRectNative))
            return;

        _taskbarScreenRect = Rectangle.FromLTRB(
            screenRectNative.Left,
            screenRectNative.Top,
            screenRectNative.Right,
            screenRectNative.Bottom);

        var clientSize = new Size(
            Math.Max(1, clientRectNative.Right - clientRectNative.Left),
            Math.Max(1, clientRectNative.Bottom - clientRectNative.Top));

        var parentChanged = _taskbarHandle != taskbar || NativeMethods.GetParent(Handle) != taskbar;
        if (parentChanged)
        {
            AttachToTaskbar(taskbar);
            _taskbarHandle = taskbar;
            forcePosition = true;
        }

        if (forcePosition || clientSize != _lastTaskbarClientSize)
        {
            PositionInsideTaskbar(clientSize);
            _lastTaskbarClientSize = clientSize;
        }

        if (_calendar is { IsDisposed: false } && !IsTaskbarActuallyVisible(_taskbarScreenRect))
        {
            _calendar.Close();
            _calendar = null;
        }
    }

    private void AttachToTaskbar(IntPtr taskbar)
    {
        var style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
        style |= NativeMethods.WS_CHILD;
        style &= ~NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
        NativeMethods.SetParent(Handle, taskbar);

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    private void PositionInsideTaskbar(Size taskbarClientSize)
    {
        var horizontal = taskbarClientSize.Width >= taskbarClientSize.Height;
        if (!horizontal)
        {
            var verticalWidth = Math.Max(42, taskbarClientSize.Width - 4);
            var verticalHeight = Math.Min(84, taskbarClientSize.Height - 8);
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOP,
                Math.Max(0, (taskbarClientSize.Width - verticalWidth) / 2),
                Math.Max(0, taskbarClientSize.Height - verticalHeight - 4),
                verticalWidth,
                verticalHeight,
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);
            return;
        }

        var taskbarHeight = taskbarClientSize.Height;
        var showDesktopStrip = Math.Clamp(taskbarHeight / 8, 5, 9);

        // Deliberately narrower than v0.2.3. This creates a clear visual gap
        // from the battery/volume/Wi-Fi icons while preserving a full dd/MM/yyyy date.
        var networkWidth = Math.Clamp((int)Math.Round(taskbarHeight * 1.25), 58, 68);
        var clockWidth = Math.Clamp((int)Math.Round(taskbarHeight * 1.75), 80, 90);
        var width = Math.Clamp(networkWidth + clockWidth, 140, 156);

        var insetY = Math.Clamp(taskbarHeight / 24, 1, 2);
        var height = Math.Max(30, taskbarHeight - insetY * 2);
        var x = Math.Max(0, taskbarClientSize.Width - showDesktopStrip - width);
        var y = insetY;

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOP,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);
    }

    private static bool IsTaskbarActuallyVisible(Rectangle taskbarRect)
    {
        if (taskbarRect.IsEmpty)
            return false;

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
                MaintainTaskbarAttachment(forcePosition: true);
            }));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() =>
            {
                _calendar?.Close();
                ApplyTheme();
                MaintainTaskbarAttachment(forcePosition: true);
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
        internal const int GWL_STYLE = -16;
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_POPUP = unchecked((int)0x80000000);

        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

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
