using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.10 consolidation layer. It intentionally replaces the stacked v0.2.8 +
/// v0.2.9 shell guards with one coordinator so Explorer is never being fought by
/// multiple timers at once. It also owns the stable rate cells, pixel-matched
/// taskbar background and hover state.
/// </summary>
internal sealed class TaskbarV029Refinement : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly FieldInfo? _overlayShownField;
    private readonly MethodInfo? _toggleCalendarMethod;
    private readonly WindowHook _hook;
    private readonly System.Windows.Forms.Timer _guardTimer = new() { Interval = 35 };
    private readonly System.Windows.Forms.Timer _colorTimer = new() { Interval = 1000 };

    private TableLayoutPanel? _layout;
    private Label? _sourceDownload;
    private Label? _sourceUpload;
    private RateCell? _downloadCell;
    private RateCell? _uploadCell;
    private bool _restoringVisibility;
    private bool _applyingSampledColor;
    private bool _hovered;
    private bool _cloakNotificationsRegistered;
    private bool _disposed;

    private TaskbarV029Refinement(TaskbarOverlayFormV027 form)
    {
        _form = form;
        _overlayShownField = typeof(TaskbarOverlayFormV027).GetField("_overlayShown", BindingFlags.Instance | BindingFlags.NonPublic);
        _toggleCalendarMethod = typeof(TaskbarOverlayFormV027).GetMethod("ToggleCalendar", BindingFlags.Instance | BindingFlags.NonPublic);
        _hook = new WindowHook(this);

        _form.HandleCreated += Form_HandleCreated;
        _form.HandleDestroyed += Form_HandleDestroyed;
        _form.Shown += Form_Shown;
        _form.VisibleChanged += Form_VisibleChanged;
        _form.Resize += Form_Resize;
        _form.SizeChanged += Form_SizeChanged;
        _form.BackColorChanged += Form_BackColorChanged;
        _form.Paint += Form_Paint;
        _form.FormClosed += (_, _) => Dispose();

        _guardTimer.Tick += (_, _) => GuardTick();
        _colorTimer.Tick += (_, _) => ApplyTaskbarPixelColor();

        if (_form.IsHandleCreated)
            InstallNativeProtections();
    }

    internal static TaskbarV029Refinement Attach(TaskbarOverlayFormV027 form) => new(form);

    private bool LogicalOverlayShown
    {
        get => _overlayShownField?.GetValue(_form) as bool? ?? _form.Visible;
        set => _overlayShownField?.SetValue(_form, value);
    }

    private void Form_HandleCreated(object? sender, EventArgs e) => InstallNativeProtections();

    private void Form_HandleDestroyed(object? sender, EventArgs e)
    {
        UnregisterCloakNotifications();
        _hook.Release();
    }

    private void Form_Shown(object? sender, EventArgs e)
    {
        InstallStableRateCells();
        ApplyTopVisualCrop();
        InstallNativeProtections();
        ApplyTaskbarPixelColor();
        UpdateHoverState();
        _guardTimer.Start();
        _colorTimer.Start();
    }

    private void Form_SizeChanged(object? sender, EventArgs e) => ApplyTopVisualCrop();

    private void Form_BackColorChanged(object? sender, EventArgs e)
    {
        if (_applyingSampledColor || !_form.IsHandleCreated || _form.IsDisposed)
            return;

        _form.BeginInvoke((Action)(() =>
        {
            if (!_disposed)
                ApplyTaskbarPixelColor();
        }));
    }

    private void Form_VisibleChanged(object? sender, EventArgs e)
    {
        if (!_form.Visible && ShouldOverlayBeVisible())
            RestoreImmediately("VisibleChanged:hidden");
    }

    private void Form_Resize(object? sender, EventArgs e)
    {
        if (_form.WindowState == FormWindowState.Minimized && ShouldOverlayBeVisible())
            RestoreImmediately("Resize:minimized");
    }

    private void GuardTick()
    {
        UpdateHoverState();
        RecoverIfShellTransitioned();
    }

    private void UpdateHoverState()
    {
        if (_disposed || _form.IsDisposed)
            return;

        var hovered = _form.Visible && _form.Bounds.Contains(Control.MousePosition);
        if (_hovered == hovered)
            return;

        _hovered = hovered;
        _form.Invalidate(true);
    }

    private void Form_Paint(object? sender, PaintEventArgs e)
    {
        if (!_hovered || _form.ClientSize.Width < 12 || _form.ClientSize.Height < 12)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = Rectangle.Inflate(_form.ClientRectangle, -3, -2);
        var light = _form.BackColor.GetBrightness() > 0.55f;
        var hoverColor = light ? Color.FromArgb(226, 226, 226) : Color.FromArgb(52, 52, 52);

        using var path = RoundedRect(rect, 7);
        using var brush = new SolidBrush(hoverColor);
        e.Graphics.FillPath(brush, path);
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

    private void InstallNativeProtections()
    {
        if (_disposed || !_form.IsHandleCreated)
            return;

        _hook.Assign(_form.Handle);
        NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, true);
        NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_DISALLOW_PEEK, true);
        NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_EXCLUDED_FROM_PEEK, true);
        NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_CLOAK, false);
        RegisterCloakNotifications();
    }

    private void RegisterCloakNotifications()
    {
        if (_cloakNotificationsRegistered || !_form.IsHandleCreated)
            return;

        try { _cloakNotificationsRegistered = NativeMethods.RegisterCloakedNotification(_form.Handle, true); }
        catch (EntryPointNotFoundException) { }
    }

    private void UnregisterCloakNotifications()
    {
        if (!_cloakNotificationsRegistered || !_form.IsHandleCreated)
            return;

        try { NativeMethods.RegisterCloakedNotification(_form.Handle, false); }
        catch (EntryPointNotFoundException) { }
        _cloakNotificationsRegistered = false;
    }

    private void InstallStableRateCells()
    {
        if (_layout is not null)
            return;

        _layout = FindDescendants(_form).OfType<TableLayoutPanel>().FirstOrDefault();
        if (_layout is null || _layout.ColumnStyles.Count < 2)
            return;

        _layout.Padding = new Padding(4, 0, 3, 0);
        _layout.ColumnStyles[0].SizeType = SizeType.Percent;
        _layout.ColumnStyles[0].Width = 41.5f;
        _layout.ColumnStyles[1].SizeType = SizeType.Percent;
        _layout.ColumnStyles[1].Width = 58.5f;

        _sourceDownload = _layout.GetControlFromPosition(0, 0) as Label;
        _sourceUpload = _layout.GetControlFromPosition(0, 1) as Label;
        var time = _layout.GetControlFromPosition(1, 0) as Label;
        var date = _layout.GetControlFromPosition(1, 1) as Label;
        SetFont(time, 10.4f);
        SetFont(date, 8.45f);

        if (_sourceDownload is not null)
        {
            _sourceDownload.TextChanged += SourceDownload_TextChanged;
            _layout.Controls.Remove(_sourceDownload);
            _sourceDownload.Visible = false;
        }

        if (_sourceUpload is not null)
        {
            _sourceUpload.TextChanged += SourceUpload_TextChanged;
            _layout.Controls.Remove(_sourceUpload);
            _sourceUpload.Visible = false;
        }

        _downloadCell = new RateCell("↓") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _uploadCell = new RateCell("↑") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        ConfigureRateCell(_downloadCell);
        ConfigureRateCell(_uploadCell);
        _layout.Controls.Add(_downloadCell, 0, 0);
        _layout.Controls.Add(_uploadCell, 0, 1);
        _downloadCell.SetText(_sourceDownload?.Text ?? "↓ 0 B/s");
        _uploadCell.SetText(_sourceUpload?.Text ?? "↑ 0 B/s");
    }

    private void ConfigureRateCell(RateCell cell)
    {
        cell.ContextMenuStrip = _form.ContextMenuStrip;
        cell.Cursor = Cursors.Hand;
        cell.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _toggleCalendarMethod?.Invoke(_form, null);
        };
    }

    private static void SetFont(Label? label, float size)
    {
        if (label is null) return;
        var old = label.Font;
        label.Font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        old.Dispose();
    }

    private void SourceDownload_TextChanged(object? sender, EventArgs e) => _downloadCell?.SetText(_sourceDownload?.Text ?? "↓ —");
    private void SourceUpload_TextChanged(object? sender, EventArgs e) => _uploadCell?.SetText(_sourceUpload?.Text ?? "↑ —");

    private void ApplyTopVisualCrop()
    {
        if (_form.ClientSize.Width <= 0 || _form.ClientSize.Height <= 4) return;
        const int topCrop = 2;
        var replacement = new Region(new Rectangle(0, topCrop, _form.ClientSize.Width, _form.ClientSize.Height - topCrop));
        var old = _form.Region;
        _form.Region = replacement;
        old?.Dispose();
    }

    private void ApplyTaskbarPixelColor()
    {
        if (_disposed || !_form.IsHandleCreated || !TryGetVisibleTaskbarRect(out var taskbarRect)) return;
        if (!TrySampleTaskbarColor(taskbarRect, out var sampled)) return;
        if (Math.Abs(_form.BackColor.R - sampled.R) <= 1 && Math.Abs(_form.BackColor.G - sampled.G) <= 1 && Math.Abs(_form.BackColor.B - sampled.B) <= 1) return;

        _applyingSampledColor = true;
        try
        {
            _form.BackColor = sampled;
            if (_layout is not null) _layout.BackColor = Color.Transparent;
            if (_downloadCell is not null) _downloadCell.BackColor = Color.Transparent;
            if (_uploadCell is not null) _uploadCell.BackColor = Color.Transparent;
            _form.Invalidate(true);
        }
        finally { _applyingSampledColor = false; }
    }

    private bool TrySampleTaskbarColor(Rectangle taskbar, out Color color)
    {
        color = Color.Empty;
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            var samples = new List<Color>(160);
            if (taskbar.Width >= taskbar.Height)
            {
                var step = Math.Max(14, taskbar.Width / 80);
                var ys = new[] { taskbar.Top + Math.Min(5, Math.Max(2, taskbar.Height / 8)), taskbar.Bottom - Math.Min(5, Math.Max(2, taskbar.Height / 8)) - 1 };
                for (var x = taskbar.Left + 10; x < taskbar.Right - 12; x += step)
                {
                    if (x >= _form.Left - 12 && x <= _form.Right + 12) continue;
                    foreach (var y in ys) AddPixelSample(dc, x, y, samples);
                }
            }
            else
            {
                var step = Math.Max(14, taskbar.Height / 60);
                var xs = new[] { taskbar.Left + 4, taskbar.Right - 5 };
                for (var y = taskbar.Top + 10; y < taskbar.Bottom - 10; y += step)
                    foreach (var x in xs) AddPixelSample(dc, x, y, samples);
            }
            if (samples.Count < 6) return false;
            var rs = samples.Select(c => (int)c.R).OrderBy(v => v).ToArray();
            var gs = samples.Select(c => (int)c.G).OrderBy(v => v).ToArray();
            var bs = samples.Select(c => (int)c.B).OrderBy(v => v).ToArray();
            var mid = samples.Count / 2;
            color = Color.FromArgb(rs[mid], gs[mid], bs[mid]);
            return true;
        }
        finally { NativeMethods.ReleaseDC(IntPtr.Zero, dc); }
    }

    private static void AddPixelSample(IntPtr dc, int x, int y, List<Color> samples)
    {
        var pixel = NativeMethods.GetPixel(dc, x, y);
        if (pixel == 0xFFFFFFFF) return;
        samples.Add(Color.FromArgb((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF)));
    }

    private void RecoverIfShellTransitioned()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated || !ShouldOverlayBeVisible()) return;
        LogicalOverlayShown = true;

        if (TryGetCloakedState(out var cloaked) && cloaked != 0)
        {
            NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_CLOAK, false);
            RestoreImmediately($"DWM cloak={cloaked}");
            return;
        }

        if (!_form.Visible || NativeMethods.IsIconic(_form.Handle) || !NativeMethods.IsWindowVisible(_form.Handle))
        {
            RestoreImmediately("hidden/minimized");
            return;
        }

        if (!IsOverlayFrontmost())
            NativeMethods.SetWindowPos(_form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private bool TryGetCloakedState(out int cloaked)
    {
        cloaked = 0;
        try { return NativeMethods.DwmGetWindowAttribute(_form.Handle, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0; }
        catch { return false; }
    }

    private void RestoreImmediately(string reason)
    {
        if (_restoringVisibility || _disposed || _form.IsDisposed || !_form.IsHandleCreated) return;
        _restoringVisibility = true;
        try
        {
            LogicalOverlayShown = true;
            NativeMethods.TrySetDwmBool(_form.Handle, NativeMethods.DWMWA_CLOAK, false);
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            NativeMethods.ShowWindow(_form.Handle, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.SetWindowPos(_form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            LogShellRecovery(reason);
        }
        finally { _restoringVisibility = false; }
    }

    private void LogShellRecovery(string reason)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMC93Labs", "SlimMonitorPC");
            Directory.CreateDirectory(folder);
            var foreground = NativeMethods.GetForegroundWindow();
            var cls = foreground == IntPtr.Zero ? "none" : NativeMethods.GetWindowClassName(foreground);
            TryGetCloakedState(out var cloaked);
            File.AppendAllText(Path.Combine(folder, "shell-state.log"), $"{DateTime.Now:O} recovery={reason}; visible={_form.Visible}; nativeVisible={NativeMethods.IsWindowVisible(_form.Handle)}; iconic={NativeMethods.IsIconic(_form.Handle)}; cloaked={cloaked}; foreground={cls}{Environment.NewLine}");
        }
        catch { }
    }

    private bool ShouldOverlayBeVisible()
    {
        if (!TryGetVisibleTaskbarRect(out var taskbarRect)) return false;
        return !IsRealFullscreenCoveringTaskbar(taskbarRect);
    }

    private bool IsOverlayFrontmost()
    {
        if (_form.Width <= 0 || _form.Height <= 0) return false;
        var point = new NativeMethods.POINT(_form.Left + Math.Max(1, _form.Width / 2), _form.Top + Math.Max(3, _form.Height / 2));
        var hit = NativeMethods.WindowFromPoint(point);
        return hit != IntPtr.Zero && NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) == _form.Handle;
    }

    private static bool TryGetVisibleTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar) || !NativeMethods.GetWindowRect(taskbar, out var native)) return false;
        var shell = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (shell.Width <= 0 || shell.Height <= 0) return false;
        var screen = Screen.FromRectangle(shell);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        if (work.Bottom < bounds.Bottom) rect = Rectangle.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        else if (work.Top > bounds.Top) rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        else if (work.Right < bounds.Right) rect = Rectangle.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        else if (work.Left > bounds.Left) rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        else rect = shell;
        var visible = Rectangle.Intersect(rect, screen.Bounds);
        var visibleArea = (long)Math.Max(0, visible.Width) * Math.Max(0, visible.Height);
        var totalArea = (long)Math.Max(0, rect.Width) * Math.Max(0, rect.Height);
        return totalArea > 0 && visibleArea * 100 >= totalArea * 40;
    }

    private static bool IsRealFullscreenCoveringTaskbar(Rectangle taskbarRect)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground)) return false;
        var cls = NativeMethods.GetWindowClassName(foreground);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(taskbar, out var shellPid);
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (shellPid != 0 && shellPid == foregroundPid) return false;
        }
        if (!TryGetWindowBounds(foreground, out var windowBounds)) return false;
        var screenBounds = Screen.FromRectangle(taskbarRect).Bounds;
        var screenArea = (long)screenBounds.Width * screenBounds.Height;
        var screenIntersection = Rectangle.Intersect(windowBounds, screenBounds);
        var coveredScreen = (long)Math.Max(0, screenIntersection.Width) * Math.Max(0, screenIntersection.Height);
        var taskbarArea = (long)taskbarRect.Width * taskbarRect.Height;
        var taskbarIntersection = Rectangle.Intersect(windowBounds, taskbarRect);
        var coveredTaskbar = (long)Math.Max(0, taskbarIntersection.Width) * Math.Max(0, taskbarIntersection.Height);
        return screenArea > 0 && taskbarArea > 0 && coveredScreen * 100 >= screenArea * 97 && coveredTaskbar * 100 >= taskbarArea * 95;
    }

    private static bool TryGetWindowBounds(IntPtr window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var extended = new NativeMethods.RECT();
        var hr = NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out extended, Marshal.SizeOf<NativeMethods.RECT>());
        if (hr == 0 && extended.Right > extended.Left && extended.Bottom > extended.Top)
        {
            bounds = Rectangle.FromLTRB(extended.Left, extended.Top, extended.Right, extended.Bottom);
            return true;
        }
        if (!NativeMethods.GetWindowRect(window, out var native)) return false;
        bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static IEnumerable<Control> FindDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in FindDescendants(child)) yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _guardTimer.Stop();
        _colorTimer.Stop();
        _guardTimer.Dispose();
        _colorTimer.Dispose();
        UnregisterCloakNotifications();
        _hook.Release();
        if (_sourceDownload is not null) _sourceDownload.TextChanged -= SourceDownload_TextChanged;
        if (_sourceUpload is not null) _sourceUpload.TextChanged -= SourceUpload_TextChanged;
        _form.HandleCreated -= Form_HandleCreated;
        _form.HandleDestroyed -= Form_HandleDestroyed;
        _form.Shown -= Form_Shown;
        _form.VisibleChanged -= Form_VisibleChanged;
        _form.Resize -= Form_Resize;
        _form.SizeChanged -= Form_SizeChanged;
        _form.BackColorChanged -= Form_BackColorChanged;
        _form.Paint -= Form_Paint;
    }

    private sealed class WindowHook : NativeWindow
    {
        private readonly TaskbarV029Refinement _owner;
        internal WindowHook(TaskbarV029Refinement owner) => _owner = owner;
        internal void Assign(IntPtr handle)
        {
            if (Handle == handle) return;
            if (Handle != IntPtr.Zero) ReleaseHandle();
            AssignHandle(handle);
        }
        internal void Release() { if (Handle != IntPtr.Zero) ReleaseHandle(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE && _owner.ShouldOverlayBeVisible())
            {
                _owner.LogicalOverlayShown = true;
                return;
            }

            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero && _owner.ShouldOverlayBeVisible())
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
                if ((pos.flags & NativeMethods.SWP_HIDEWINDOW) != 0) pos.flags &= ~NativeMethods.SWP_HIDEWINDOW;
                pos.flags &= ~NativeMethods.SWP_NOZORDER;
                pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                _owner.LogicalOverlayShown = true;
                Marshal.StructureToPtr(pos, m.LParam, false);
            }

            if (m.Msg == NativeMethods.WM_CLOAKED_STATE_CHANGED && m.WParam.ToInt64() != 0 && _owner.ShouldOverlayBeVisible())
            {
                var cloakState = m.WParam.ToInt64();
                _owner.LogicalOverlayShown = true;
                NativeMethods.TrySetDwmBool(_owner._form.Handle, NativeMethods.DWMWA_CLOAK, false);
                _owner._form.BeginInvoke((Action)(() => _owner.RestoreImmediately($"WM_CLOAKED_STATE_CHANGED:{cloakState}")));
                return;
            }

            base.WndProc(ref m);
            if (m.Msg == NativeMethods.WM_SIZE && m.WParam.ToInt64() == NativeMethods.SIZE_MINIMIZED && _owner.ShouldOverlayBeVisible())
                _owner.RestoreImmediately("WM_SIZE:minimized");
        }
    }

    private sealed class RateCell : Control
    {
        private readonly string _arrow;
        private string _value = "0";
        private string _unit = "B/s";
        private readonly Font _font = new("Segoe UI", 6.15f, FontStyle.Regular, GraphicsUnit.Point);
        internal RateCell(string arrow)
        {
            _arrow = arrow;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        internal void SetText(string text)
        {
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) { _value = parts[1]; _unit = parts[2]; }
            else if (parts.Length >= 2) { _value = parts[1]; _unit = string.Empty; }
            else { _value = "—"; _unit = string.Empty; }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var unitWidth = Math.Min(27, Math.Max(21, Width / 3));
            const int arrowWidth = 9;
            const int gap = 1;
            var valueWidth = Math.Max(12, Width - arrowWidth - gap - unitWidth);
            TextRenderer.DrawText(e.Graphics, _arrow, _font, new Rectangle(0, 0, arrowWidth, Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, _value, _font, new Rectangle(arrowWidth, 0, valueWidth, Height), ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, _unit, _font, new Rectangle(arrowWidth + valueWidth + gap, 0, unitWidth, Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing) { if (disposing) _font.Dispose(); base.Dispose(disposing); }
    }

    private static class NativeMethods
    {
        internal const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal const int DWMWA_DISALLOW_PEEK = 11;
        internal const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        internal const int DWMWA_CLOAK = 13;
        internal const int DWMWA_CLOAKED = 14;
        internal const int WM_SIZE = 0x0005;
        internal const int WM_WINDOWPOSCHANGING = 0x0046;
        internal const int WM_SYSCOMMAND = 0x0112;
        internal const int WM_CLOAKED_STATE_CHANGED = 0x0347;
        internal const int SC_MINIMIZE = 0xF020;
        internal const int SIZE_MINIMIZED = 1;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal const uint GA_ROOT = 2;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const uint SWP_HIDEWINDOW = 0x0080;
        internal static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);
        [DllImport("user32.dll", EntryPoint = "RegisterCloakedNotification", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterCloakedNotification(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool register);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr hDC, int x, int y);

        internal static string GetWindowClassName(IntPtr hWnd)
        {
            var text = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }
        internal static bool TrySetDwmBool(IntPtr hWnd, int attribute, bool enabled)
        {
            try { var value = enabled ? 1 : 0; return DwmSetWindowAttribute(hWnd, attribute, ref value, sizeof(int)) == 0; }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }
        [StructLayout(LayoutKind.Sequential)] internal readonly struct POINT { public readonly int X; public readonly int Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    }
}
