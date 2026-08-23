using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.18 flicker-only guard.
///
/// Windows Show Desktop can temporarily remove/minimize ordinary top-level windows,
/// including topmost utility overlays. Instead of fighting that shell transition,
/// keep a passive visual mirror as a real child of Shell_TrayWnd. The normal overlay
/// remains the interactive surface. If Windows briefly removes it, the taskbar-owned
/// mirror is already underneath with the same pixels, so the native clock/date never
/// flashes through.
///
/// The mirror never intercepts input, never manipulates Explorer's window procedure,
/// and never changes the main overlay's visibility or Z-order.
/// </summary>
internal sealed class TaskbarV0218FlashMirror : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly MirrorForm _mirror = new();
    private readonly System.Windows.Forms.Timer _syncTimer = new() { Interval = 200 };
    private IntPtr _taskbar = IntPtr.Zero;
    private Rectangle _lastMirrorScreenBounds = Rectangle.Empty;
    private bool _disposed;

    private TaskbarV0218FlashMirror(TaskbarOverlayFormV027 form)
    {
        _form = form;

        _form.Shown += Form_Shown;
        _form.LocationChanged += Form_GeometryChanged;
        _form.SizeChanged += Form_GeometryChanged;
        _form.Paint += Form_Paint;
        _form.FormClosed += Form_FormClosed;

        _syncTimer.Tick += SyncTimer_Tick;
    }

    internal static TaskbarV0218FlashMirror Attach(TaskbarOverlayFormV027 form)
        => new(form);

    private void Form_Shown(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        EnsureMirrorAttached(forcePosition: true);
        RefreshSnapshot();
        _syncTimer.Start();
    }

    private void Form_GeometryChanged(object? sender, EventArgs e)
    {
        if (!_disposed && _form.IsHandleCreated)
            EnsureMirrorAttached(forcePosition: true);
    }

    private void Form_Paint(object? sender, PaintEventArgs e)
    {
        if (_disposed || !_form.Visible)
            return;

        // Update after the current paint has completed, so the mirror receives the
        // final composited WinForms surface rather than a half-painted frame.
        _form.BeginInvoke((Action)(() =>
        {
            if (!_disposed)
                RefreshSnapshot();
        }));
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        EnsureMirrorAttached(forcePosition: false);
        RefreshSnapshot();
    }

    private void EnsureMirrorAttached(bool forcePosition)
    {
        if (_disposed || !_form.IsHandleCreated || _form.IsDisposed)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar))
            return;

        if (!NativeMethods.GetWindowRect(taskbar, out var tbRectNative) ||
            !NativeMethods.GetWindowRect(_form.Handle, out var formRectNative))
            return;

        var taskbarScreen = Rectangle.FromLTRB(
            tbRectNative.Left,
            tbRectNative.Top,
            tbRectNative.Right,
            tbRectNative.Bottom);

        var formScreen = Rectangle.FromLTRB(
            formRectNative.Left,
            formRectNative.Top,
            formRectNative.Right,
            formRectNative.Bottom);

        if (taskbarScreen.Width <= 0 || taskbarScreen.Height <= 0 ||
            formScreen.Width <= 0 || formScreen.Height <= 0)
            return;

        _ = _mirror.Handle;

        var parentChanged = _taskbar != taskbar || NativeMethods.GetParent(_mirror.Handle) != taskbar;
        if (parentChanged)
        {
            AttachMirrorToTaskbar(taskbar);
            _taskbar = taskbar;
            forcePosition = true;
        }

        if (forcePosition || formScreen != _lastMirrorScreenBounds)
        {
            var x = formScreen.Left - taskbarScreen.Left;
            var y = formScreen.Top - taskbarScreen.Top;

            NativeMethods.SetWindowPos(
                _mirror.Handle,
                NativeMethods.HWND_TOP,
                x,
                y,
                formScreen.Width,
                formScreen.Height,
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);

            _lastMirrorScreenBounds = formScreen;
        }
    }

    private void AttachMirrorToTaskbar(IntPtr taskbar)
    {
        var style = NativeMethods.GetWindowLongPtr(_mirror.Handle, NativeMethods.GWL_STYLE).ToInt64();
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
        style &= ~NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLongPtr(_mirror.Handle, NativeMethods.GWL_STYLE, new IntPtr(style));

        // Windows 11's taskbar UI is composition/XAML hosted. A fully opaque layered
        // child is used here so the child remains paintable in the taskbar hierarchy,
        // following the same Win32 pattern used by established taskbar monitors.
        var exStyle = NativeMethods.GetWindowLongPtr(_mirror.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW |
                   NativeMethods.WS_EX_NOACTIVATE |
                   NativeMethods.WS_EX_LAYERED |
                   NativeMethods.WS_EX_COMPOSITED |
                   NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_mirror.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        NativeMethods.SetParent(_mirror.Handle, taskbar);
        NativeMethods.SetLayeredWindowAttributes(_mirror.Handle, 0, 255, NativeMethods.LWA_ALPHA);

        NativeMethods.SetWindowPos(
            _mirror.Handle,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void RefreshSnapshot()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated ||
            !_form.Visible || NativeMethods.IsIconic(_form.Handle) ||
            _form.ClientSize.Width <= 0 || _form.ClientSize.Height <= 0)
            return;

        try
        {
            using var bitmap = new Bitmap(
                _form.ClientSize.Width,
                _form.ClientSize.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            _form.DrawToBitmap(bitmap, _form.ClientRectangle);
            _mirror.SetSnapshot(bitmap);
        }
        catch
        {
            // A missed mirror refresh must never affect the real taskbar overlay.
            // Keep the last valid snapshot and try again on the next normal tick.
        }
    }

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _syncTimer.Stop();
        _syncTimer.Dispose();

        _form.Shown -= Form_Shown;
        _form.LocationChanged -= Form_GeometryChanged;
        _form.SizeChanged -= Form_GeometryChanged;
        _form.Paint -= Form_Paint;
        _form.FormClosed -= Form_FormClosed;

        _mirror.Dispose();
    }

    private sealed class MirrorForm : Form
    {
        private Bitmap? _snapshot;

        internal MirrorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = false;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            DoubleBuffered = true;
            Enabled = true;
        }

        protected override bool ShowWithoutActivation => true;

        internal void SetSnapshot(Bitmap source)
        {
            if (IsDisposed)
                return;

            var replacement = source.Clone(
                new Rectangle(Point.Empty, source.Size),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            var previous = _snapshot;
            _snapshot = replacement;
            previous?.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_snapshot is not null)
                e.Graphics.DrawImageUnscaled(_snapshot, Point.Empty);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                m.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _snapshot?.Dispose();
                _snapshot = null;
            }
            base.Dispose(disposing);
        }
    }

    private static class NativeMethods
    {
        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;

        internal const long WS_CHILD = 0x40000000L;
        internal const long WS_POPUP = unchecked((long)0x80000000L);
        internal const long WS_VISIBLE = 0x10000000L;

        internal const long WS_EX_TRANSPARENT = 0x00000020L;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const long WS_EX_LAYERED = 0x00080000L;
        internal const long WS_EX_NOACTIVATE = 0x08000000L;
        internal const long WS_EX_COMPOSITED = 0x02000000L;

        internal const uint LWA_ALPHA = 0x00000002;

        internal const int WM_NCHITTEST = 0x0084;
        internal const int HTTRANSPARENT = -1;

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint crKey,
            byte bAlpha,
            uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }
    }
}
