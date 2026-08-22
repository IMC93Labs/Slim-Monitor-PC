using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

internal sealed class TaskbarV029Refinement : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly FieldInfo? _overlayShownField;
    private readonly MethodInfo? _toggleCalendarMethod;
    private readonly System.Windows.Forms.Timer _guardTimer = new() { Interval = 12 };
    private readonly System.Windows.Forms.Timer _colorTimer = new() { Interval = 750 };

    private TableLayoutPanel? _layout;
    private Label? _sourceDownload;
    private Label? _sourceUpload;
    private RateCell? _downloadCell;
    private RateCell? _uploadCell;
    private bool _restoringVisibility;
    private bool _applyingSampledColor;
    private bool _disposed;

    private TaskbarV029Refinement(TaskbarOverlayFormV027 form)
    {
        _form = form;
        _overlayShownField = typeof(TaskbarOverlayFormV027).GetField("_overlayShown", BindingFlags.Instance | BindingFlags.NonPublic);
        _toggleCalendarMethod = typeof(TaskbarOverlayFormV027).GetMethod("ToggleCalendar", BindingFlags.Instance | BindingFlags.NonPublic);

        _form.Shown += Form_Shown;
        _form.VisibleChanged += Form_VisibleChanged;
        _form.Resize += Form_Resize;
        _form.SizeChanged += Form_SizeChanged;
        _form.BackColorChanged += Form_BackColorChanged;
        _form.FormClosed += (_, _) => Dispose();

        _guardTimer.Tick += (_, _) => RecoverIfShellTransitioned();
        _colorTimer.Tick += (_, _) => ApplyTaskbarPixelColor();
    }

    internal static TaskbarV029Refinement Attach(TaskbarOverlayFormV027 form) => new(form);

    private bool LogicalOverlayShown
    {
        get => _overlayShownField?.GetValue(_form) as bool? ?? _form.Visible;
        set => _overlayShownField?.SetValue(_form, value);
    }

    private void Form_Shown(object? sender, EventArgs e)
    {
        InstallStableRateCells();
        ApplyTopVisualCrop();
        ApplyTaskbarPixelColor();
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
            RestoreImmediately();
    }

    private void Form_Resize(object? sender, EventArgs e)
    {
        if (_form.WindowState == FormWindowState.Minimized && ShouldOverlayBeVisible())
            RestoreImmediately();
    }

    private void InstallStableRateCells()
    {
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
        if (label is null)
            return;
        var old = label.Font;
        label.Font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        old.Dispose();
    }

    private void SourceDownload_TextChanged(object? sender, EventArgs e)
        => _downloadCell?.SetText(_sourceDownload?.Text ?? "↓ —");

    private void SourceUpload_TextChanged(object? sender, EventArgs e)
        => _uploadCell?.SetText(_sourceUpload?.Text ?? "↑ —");

    private void ApplyTopVisualCrop()
    {
        if (_form.ClientSize.Width <= 0 || _form.ClientSize.Height <= 4)
            return;

        const int topCrop = 2;
        var replacement = new Region(new Rectangle(0, topCrop, _form.ClientSize.Width, _form.ClientSize.Height - topCrop));
        var old = _form.Region;
        _form.Region = replacement;
        old?.Dispose();
    }

    private void ApplyTaskbarPixelColor()
    {
        if (_disposed || !_form.IsHandleCreated || !TryGetVisibleTaskbarRect(out var taskbarRect))
            return;

        if (!TrySampleTaskbarColor(taskbarRect, out var sampled))
            return;

        if (Math.Abs(_form.BackColor.R - sampled.R) <= 1 &&
            Math.Abs(_form.BackColor.G - sampled.G) <= 1 &&
            Math.Abs(_form.BackColor.B - sampled.B) <= 1)
            return;

        _applyingSampledColor = true;
        try
        {
            _form.BackColor = sampled;
            if (_layout is not null)
                _layout.BackColor = Color.Transparent;
            if (_downloadCell is not null)
                _downloadCell.BackColor = Color.Transparent;
            if (_uploadCell is not null)
                _uploadCell.BackColor = Color.Transparent;
            _form.Invalidate(true);
        }
        finally
        {
            _applyingSampledColor = false;
        }
    }

    private static bool TrySampleTaskbarColor(Rectangle taskbar, out Color color)
    {
        color = Color.Empty;
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return false;

        try
        {
            var samples = new List<Color>(32);
            if (taskbar.Width >= taskbar.Height)
            {
                var xs = new[] { taskbar.Right - 2, taskbar.Right - 3, taskbar.Right - 4, taskbar.Right - 5, taskbar.Right - 6 };
                var ys = new[]
                {
                    taskbar.Top + Math.Max(4, taskbar.Height / 5),
                    taskbar.Top + Math.Max(6, taskbar.Height * 2 / 5),
                    taskbar.Top + Math.Max(8, taskbar.Height * 3 / 5),
                    taskbar.Bottom - Math.Max(5, taskbar.Height / 5)
                };
                foreach (var x in xs)
                foreach (var y in ys)
                    AddPixelSample(dc, x, y, samples);
            }
            else
            {
                var xs = new[] { taskbar.Left + taskbar.Width / 3, taskbar.Left + taskbar.Width * 2 / 3 };
                var ys = new[] { taskbar.Top + 6, taskbar.Top + 12, taskbar.Bottom - 6, taskbar.Bottom - 12 };
                foreach (var x in xs)
                foreach (var y in ys)
                    AddPixelSample(dc, x, y, samples);
            }

            if (samples.Count < 3)
                return false;

            var rs = samples.Select(c => (int)c.R).OrderBy(v => v).ToArray();
            var gs = samples.Select(c => (int)c.G).OrderBy(v => v).ToArray();
            var bs = samples.Select(c => (int)c.B).OrderBy(v => v).ToArray();
            var mid = samples.Count / 2;
            color = Color.FromArgb(rs[mid], gs[mid], bs[mid]);
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static void AddPixelSample(IntPtr dc, int x, int y, List<Color> samples)
    {
        var pixel = NativeMethods.GetPixel(dc, x, y);
        if (pixel == 0xFFFFFFFF)
            return;
        var r = (int)(pixel & 0xFF);
        var g = (int)((pixel >> 8) & 0xFF);
        var b = (int)((pixel >> 16) & 0xFF);
        samples.Add(Color.FromArgb(r, g, b));
    }

    private void RecoverIfShellTransitioned()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated || !ShouldOverlayBeVisible())
            return;

        LogicalOverlayShown = true;

        if (!_form.Visible || NativeMethods.IsIconic(_form.Handle) || !NativeMethods.IsWindowVisible(_form.Handle))
        {
            RestoreImmediately();
            return;
        }

        if (!IsOverlayFrontmost())
        {
            NativeMethods.SetWindowPos(
                _form.Handle,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private void RestoreImmediately()
    {
        if (_restoringVisibility || _disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;

        _restoringVisibility = true;
        try
        {
            LogicalOverlayShown = true;
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            NativeMethods.ShowWindow(_form.Handle, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.SetWindowPos(
                _form.Handle,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);
        }
        finally
        {
            _restoringVisibility = false;
        }
    }

    private bool ShouldOverlayBeVisible()
    {
        if (!TryGetVisibleTaskbarRect(out var taskbarRect))
            return false;
        return !IsRealFullscreenCoveringTaskbar(taskbarRect);
    }

    private bool IsOverlayFrontmost()
    {
        if (_form.Width <= 0 || _form.Height <= 0)
            return false;
        var point = new NativeMethods.POINT(
            _form.Left + Math.Max(1, _form.Width / 2),
            _form.Top + Math.Max(3, _form.Height / 2));
        var hit = NativeMethods.WindowFromPoint(point);
        return hit != IntPtr.Zero && NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) == _form.Handle;
    }

    private static bool TryGetVisibleTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar) || !NativeMethods.GetWindowRect(taskbar, out var native))
            return false;

        var shell = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (shell.Width <= 0 || shell.Height <= 0)
            return false;

        var screen = Screen.FromRectangle(shell);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        if (work.Bottom < bounds.Bottom)
            rect = Rectangle.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        else if (work.Top > bounds.Top)
            rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        else if (work.Right < bounds.Right)
            rect = Rectangle.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        else if (work.Left > bounds.Left)
            rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        else
            rect = shell;

        var visible = Rectangle.Intersect(rect, screen.Bounds);
        var visibleArea = (long)Math.Max(0, visible.Width) * Math.Max(0, visible.Height);
        var totalArea = (long)Math.Max(0, rect.Width) * Math.Max(0, rect.Height);
        return totalArea > 0 && visibleArea * 100 >= totalArea * 40;
    }

    private static bool IsRealFullscreenCoveringTaskbar(Rectangle taskbarRect)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            return false;

        var cls = NativeMethods.GetWindowClassName(foreground);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(taskbar, out var shellPid);
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (shellPid != 0 && shellPid == foregroundPid)
                return false;
        }

        if (!TryGetWindowBounds(foreground, out var windowBounds))
            return false;

        var screenBounds = Screen.FromRectangle(taskbarRect).Bounds;
        var screenArea = (long)screenBounds.Width * screenBounds.Height;
        var screenIntersection = Rectangle.Intersect(windowBounds, screenBounds);
        var coveredScreen = (long)Math.Max(0, screenIntersection.Width) * Math.Max(0, screenIntersection.Height);

        var taskbarArea = (long)taskbarRect.Width * taskbarRect.Height;
        var taskbarIntersection = Rectangle.Intersect(windowBounds, taskbarRect);
        var coveredTaskbar = (long)Math.Max(0, taskbarIntersection.Width) * Math.Max(0, taskbarIntersection.Height);

        return screenArea > 0 && taskbarArea > 0 &&
               coveredScreen * 100 >= screenArea * 97 &&
               coveredTaskbar * 100 >= taskbarArea * 95;
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

        if (!NativeMethods.GetWindowRect(window, out var native))
            return false;
        bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static IEnumerable<Control> FindDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in FindDescendants(child))
                yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _guardTimer.Stop();
        _colorTimer.Stop();
        _guardTimer.Dispose();
        _colorTimer.Dispose();

        if (_sourceDownload is not null)
            _sourceDownload.TextChanged -= SourceDownload_TextChanged;
        if (_sourceUpload is not null)
            _sourceUpload.TextChanged -= SourceUpload_TextChanged;

        _form.Shown -= Form_Shown;
        _form.VisibleChanged -= Form_VisibleChanged;
        _form.Resize -= Form_Resize;
        _form.SizeChanged -= Form_SizeChanged;
        _form.BackColorChanged -= Form_BackColorChanged;
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
            if (parts.Length >= 3)
            {
                _value = parts[1];
                _unit = parts[2];
            }
            else if (parts.Length >= 2)
            {
                _value = parts[1];
                _unit = string.Empty;
            }
            else
            {
                _value = "—";
                _unit = string.Empty;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var unitWidth = Math.Min(27, Math.Max(21, Width / 3));
            var arrowWidth = 9;
            var gap = 1;
            var valueWidth = Math.Max(12, Width - arrowWidth - gap - unitWidth);

            TextRenderer.DrawText(e.Graphics, _arrow, _font, new Rectangle(0, 0, arrowWidth, Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, _value, _font, new Rectangle(arrowWidth, 0, valueWidth, Height), ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, _unit, _font, new Rectangle(arrowWidth + valueWidth + gap, 0, unitWidth, Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _font.Dispose();
            base.Dispose(disposing);
        }
    }

    private static class NativeMethods
    {
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal const uint GA_ROOT = 2;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);
        internal static string GetWindowClassName(IntPtr hWnd)
        {
            var text = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        internal static extern uint GetPixel(IntPtr hDC, int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct POINT
        {
            public readonly int X;
            public readonly int Y;
            public POINT(int x, int y) { X = x; Y = y; }
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
