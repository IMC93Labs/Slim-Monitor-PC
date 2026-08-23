using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.20 flicker fix.
///
/// Windows' native Show desktop / Win+D is allowed to temporarily remove ordinary
/// top-level windows, including always-on-top utility overlays. Rather than fighting
/// that compositor transition after it starts, this shim extends the existing Slim
/// Monitor PC window over the tiny native Show desktop strip and implements that one
/// mouse action locally with documented Win32 APIs.
///
/// No global mouse/keyboard hook is installed, Explorer is never subclassed or
/// reparented, and no DWM visibility state is changed.
/// </summary>
internal sealed class TaskbarV0220CustomShowDesktop : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly WindowHook _hook;
    private readonly ShowDesktopStrip _strip;
    private readonly Dictionary<IntPtr, WindowSnapshot> _minimizedByUs = new();
    private readonly FieldInfo? _calendarField;

    private TableLayoutPanel? _layout;
    private bool _ready;
    private bool _disposed;
    private bool _internalResize;
    private int _contentWidth;
    private int _stripWidth = 7;
    private IntPtr _foregroundBeforeDesktop = IntPtr.Zero;

    private TaskbarV0220CustomShowDesktop(TaskbarOverlayFormV027 form)
    {
        _form = form;
        _hook = new WindowHook(this);
        _strip = new ShowDesktopStrip(this);
        _calendarField = typeof(TaskbarOverlayFormV027).GetField(
            "_calendar",
            BindingFlags.Instance | BindingFlags.NonPublic);

        _form.HandleCreated += Form_HandleCreated;
        _form.HandleDestroyed += Form_HandleDestroyed;
        _form.Shown += Form_Shown;
        _form.SizeChanged += Form_SizeChanged;
        _form.BackColorChanged += Form_BackColorChanged;
        _form.FormClosed += Form_FormClosed;

        if (_form.IsHandleCreated)
            _hook.Assign(_form.Handle);
    }

    internal static TaskbarV0220CustomShowDesktop Attach(TaskbarOverlayFormV027 form)
        => new(form);

    private void Form_HandleCreated(object? sender, EventArgs e)
        => _hook.Assign(_form.Handle);

    private void Form_HandleDestroyed(object? sender, EventArgs e)
        => _hook.Release();

    private void Form_Shown(object? sender, EventArgs e)
    {
        if (_disposed || _ready)
            return;

        _layout = FindDescendants(_form).OfType<TableLayoutPanel>().FirstOrDefault();
        _contentWidth = Math.Max(1, _form.ClientSize.Width);
        _stripWidth = CalculateStripWidth(_form.ClientSize.Height);

        if (_layout is not null)
            _layout.Dock = DockStyle.None;

        _strip.BackColor = _form.BackColor;
        _form.Controls.Add(_strip);
        _strip.BringToFront();

        _ready = true;
        ExtendIntoNativeShowDesktopStrip();
        LayoutSurfaces();
    }

    private void Form_SizeChanged(object? sender, EventArgs e)
    {
        if (_ready && !_internalResize)
            LayoutSurfaces();
    }

    private void Form_BackColorChanged(object? sender, EventArgs e)
    {
        _strip.BackColor = _form.BackColor;
        _strip.Invalidate();
    }

    private void ExtendIntoNativeShowDesktopStrip()
    {
        if (!_ready || _disposed || !_form.IsHandleCreated || _form.IsDisposed)
            return;

        _stripWidth = CalculateStripWidth(_form.ClientSize.Height);
        var desiredWidth = Math.Max(1, _contentWidth + _stripWidth);
        if (_form.Width == desiredWidth)
            return;

        _internalResize = true;
        try
        {
            NativeMethods.SetWindowPos(
                _form.Handle,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                desiredWidth,
                _form.Height,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOACTIVATE);
        }
        finally
        {
            _internalResize = false;
        }
    }

    private void LayoutSurfaces()
    {
        if (!_ready || _form.IsDisposed)
            return;

        _stripWidth = CalculateStripWidth(_form.ClientSize.Height);
        var availableContent = Math.Max(1, _form.ClientSize.Width - _stripWidth);

        // Preserve the exact pre-v0.2.20 meter width. Only the far-right native
        // Show desktop strip is added to the form's hit-test area.
        _contentWidth = Math.Min(_contentWidth, availableContent);

        if (_layout is not null)
        {
            _layout.Dock = DockStyle.None;
            _layout.Bounds = new Rectangle(0, 0, availableContent, _form.ClientSize.Height);
        }

        _strip.Bounds = new Rectangle(
            availableContent,
            0,
            Math.Max(1, _stripWidth),
            _form.ClientSize.Height);
        _strip.BringToFront();
    }

    private static int CalculateStripWidth(int taskbarHeight)
        => Math.Clamp(Math.Max(1, taskbarHeight) / 8, 5, 9);

    private void ToggleDesktop()
    {
        if (_disposed || _form.IsDisposed || !_form.Visible)
            return;

        CloseOwnPopups();

        var currentlyVisible = EnumerateEligibleWindows();
        if (_minimizedByUs.Count > 0 && currentlyVisible.Count == 0)
        {
            RestoreWindows();
            return;
        }

        MinimizeWindows(currentlyVisible);
    }

    private void CloseOwnPopups()
    {
        _form.ContextMenuStrip?.Close();

        if (_calendarField?.GetValue(_form) is Form calendar && !calendar.IsDisposed)
        {
            try { calendar.Close(); } catch { }
        }
    }

    private void MinimizeWindows(List<WindowSnapshot> windows)
    {
        if (windows.Count == 0)
            return;

        if (_minimizedByUs.Count == 0)
            _foregroundBeforeDesktop = NativeMethods.GetForegroundWindow();

        foreach (var snapshot in windows)
        {
            if (!_minimizedByUs.ContainsKey(snapshot.Handle))
                _minimizedByUs[snapshot.Handle] = snapshot;

            // SW_SHOWMINNOACTIVE gives the same minimized state without activating
            // each application while we walk the desktop. ShowWindowAsync posts the
            // request and cannot hang us behind an unresponsive application.
            NativeMethods.ShowWindowAsync(snapshot.Handle, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }

    private void RestoreWindows()
    {
        if (_minimizedByUs.Count == 0)
            return;

        // Restore bottom-to-top so the window that was originally foremost is
        // naturally restored last. Pre-existing minimized windows were never stored.
        var snapshots = _minimizedByUs.Values.ToArray();
        for (var i = snapshots.Length - 1; i >= 0; i--)
        {
            var snapshot = snapshots[i];
            if (!NativeMethods.IsWindow(snapshot.Handle))
                continue;

            NativeMethods.GetWindowThreadProcessId(snapshot.Handle, out var currentPid);
            if (currentPid == 0 || currentPid != snapshot.ProcessId)
                continue;

            if (!NativeMethods.IsIconic(snapshot.Handle))
                continue;

            NativeMethods.ShowWindowAsync(
                snapshot.Handle,
                snapshot.WasMaximized ? NativeMethods.SW_SHOWMAXIMIZED : NativeMethods.SW_RESTORE);
        }

        var previousForeground = _foregroundBeforeDesktop;
        _minimizedByUs.Clear();
        _foregroundBeforeDesktop = IntPtr.Zero;

        if (previousForeground != IntPtr.Zero && NativeMethods.IsWindow(previousForeground))
            NativeMethods.SetForegroundWindow(previousForeground);
    }

    private List<WindowSnapshot> EnumerateEligibleWindows()
    {
        var result = new List<WindowSnapshot>();
        var currentPid = (uint)Environment.ProcessId;
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows((window, _) =>
        {
            if (window == IntPtr.Zero || window == _form.Handle || window == shellWindow)
                return true;

            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
                return true;

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == currentPid)
                return true;

            var className = NativeMethods.GetClassName(window);
            if (IsShellInfrastructureClass(className))
                return true;

            if (NativeMethods.IsCloaked(window))
                return true;

            var exStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_EXSTYLE).ToInt64();
            var isToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
            var isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
            if (isToolWindow && !isAppWindow)
                return true;

            var owner = NativeMethods.GetWindow(window, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero && !isAppWindow)
                return true;

            result.Add(new WindowSnapshot(
                window,
                processId,
                NativeMethods.IsZoomed(window)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsShellInfrastructureClass(string className)
        => className is
            "Progman" or
            "WorkerW" or
            "Shell_TrayWnd" or
            "Shell_SecondaryTrayWnd" or
            "NotifyIconOverflowWindow" or
            "TopLevelWindowForOverflowXamlIsland" or
            "Windows.UI.Composition.DesktopWindowContentBridge" or
            "Windows.UI.Input.InputSite.WindowClass";

    private void AdjustProposedWindowPos(ref NativeMethods.WINDOWPOS pos)
    {
        if (!_ready || _internalResize || (pos.flags & NativeMethods.SWP_NOSIZE) != 0)
            return;

        // The legacy positioning code still computes the approved meter width and
        // deliberately leaves the native 5-9 px Show desktop strip free. Expand only
        // that proposal by exactly the same strip width, keeping X unchanged. This
        // overlays the native button without shifting the meter even one pixel.
        if (pos.cx < 100 || pos.cx > 220 || pos.cy < 20)
            return;

        var strip = CalculateStripWidth(pos.cy);
        var currentTotal = _contentWidth + _stripWidth;
        if (pos.cx == currentTotal)
            return;

        _contentWidth = pos.cx;
        _stripWidth = strip;
        pos.cx += strip;
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

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hook.Release();

        _form.HandleCreated -= Form_HandleCreated;
        _form.HandleDestroyed -= Form_HandleDestroyed;
        _form.Shown -= Form_Shown;
        _form.SizeChanged -= Form_SizeChanged;
        _form.BackColorChanged -= Form_BackColorChanged;
        _form.FormClosed -= Form_FormClosed;

        _strip.Dispose();
        _minimizedByUs.Clear();
    }

    private readonly record struct WindowSnapshot(IntPtr Handle, uint ProcessId, bool WasMaximized);

    private sealed class ShowDesktopStrip : Control
    {
        private readonly TaskbarV0220CustomShowDesktop _owner;
        private bool _hovered;
        private bool _pressed;

        internal ShowDesktopStrip(TaskbarV0220CustomShowDesktop owner)
        {
            _owner = owner;
            Cursor = Cursors.Default;
            TabStop = false;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            if (!_pressed)
                Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _pressed = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            var invoke = _pressed && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Capture = false;
            Invalidate();

            if (invoke)
                _owner.ToggleDesktop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var baseColor = Parent?.BackColor ?? BackColor;
            using (var baseBrush = new SolidBrush(baseColor))
                e.Graphics.FillRectangle(baseBrush, ClientRectangle);

            if (_hovered || _pressed)
            {
                var light = baseColor.GetBrightness() > 0.55f;
                var highlight = _pressed
                    ? (light ? Color.FromArgb(205, 205, 205) : Color.FromArgb(68, 68, 68))
                    : (light ? Color.FromArgb(224, 224, 224) : Color.FromArgb(48, 48, 48));
                using var brush = new SolidBrush(highlight);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            // Keep the subtle divider that Windows draws beside the native strip.
            var divider = baseColor.GetBrightness() > 0.55f
                ? Color.FromArgb(205, 205, 205)
                : Color.FromArgb(48, 48, 48);
            using var pen = new Pen(divider);
            e.Graphics.DrawLine(pen, 0, 1, 0, Math.Max(1, Height - 2));
        }
    }

    private sealed class WindowHook : NativeWindow
    {
        private readonly TaskbarV0220CustomShowDesktop _owner;

        internal WindowHook(TaskbarV0220CustomShowDesktop owner) => _owner = owner;

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
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
                _owner.AdjustProposedWindowPos(ref pos);
                Marshal.StructureToPtr(pos, m.LParam, false);
            }

            base.WndProc(ref m);
        }
    }

    private static class NativeMethods
    {
        internal const int WM_WINDOWPOSCHANGING = 0x0046;
        internal const int GWL_EXSTYLE = -20;
        internal const uint GW_OWNER = 4;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const long WS_EX_APPWINDOW = 0x00040000L;
        internal const int DWMWA_CLOAKED = 14;

        internal const int SW_SHOWMAXIMIZED = 3;
        internal const int SW_SHOWMINNOACTIVE = 7;
        internal const int SW_RESTORE = 9;

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;

        internal static readonly IntPtr HWND_TOPMOST = new(-1);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        internal static string GetClassName(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            return GetClassName(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out int pvAttribute,
            int cbAttribute);

        internal static bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                var hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
                return hr == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            internal IntPtr hwnd;
            internal IntPtr hwndInsertAfter;
            internal int x;
            internal int y;
            internal int cx;
            internal int cy;
            internal uint flags;
        }
    }
}
