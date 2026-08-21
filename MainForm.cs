using System.Diagnostics;
using System.Drawing;
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
    private const string SettingsKeyPath = @"Software\IMC93Labs\SlimMonitorPC";
    private const string PositionValueName = "TaskbarOffset";
    private const int PreferredWidth = 96;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _zOrderTimer;
    private readonly Label _speedLabel;
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

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

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
        Padding = new Padding(3, 0, 2, 0);
        Cursor = Cursors.SizeAll;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // The executable already contains the application icon; this is only cosmetic.
        }

        ApplyTheme();

        _speedLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "↓ —\n↑ —",
            Font = new Font("Segoe UI", 8.0f, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.Transparent,
            ForeColor = ForeColor,
            UseMnemonic = false,
            Cursor = Cursors.SizeAll
        };
        Controls.Add(_speedLabel);

        _toolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 250,
            ReshowDelay = 100
        };

        _menu = new ContextMenuStrip();
        _adapterItem = new ToolStripMenuItem("Wi-Fi: buscando…") { Enabled = false };
        _startupItem = new ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        _startupItem.CheckedChanged += StartupItem_CheckedChanged;

        var resetPositionItem = new ToolStripMenuItem("Restablecer posición");
        resetPositionItem.Click += (_, _) =>
        {
            ClearSavedTaskbarOffset();
            PositionOnTaskbar(useSavedPosition: false);
        };

        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => Close();

        _menu.Items.Add(_adapterItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(resetPositionItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        ContextMenuStrip = _menu;
        _speedLabel.ContextMenuStrip = _menu;

        MouseDown += Drag_MouseDown;
        MouseMove += Drag_MouseMove;
        MouseUp += Drag_MouseUp;
        _speedLabel.MouseDown += Drag_MouseDown;
        _speedLabel.MouseMove += Drag_MouseMove;
        _speedLabel.MouseUp += Drag_MouseUp;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateNetworkSpeed();

        // Explorer/taskbar can move itself above other top-most windows after a click.
        // Reasserting only the Z-order keeps the meter visible without moving it.
        _zOrderTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _zOrderTimer.Tick += (_, _) => EnsureAboveTaskbar();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            PositionOnTaskbar(useSavedPosition: true);
            ResetAdapter();
            UpdateNetworkSpeed();
            EnsureAboveTaskbar();
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _zOrderTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _menu.Dispose();
        _toolTip.Dispose();
        base.OnFormClosed(e);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)(() => PositionOnTaskbar(useSavedPosition: true)));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke((Action)(() =>
        {
            ApplyTheme();
            _speedLabel.ForeColor = ForeColor;
            PositionOnTaskbar(useSavedPosition: true);
        }));
    }

    private void Drag_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _dragging = true;
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
        Capture = true;
        _speedLabel.Capture = true;
        EnsureAboveTaskbar();
    }

    private void Drag_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left)
            return;

        var cursor = Cursor.Position;
        var proposed = new Point(
            _dragStartLocation.X + cursor.X - _dragStartCursor.X,
            _dragStartLocation.Y + cursor.Y - _dragStartCursor.Y);

        MoveWithinTaskbar(proposed);
        EnsureAboveTaskbar();
    }

    private void Drag_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            _speedLabel.Capture = false;
            SaveTaskbarOffset();
        }

        EnsureAboveTaskbar();
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
                _speedLabel.Text = "↓ —\n↑ —";
                _adapterItem.Text = "Wi-Fi: sin conexión";
                _toolTip.SetToolTip(_speedLabel, "Wi-Fi sin conexión\nArrastra con el botón izquierdo para mover\nClic derecho: opciones");
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
                _lastSampleUtc = DateTime.UtcNow;
                _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
                _speedLabel.Text = "↓ 0 KB/s\n↑ 0 KB/s";
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

            var rxPerSecond = rxDelta / elapsed;
            var txPerSecond = txDelta / elapsed;

            _lastReceived = stats.BytesReceived;
            _lastSent = stats.BytesSent;
            _lastSampleUtc = now;

            _speedLabel.Text = $"↓ {FormatRate(rxPerSecond)}\n↑ {FormatRate(txPerSecond)}";
            _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
            UpdateTooltip();
        }
        catch
        {
            _speedLabel.Text = "↓ —\n↑ —";
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

        var wifi = adapters.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        if (wifi is not null)
            return wifi;

        // Fallback for drivers that do not report Wireless80211 correctly.
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
        if (_adapter is null)
            return;

        var text = $"{FriendlyAdapterName(_adapter)}\n" +
                   $"Recibido desde que se abrió: {FormatBytes(_sessionReceived)}\n" +
                   $"Enviado desde que se abrió: {FormatBytes(_sessionSent)}\n" +
                   "Arrastra con el botón izquierdo para mover\n" +
                   "Clic derecho: opciones";
        _toolTip.SetToolTip(_speedLabel, text);
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

    private void PositionOnTaskbar(bool useSavedPosition)
    {
        if (!IsHandleCreated)
            return;

        if (!TryGetTaskbarRect(out var taskbarRect))
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                area.Right - PreferredWidth - 12,
                area.Bottom - 32,
                PreferredWidth,
                30,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            return;
        }

        var horizontal = taskbarRect.Width >= taskbarRect.Height;
        int width;
        int height;
        int x;
        int y;

        if (horizontal)
        {
            width = PreferredWidth;
            height = Math.Clamp(taskbarRect.Height - 8, 28, 34);
            y = taskbarRect.Top + (taskbarRect.Height - height) / 2;

            var savedOffset = useSavedPosition ? LoadSavedTaskbarOffset() : null;
            if (savedOffset.HasValue)
            {
                x = Math.Clamp(taskbarRect.Left + savedOffset.Value, taskbarRect.Left, taskbarRect.Right - width);
            }
            else
            {
                var tray = NativeMethods.FindWindowEx(NativeMethods.FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "TrayNotifyWnd", null);
                if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect))
                    x = Math.Max(taskbarRect.Left + 4, trayRect.Left - width - 4);
                else
                    x = Math.Max(taskbarRect.Left + 4, taskbarRect.Right - width - 250);
            }
        }
        else
        {
            width = Math.Clamp(taskbarRect.Width - 6, 46, PreferredWidth);
            height = 32;
            x = taskbarRect.Left + (taskbarRect.Width - width) / 2;

            var savedOffset = useSavedPosition ? LoadSavedTaskbarOffset() : null;
            y = savedOffset.HasValue
                ? Math.Clamp(taskbarRect.Top + savedOffset.Value, taskbarRect.Top, taskbarRect.Bottom - height)
                : Math.Max(taskbarRect.Top + 4, taskbarRect.Bottom - height - 110);
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void MoveWithinTaskbar(Point proposedLocation)
    {
        if (!TryGetTaskbarRect(out var taskbarRect))
        {
            Location = proposedLocation;
            return;
        }

        var horizontal = taskbarRect.Width >= taskbarRect.Height;
        int x;
        int y;

        if (horizontal)
        {
            x = Math.Clamp(proposedLocation.X, taskbarRect.Left, taskbarRect.Right - Width);
            y = taskbarRect.Top + (taskbarRect.Height - Height) / 2;
        }
        else
        {
            x = taskbarRect.Left + (taskbarRect.Width - Width) / 2;
            y = Math.Clamp(proposedLocation.Y, taskbarRect.Top, taskbarRect.Bottom - Height);
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            Width,
            Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
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

    private void SaveTaskbarOffset()
    {
        try
        {
            if (!TryGetTaskbarRect(out var taskbarRect))
                return;

            var horizontal = taskbarRect.Width >= taskbarRect.Height;
            var offset = horizontal ? Left - taskbarRect.Left : Top - taskbarRect.Top;
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(PositionValueName, offset, RegistryValueKind.DWord);
        }
        catch
        {
            // Position persistence is optional; dragging still works if the registry is unavailable.
        }
    }

    private static int? LoadSavedTaskbarOffset()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
            return key?.GetValue(PositionValueName) is int offset ? offset : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearSavedTaskbarOffset()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.DeleteValue(PositionValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Optional setting; no action required.
        }
    }

    private void ApplyTheme()
    {
        var light = IsSystemLightTheme();
        BackColor = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
        ForeColor = light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
    }

    private static bool IsSystemLightTheme()
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

    private static class NativeMethods
    {
        internal static readonly IntPtr HWND_TOPMOST = new(-1);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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
