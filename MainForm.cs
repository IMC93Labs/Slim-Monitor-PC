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

    private readonly System.Windows.Forms.Timer _timer;
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
        Padding = new Padding(8, 0, 8, 0);
        Cursor = Cursors.Default;

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
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "↓ —   ↑ —",
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.Transparent,
            ForeColor = ForeColor,
            UseMnemonic = false
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

        var refreshItem = new ToolStripMenuItem("Recolocar en la barra de tareas");
        refreshItem.Click += (_, _) => PositionOnTaskbar();

        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => Close();

        _menu.Items.Add(_adapterItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(refreshItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        ContextMenuStrip = _menu;
        _speedLabel.ContextMenuStrip = _menu;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateNetworkSpeed();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Shown += (_, _) =>
        {
            PositionOnTaskbar();
            ResetAdapter();
            UpdateNetworkSpeed();
            _timer.Start();
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
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _menu.Dispose();
        _toolTip.Dispose();
        base.OnFormClosed(e);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke((Action)PositionOnTaskbar);
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke((Action)(() =>
        {
            ApplyTheme();
            _speedLabel.ForeColor = ForeColor;
            PositionOnTaskbar();
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
                _speedLabel.Text = "Wi-Fi sin conexión";
                _adapterItem.Text = "Wi-Fi: sin conexión";
                _toolTip.SetToolTip(_speedLabel, "No se ha encontrado una interfaz Wi-Fi activa.");
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
                _speedLabel.Text = "↓ 0 KB/s   ↑ 0 KB/s";
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

            _speedLabel.Text = $"↓ {FormatRate(rxPerSecond)}   ↑ {FormatRate(txPerSecond)}";
            _adapterItem.Text = $"Wi-Fi: {FriendlyAdapterName(current)}";
            UpdateTooltip();
        }
        catch
        {
            _speedLabel.Text = "Wi-Fi —";
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

    private void PositionOnTaskbar()
    {
        if (!IsHandleCreated)
            return;

        const int preferredWidth = 180;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);

        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Bounds = new Rectangle(area.Right - preferredWidth - 12, area.Bottom - 34, preferredWidth, 30);
            return;
        }

        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        var horizontal = taskbarWidth >= taskbarHeight;

        int width;
        int height;
        int x;
        int y;

        if (horizontal)
        {
            height = Math.Clamp(taskbarHeight - 8, 24, 32);
            width = preferredWidth;

            var tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect))
                x = Math.Max(taskbarRect.Left + 8, trayRect.Left - width - 8);
            else
                x = Math.Max(taskbarRect.Left + 8, taskbarRect.Right - width - 250);

            y = taskbarRect.Top + (taskbarHeight - height) / 2;
        }
        else
        {
            width = Math.Clamp(taskbarWidth - 8, 40, preferredWidth);
            height = 30;
            x = taskbarRect.Left + (taskbarWidth - width) / 2;
            y = Math.Max(taskbarRect.Top + 8, taskbarRect.Bottom - height - 110);
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
