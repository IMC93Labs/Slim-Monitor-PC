using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.8 integration shim over the proven v0.2.7 form. It keeps the existing
/// network/calendar/startup logic while correcting the final Windows 11 shell
/// integration details: visual height, native-like hover and Show desktop hiding.
/// </summary>
internal sealed class TaskbarV028Integration : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly FieldInfo? _overlayShownField;
    private readonly WindowHook _hook;
    private readonly System.Windows.Forms.Timer _guardTimer = new() { Interval = 35 };
    private readonly List<Control> _surfaces = new();
    private TableLayoutPanel? _layout;
    private bool _hovered;
    private bool _disposed;

    private TaskbarV028Integration(TaskbarOverlayFormV027 form)
    {
        _form = form;
        _overlayShownField = typeof(TaskbarOverlayFormV027).GetField(
            "_overlayShown",
            BindingFlags.Instance | BindingFlags.NonPublic);

        _hook = new WindowHook(this);

        _form.HandleCreated += Form_HandleCreated;
        _form.HandleDestroyed += Form_HandleDestroyed;
        _form.Shown += Form_Shown;
        _form.Paint += Form_Paint;
        _form.BackColorChanged += Form_BackColorChanged;
        _form.FormClosed += (_, _) => Dispose();

        _guardTimer.Tick += (_, _) => GuardVisibleState();

        if (_form.IsHandleCreated)
            _hook.Assign(_form.Handle);
    }

    internal static TaskbarV028Integration Attach(TaskbarOverlayFormV027 form)
        => new(form);

    private void Form_HandleCreated(object? sender, EventArgs e)
        => _hook.Assign(_form.Handle);

    private void Form_HandleDestroyed(object? sender, EventArgs e)
        => _hook.Release();

    private void Form_Shown(object? sender, EventArgs e)
    {
        ApplyLayoutRefinement();
        WireHoverSurfaces();
        _guardTimer.Start();
        _form.Invalidate(true);
    }

    private void Form_BackColorChanged(object? sender, EventArgs e)
    {
        if (!_form.IsHandleCreated || _form.IsDisposed)
            return;

        _form.BeginInvoke((Action)(() =>
        {
            if (_form.IsDisposed)
                return;
            MakeContentTransparent();
            _form.Invalidate(true);
        }));
    }

    private void ApplyLayoutRefinement()
    {
        _layout = FindDescendants(_form).OfType<TableLayoutPanel>().FirstOrDefault();
        if (_layout is null)
            return;

        _layout.Padding = new Padding(4, 0, 3, 0);
        if (_layout.ColumnStyles.Count >= 2)
        {
            _layout.ColumnStyles[0].SizeType = SizeType.Percent;
            _layout.ColumnStyles[0].Width = 44f;
            _layout.ColumnStyles[1].SizeType = SizeType.Percent;
            _layout.ColumnStyles[1].Width = 56f;
        }

        var download = _layout.GetControlFromPosition(0, 0) as Label;
        var upload = _layout.GetControlFromPosition(0, 1) as Label;
        var time = _layout.GetControlFromPosition(1, 0) as Label;
        var date = _layout.GetControlFromPosition(1, 1) as Label;

        SetFont(download, 6.35f);
        SetFont(upload, 6.35f);
        SetFont(time, 10.4f);
        SetFont(date, 8.85f);

        MakeContentTransparent();
    }

    private static void SetFont(Label? label, float size)
    {
        if (label is null)
            return;

        var old = label.Font;
        label.Font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        old.Dispose();
    }

    private void MakeContentTransparent()
    {
        if (_layout is null)
            return;

        _layout.BackColor = Color.Transparent;
        foreach (var control in FindDescendants(_layout))
            control.BackColor = Color.Transparent;
    }

    private void WireHoverSurfaces()
    {
        foreach (var control in new[] { (Control)_form }.Concat(FindDescendants(_form)))
        {
            if (_surfaces.Contains(control))
                continue;

            _surfaces.Add(control);
            control.MouseEnter += Surface_MouseEnter;
            control.MouseLeave += Surface_MouseLeave;
        }
    }

    private void Surface_MouseEnter(object? sender, EventArgs e)
        => SetHovered(true);

    private void Surface_MouseLeave(object? sender, EventArgs e)
    {
        if (_form.IsDisposed || !_form.IsHandleCreated)
            return;

        _form.BeginInvoke((Action)(() => SetHovered(_form.Bounds.Contains(Control.MousePosition))));
    }

    private void SetHovered(bool value)
    {
        if (_hovered == value)
            return;

        _hovered = value;
        _form.Invalidate(true);
    }

    private void Form_Paint(object? sender, PaintEventArgs e)
    {
        if (!_hovered || _form.ClientSize.Width < 12 || _form.ClientSize.Height < 12)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = Rectangle.Inflate(_form.ClientRectangle, -3, -2);
        var light = _form.BackColor.GetBrightness() > 0.55f;
        var hover = light ? Color.FromArgb(226, 226, 226) : Color.FromArgb(52, 52, 52);

        using var path = RoundedRect(rect, 7);
        using var brush = new SolidBrush(hover);
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

    private static IEnumerable<Control> FindDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in FindDescendants(child))
                yield return nested;
        }
    }

    private bool LogicalOverlayShown
    {
        get => _overlayShownField?.GetValue(_form) as bool? ?? _form.Visible;
        set => _overlayShownField?.SetValue(_form, value);
    }

    private void GuardVisibleState()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated || !LogicalOverlayShown)
            return;

        if (!TaskbarIsActuallyAvailable(out var taskbarRect))
            return;

        if (IsRealFullscreenCoveringTaskbar(taskbarRect))
            return;

        if (!NativeMethods.IsWindowVisible(_form.Handle) || NativeMethods.IsIconic(_form.Handle))
            NativeMethods.ShowWindow(_form.Handle, NativeMethods.SW_SHOWNOACTIVATE);

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

    private bool ShouldBlockHide()
    {
        if (!TaskbarIsActuallyAvailable(out var taskbarRect))
            return false;

        return !IsRealFullscreenCoveringTaskbar(taskbarRect);
    }

    private bool IsOverlayFrontmost()
    {
        if (_form.Width <= 0 || _form.Height <= 0)
            return false;

        var point = new NativeMethods.POINT(
            _form.Left + Math.Max(1, _form.Width / 2),
            _form.Top + Math.Max(1, _form.Height / 2));
        var hit = NativeMethods.WindowFromPoint(point);
        return hit != IntPtr.Zero && NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) == _form.Handle;
    }

    private static bool TaskbarIsActuallyAvailable(out Rectangle taskbarRect)
    {
        taskbarRect = Rectangle.Empty;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar))
            return false;

        if (!NativeMethods.GetWindowRect(taskbar, out var native))
            return false;

        var shell = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (shell.Width <= 0 || shell.Height <= 0)
            return false;

        var screen = Screen.FromRectangle(shell);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;

        if (work.Bottom < bounds.Bottom)
            taskbarRect = Rectangle.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        else if (work.Top > bounds.Top)
            taskbarRect = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        else if (work.Right < bounds.Right)
            taskbarRect = Rectangle.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        else if (work.Left > bounds.Left)
            taskbarRect = Rectangle.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        else
            taskbarRect = shell;

        var intersection = Rectangle.Intersect(taskbarRect, screen.Bounds);
        var visibleArea = (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        var totalArea = (long)Math.Max(0, taskbarRect.Width) * Math.Max(0, taskbarRect.Height);
        return totalArea > 0 && visibleArea * 100 >= totalArea * 40;
    }

    private static bool IsRealFullscreenCoveringTaskbar(Rectangle taskbarRect)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            return false;

        var className = NativeMethods.GetWindowClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(taskbar, out var taskbarPid);
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (taskbarPid != 0 && taskbarPid == foregroundPid)
                return false;
        }

        if (!TryGetWindowBounds(foreground, out var windowBounds))
            return false;

        var taskbarArea = (long)taskbarRect.Width * taskbarRect.Height;
        var taskbarIntersection = Rectangle.Intersect(windowBounds, taskbarRect);
        var coveredTaskbar = (long)Math.Max(0, taskbarIntersection.Width) * Math.Max(0, taskbarIntersection.Height);
        if (taskbarArea <= 0 || coveredTaskbar * 100 < taskbarArea * 95)
            return false;

        var screenBounds = Screen.FromRectangle(taskbarRect).Bounds;
        var screenArea = (long)screenBounds.Width * screenBounds.Height;
        var screenIntersection = Rectangle.Intersect(windowBounds, screenBounds);
        var coveredScreen = (long)Math.Max(0, screenIntersection.Width) * Math.Max(0, screenIntersection.Height);
        return screenArea > 0 && coveredScreen * 100 >= screenArea * 97;
    }

    private static bool TryGetWindowBounds(IntPtr window, out Rectangle bounds)
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

    private void AdjustProposedWindowPos(ref NativeMethods.WINDOWPOS pos)
    {
        if ((pos.flags & (NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE)) != 0)
            return;

        if (!TaskbarIsActuallyAvailable(out var taskbarRect) || taskbarRect.Width < taskbarRect.Height)
            return;

        var desiredTopInset = taskbarRect.Height > 32 ? 3 : 2;
        pos.y = taskbarRect.Top + desiredTopInset;
        pos.cy = Math.Max(30, taskbarRect.Height - desiredTopInset);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _guardTimer.Stop();
        _guardTimer.Dispose();
        _hook.Release();

        foreach (var surface in _surfaces)
        {
            surface.MouseEnter -= Surface_MouseEnter;
            surface.MouseLeave -= Surface_MouseLeave;
        }
        _surfaces.Clear();

        _form.HandleCreated -= Form_HandleCreated;
        _form.HandleDestroyed -= Form_HandleDestroyed;
        _form.Shown -= Form_Shown;
        _form.Paint -= Form_Paint;
        _form.BackColorChanged -= Form_BackColorChanged;
    }

    private sealed class WindowHook : NativeWindow
    {
        private readonly TaskbarV028Integration _owner;

        internal WindowHook(TaskbarV028Integration owner) => _owner = owner;

        internal void Assign(IntPtr handle)
        {
            if (Handle == handle)
                return;
            if (Handle != IntPtr.Zero)
                ReleaseHandle();
            AssignHandle(handle);
        }

        internal void Release()
        {
            if (Handle != IntPtr.Zero)
                ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SYSCOMMAND &&
                (m.WParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
            {
                return;
            }

            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
                _owner.AdjustProposedWindowPos(ref pos);

                if ((pos.flags & NativeMethods.SWP_HIDEWINDOW) != 0 && _owner.ShouldBlockHide())
                {
                    pos.flags &= ~NativeMethods.SWP_HIDEWINDOW;
                    pos.flags &= ~NativeMethods.SWP_NOZORDER;
                    pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                    _owner.LogicalOverlayShown = true;
                }
                else if (_owner.LogicalOverlayShown)
                {
                    pos.flags &= ~NativeMethods.SWP_NOZORDER;
                    pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                }

                Marshal.StructureToPtr(pos, m.LParam, false);
            }

            base.WndProc(ref m);

            if (m.Msg == NativeMethods.WM_SIZE && m.WParam.ToInt64() == NativeMethods.SIZE_MINIMIZED)
            {
                _owner.LogicalOverlayShown = true;
                NativeMethods.ShowWindow(_owner._form.Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
        }
    }

    private static class NativeMethods
    {
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal const int WM_SIZE = 0x0005;
        internal const int WM_WINDOWPOSCHANGING = 0x0046;
        internal const int WM_SYSCOMMAND = 0x0112;
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

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
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

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
