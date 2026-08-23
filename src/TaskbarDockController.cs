using System.Runtime.InteropServices;

namespace SlimMonitorPC;

/// <summary>
/// Keeps the widget structurally docked to the Windows taskbar without parenting
/// it into Explorer. The widget remains a normal top-level window whose OWNER is
/// Shell_TrayWnd. Z-order is asserted exactly once when ownership is established
/// (and again only after an Explorer restart). Steady-state refreshes never
/// promote Z-order; they only adjust geometry with SWP_NOZORDER.
/// </summary>
internal sealed class TaskbarDockController : IDisposable
{
    private readonly TaskbarWidgetForm _form;
    private readonly NativeMethods.WinEventProc _winEventProc;
    private readonly System.Windows.Forms.Timer _debounceTimer = new() { Interval = 55 };
    private readonly System.Windows.Forms.Timer _safetyTimer = new() { Interval = 1500 };

    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private IntPtr _showHideHook;
    private IntPtr _taskbar;
    private Rectangle _lastBounds = Rectangle.Empty;
    private bool _seatedInTopmostBand;
    private bool _hiddenByShellState;
    private bool _disposed;

    internal TaskbarDockController(TaskbarWidgetForm form)
    {
        _form = form;
        _winEventProc = WinEventCallback;
        _form.WidgetShown += Form_WidgetShown;
        _form.FormClosed += Form_FormClosed;
        _debounceTimer.Tick += DebounceTimer_Tick;
        _safetyTimer.Tick += SafetyTimer_Tick;
    }

    private void Form_WidgetShown(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        InstallHooks();
        RefreshAuthoritative(forceGeometry: true);
        _safetyTimer.Start();
    }

