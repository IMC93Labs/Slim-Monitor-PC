using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace SlimMonitorPC;

internal sealed class TaskbarWidgetForm : Form
{
    private const string AppName = "Slim Monitor PC";

    private readonly NetworkMeter _networkMeter = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 1000 };
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _adapterItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _downloadItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _uploadItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _receivedItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _sentItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _startupItem = new("Iniciar con Windows") { CheckOnClick = true };

    private readonly Font _rateFont = new("Segoe UI", 5.9f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _timeFont = new("Segoe UI", 10.4f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _dateFont = new("Segoe UI", 8.2f, FontStyle.Regular, GraphicsUnit.Point);

    private NetworkSnapshot _snapshot = NetworkSnapshot.Disconnected;
    private Rectangle _taskbarBounds;
    private CalendarPopup? _calendar;
    private bool _hovered;
    private bool _syncingStartup;
    private bool _light;

    internal event EventHandler? WidgetShown;

    internal TaskbarWidgetForm()
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
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Opacity = 0;
        MinimumSize = new Size(120, 30);

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        ApplyTheme();
        BuildMenu();
        UpdateSnapshot();

        _uiTimer.Tick += (_, _) =>
        {
            UpdateSnapshot();
            Invalidate();
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _uiTimer.Start();
        WidgetShown?.Invoke(this, EventArgs.Empty);
    }

    internal void SetTaskbarBounds(Rectangle bounds)
    {
        _taskbarBounds = bounds;
    }

    internal void RevealAfterDock()
    {
        if (Opacity < 1)
            Opacity = 1;
    }

    internal void ConcealForFullscreen()
    {
        _calendar?.Close();
        _calendar = null;
    }

    private void BuildMenu()
    {
        _menu.RenderMode = ToolStripRenderMode.System;
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _adapterItem,
            _downloadItem,
            _uploadItem,
            _receivedItem,
            _sentItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Abrir calendario", null, (_, _) => ToggleCalendar()),
            _startupItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Salir", null, (_, _) => Close())
        });

        _menu.Opening += (_, _) =>
        {
            RefreshMenuInfo();
            _syncingStartup = true;
            _startupItem.Checked = StartupManager.IsEnabled();
            _syncingStartup = false;
        };

        _startupItem.CheckedChanged += (_, _) =>
        {
            if (_syncingStartup)
                return;

            try
            {
                StartupManager.SetEnabled(_startupItem.Checked);
            }
            catch (Exception ex)
            {
                _syncingStartup = true;
                _startupItem.Checked = StartupManager.IsEnabled();
                _syncingStartup = false;
                MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        ContextMenuStrip = _menu;
    }

    private void UpdateSnapshot()
    {
        _snapshot = _networkMeter.Sample();
        RefreshMenuInfo();
    }

    private void RefreshMenuInfo()
    {
        var adapter = _snapshot.Connected ? FriendlyAdapterName(_snapshot.AdapterName) : "sin conexión";
        _adapterItem.Text = $"Wi-Fi: {adapter}";
        _downloadItem.Text = $"Descarga actual: {DisplayRate(_snapshot.DownloadBytesPerSecond)}";
        _uploadItem.Text = $"Subida actual: {DisplayRate(_snapshot.UploadBytesPerSecond)}";
        _receivedItem.Text = $"Recibido desde que se abrió: {NetworkMeter.FormatBytes(_snapshot.SessionReceived)}";
        _sentItem.Text = $"Enviado desde que se abrió: {NetworkMeter.FormatBytes(_snapshot.SessionSent)}";
    }

    private static string FriendlyAdapterName(string name)
    {
        var clean = name.Trim();
        return clean.Length <= 30 ? clean : clean[..27] + "…";
    }

    private static string DisplayRate(double bytesPerSecond)
        => NetworkMeter.FormatRate(bytesPerSecond).Replace('|', ' ');

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in one buffered pass in OnPaint. Keeping the
        // background out of a separate erase pass prevents a visible intermediate frame.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var background = _light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(27, 27, 27);
        var foreground = _light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        if (_hovered && Width > 12 && Height > 12)
        {
            var hoverRect = Rectangle.Inflate(ClientRectangle, -3, -3);
            using var path = RoundedRect(hoverRect, 7);
            using var hoverBrush = new SolidBrush(_light ? Color.FromArgb(226, 226, 226) : Color.FromArgb(50, 50, 50));
            e.Graphics.FillPath(hoverBrush, path);
        }

        var now = DateTime.Now;
        var rateWidth = Math.Clamp((int)Math.Round(Width * 0.45), 68, 74);
        var clockLeft = rateWidth;
        var clockWidth = Math.Max(70, Width - rateWidth - 5);
        var halfHeight = Height / 2;

        DrawRate(e.Graphics, new Rectangle(4, 0, rateWidth - 4, halfHeight), "↓", _snapshot.Connected ? _snapshot.DownloadBytesPerSecond : null, foreground);
        DrawRate(e.Graphics, new Rectangle(4, halfHeight, rateWidth - 4, Height - halfHeight), "↑", _snapshot.Connected ? _snapshot.UploadBytesPerSecond : null, foreground);

        TextRenderer.DrawText(
            e.Graphics,
            now.ToString("HH:mm", CultureInfo.CurrentCulture),
            _timeFont,
            new Rectangle(clockLeft, 0, clockWidth, halfHeight),
            foreground,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(
            e.Graphics,
            now.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
            _dateFont,
            new Rectangle(clockLeft, halfHeight, clockWidth, Height - halfHeight),
            foreground,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void DrawRate(Graphics graphics, Rectangle rect, string arrow, double? bytesPerSecond, Color foreground)
    {
        var formatted = bytesPerSecond.HasValue ? NetworkMeter.FormatRate(bytesPerSecond.Value) : "—|";
        var parts = formatted.Split('|', 2);
        var value = parts[0];
        var unit = parts.Length > 1 ? parts[1] : string.Empty;

        const int arrowWidth = 9;
        var unitWidth = Math.Min(27, Math.Max(23, rect.Width / 3));
        var valueWidth = Math.Max(18, rect.Width - arrowWidth - unitWidth - 1);

        TextRenderer.DrawText(
            graphics,
            arrow,
            _rateFont,
            new Rectangle(rect.Left, rect.Top, arrowWidth, rect.Height),
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(
            graphics,
            value,
            _rateFont,
            new Rectangle(rect.Left + arrowWidth, rect.Top, valueWidth, rect.Height),
            foreground,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(
            graphics,
            unit,
            _rateFont,
            new Rectangle(rect.Left + arrowWidth + valueWidth + 1, rect.Top, unitWidth, rect.Height),
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_hovered)
        {
            _hovered = true;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered)
        {
            _hovered = false;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            ToggleCalendar();
    }

    private void ToggleCalendar()
    {
        if (_taskbarBounds.IsEmpty)
            return;

        if (_calendar is { IsDisposed: false, Visible: true })
        {
            _calendar.Close();
            _calendar = null;
            return;
        }

        _calendar?.Dispose();
        _calendar = new CalendarPopup(_light);
        _calendar.FormClosed += (_, _) => _calendar = null;
        _calendar.ShowNear(Bounds, _taskbarBounds, this);
    }

    private void ApplyTheme()
    {
        _light = IsSystemLightTheme();
        BackColor = _light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(27, 27, 27);
        ForeColor = _light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
    }

    internal static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _calendar?.Close();
        _calendar?.Dispose();
        _menu.Dispose();
        _rateFont.Dispose();
        _timeFont.Dispose();
        _dateFont.Dispose();
        base.OnFormClosed(e);
    }
}