    private void InstallHooks()
    {
        const uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            flags);

        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            flags);

        _showHideHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_HIDE,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            flags);
    }

    private void WinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (_disposed || hwnd == IntPtr.Zero || !_form.IsHandleCreated || _form.IsDisposed)
            return;

        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && idObject != NativeMethods.OBJID_WINDOW)
            return;

        if (eventType is NativeMethods.EVENT_OBJECT_SHOW or NativeMethods.EVENT_OBJECT_HIDE)
        {
            if (idObject != NativeMethods.OBJID_WINDOW)
                return;

            var foreground = NativeMethods.GetForegroundWindow();
            if (hwnd != _taskbar && hwnd != foreground)
                return;
        }

        try
        {
            _form.BeginInvoke((Action)ScheduleRefresh);
        }
        catch
        {
            // The form can be destroyed between the WinEvent callback and BeginInvoke.
        }
    }

    private void ScheduleRefresh()
    {
        if (_disposed || _form.IsDisposed)
            return;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RefreshAuthoritative(forceGeometry: false);
    }

    private void SafetyTimer_Tick(object? sender, EventArgs e)
        => RefreshAuthoritative(forceGeometry: false);

    private void RefreshAuthoritative(bool forceGeometry)
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar) || !TryGetVisibleTaskbarRect(taskbar, out var taskbarRect))
        {
            HideWidget();
            return;
        }

        var ownerChanged = EnsureTaskbarOwner(taskbar);
        _taskbar = taskbar;
        _form.SetTaskbarBounds(taskbarRect);

        if (IsForeignFullscreenCoveringTaskbar(taskbar, taskbarRect))
        {
            HideWidget();
            return;
        }

        var target = CalculateWidgetBounds(taskbarRect);
        var geometryChanged = target != _lastBounds;

        if (ownerChanged || !_seatedInTopmostBand)
        {
            // One authoritative Z-order seat only. Once the taskbar owns the widget,
            // Windows maintains the owner/owned relationship; repeated TOPMOST calls
            // are deliberately forbidden because they are a known source of one-frame
            // shell/widget flicker.
            NativeMethods.SetWindowPos(
                _form.Handle,
                NativeMethods.HWND_TOPMOST,
                target.Left,
                target.Top,
                target.Width,
                target.Height,
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW |
                NativeMethods.SWP_NOOWNERZORDER);
            _seatedInTopmostBand = true;
            _lastBounds = target;
        }
        else if (forceGeometry || geometryChanged)
        {
            // Geometry only: never change Z-order in steady state.
            NativeMethods.SetWindowPos(
                _form.Handle,
                IntPtr.Zero,
                target.Left,
                target.Top,
                target.Width,
                target.Height,
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOOWNERZORDER |
                NativeMethods.SWP_SHOWWINDOW);
            _lastBounds = target;
        }
        else if (!NativeMethods.IsWindowVisible(_form.Handle) || NativeMethods.IsIconic(_form.Handle))
        {
            // Recovery path only. It intentionally does not touch Z-order.
            NativeMethods.ShowWindow(_form.Handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        _hiddenByShellState = false;
        _form.RevealAfterDock();
    }

    private bool EnsureTaskbarOwner(IntPtr taskbar)
    {
        var currentOwner = NativeMethods.GetWindowLongPtr(_form.Handle, NativeMethods.GWLP_HWNDPARENT);
        if (currentOwner == taskbar)
            return false;

        NativeMethods.SetWindowLongPtr(_form.Handle, NativeMethods.GWLP_HWNDPARENT, taskbar);
        _seatedInTopmostBand = false;
        return true;
    }

    private void HideWidget()
    {
        if (_hiddenByShellState)
            return;

        _hiddenByShellState = true;
        _form.ConcealForFullscreen();
        if (_form.IsHandleCreated && NativeMethods.IsWindowVisible(_form.Handle))
            NativeMethods.ShowWindow(_form.Handle, NativeMethods.SW_HIDE);
    }

    private static Rectangle CalculateWidgetBounds(Rectangle taskbar)
    {
        if (taskbar.Width < taskbar.Height)
        {
            var width = Math.Max(40, taskbar.Width - 4);
            var height = Math.Min(82, Math.Max(30, taskbar.Height - 4));
            return new Rectangle(
                taskbar.Left + Math.Max(0, (taskbar.Width - width) / 2),
                taskbar.Bottom - height - 2,
                width,
                height);
        }

        var showDesktopStrip = Math.Clamp(taskbar.Height / 8, 5, 9);
        var widthHorizontal = Math.Clamp((int)Math.Round(taskbar.Height * 3.30), 150, 160);
        var topInset = taskbar.Height > 32 ? 3 : 2;
        var heightHorizontal = Math.Max(30, taskbar.Height - topInset);
        var x = Math.Max(taskbar.Left, taskbar.Right - showDesktopStrip - widthHorizontal);
        var y = taskbar.Top + topInset;
        return new Rectangle(x, y, widthHorizontal, heightHorizontal);
    }

    private static bool TryGetVisibleTaskbarRect(IntPtr taskbar, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!NativeMethods.GetWindowRect(taskbar, out var native))
            return false;

        var shellRect = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (shellRect.Width <= 0 || shellRect.Height <= 0)
            return false;

        var screen = Screen.FromHandle(taskbar);
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
        {
            // Auto-hide does not reserve WorkingArea. Use only the portion of the
            // shell window that is actually on-screen; a thin edge means hidden.
            rect = Rectangle.Intersect(shellRect, bounds);
            if (rect.Width <= 0 || rect.Height <= 0)
                return false;

            if (rect.Width >= rect.Height && rect.Height < 18)
                return false;
            if (rect.Height > rect.Width && rect.Width < 18)
                return false;
        }

        return rect.Width > 0 && rect.Height > 0;
    }

    private bool IsForeignFullscreenCoveringTaskbar(IntPtr taskbar, Rectangle taskbarRect)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _form.Handle || foreground == taskbar)
            return false;

        if (foreground == NativeMethods.GetShellWindow())
            return false;

        if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            return false;

        var className = NativeMethods.GetWindowClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        NativeMethods.GetWindowThreadProcessId(taskbar, out var taskbarPid);
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
        if (taskbarPid != 0 && foregroundPid == taskbarPid)
            return false;

        if (!TryGetVisibleWindowBounds(foreground, out var foregroundBounds))
            return false;

        var screen = Screen.FromRectangle(taskbarRect);
        var screenBounds = screen.Bounds;
        var taskbarIntersection = Rectangle.Intersect(foregroundBounds, taskbarRect);
        var screenIntersection = Rectangle.Intersect(foregroundBounds, screenBounds);

        var taskbarArea = (long)taskbarRect.Width * taskbarRect.Height;
        var coveredTaskbarArea = (long)Math.Max(0, taskbarIntersection.Width) * Math.Max(0, taskbarIntersection.Height);
        if (taskbarArea <= 0 || coveredTaskbarArea * 100 < taskbarArea * 92)
            return false;

        var screenArea = (long)screenBounds.Width * screenBounds.Height;
        var coveredScreenArea = (long)Math.Max(0, screenIntersection.Width) * Math.Max(0, screenIntersection.Height);
        return screenArea > 0 && coveredScreenArea * 100 >= screenArea * 97;
    }

    private static bool TryGetVisibleWindowBounds(IntPtr hwnd, out Rectangle bounds)
    {
        var extended = new NativeMethods.RECT();
        var hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out extended,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0 && extended.Right > extended.Left && extended.Bottom > extended.Top)
        {
            bounds = Rectangle.FromLTRB(extended.Left, extended.Top, extended.Right, extended.Bottom);
            return true;
        }

        if (NativeMethods.GetWindowRect(hwnd, out var native) && native.Right > native.Left && native.Bottom > native.Top)
        {
            bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            return true;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _debounceTimer.Stop();
        _safetyTimer.Stop();
        _debounceTimer.Dispose();
        _safetyTimer.Dispose();

        if (_foregroundHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_locationHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_locationHook);
        if (_showHideHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_showHideHook);

        _foregroundHook = IntPtr.Zero;
        _locationHook = IntPtr.Zero;
        _showHideHook = IntPtr.Zero;

        _form.WidgetShown -= Form_WidgetShown;
        _form.FormClosed -= Form_FormClosed;
    }
}
