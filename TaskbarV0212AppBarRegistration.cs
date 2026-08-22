using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// Registers the existing overlay with the Windows shell as an application desktop
/// toolbar (appbar) without claiming/reserving any desktop work area. The goal is
/// only to make the shell classify the window as taskbar-style infrastructure so
/// Show desktop does not transiently treat it like a normal application window.
///
/// Important: this class deliberately does NOT call ABM_QUERYPOS or ABM_SETPOS and
/// never changes Explorer/taskbar HWNDs. The existing v0.2.8 positioning/fullscreen
/// logic remains authoritative.
/// </summary>
internal sealed class TaskbarV0212AppBarRegistration : IDisposable
{
    private const int AppBarCallbackMessage = 0x8000 + 0x212; // WM_APP + private id

    private readonly Form _form;
    private bool _registered;
    private bool _disposed;

    private TaskbarV0212AppBarRegistration(Form form)
    {
        _form = form;
        _form.HandleCreated += Form_HandleCreated;
        _form.HandleDestroyed += Form_HandleDestroyed;
        _form.FormClosed += Form_FormClosed;

        if (_form.IsHandleCreated)
            Register();
    }

    internal static TaskbarV0212AppBarRegistration Attach(Form form) => new(form);

    private void Form_HandleCreated(object? sender, EventArgs e) => Register();

    private void Form_HandleDestroyed(object? sender, EventArgs e) => Unregister();

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    private void Register()
    {
        if (_disposed || _registered || !_form.IsHandleCreated)
            return;

        var data = CreateData();
        data.uCallbackMessage = AppBarCallbackMessage;

        // ABM_NEW only registers the HWND with the shell. We intentionally never
        // call ABM_SETPOS, therefore no screen/work-area space is reserved.
        _registered = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data) != UIntPtr.Zero;
    }

    private void Unregister()
    {
        if (!_registered || !_form.IsHandleCreated)
        {
            _registered = false;
            return;
        }

        var data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        _registered = false;
    }

    private NativeMethods.APPBARDATA CreateData()
        => new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = _form.Handle
        };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();

        _form.HandleCreated -= Form_HandleCreated;
        _form.HandleDestroyed -= Form_HandleDestroyed;
        _form.FormClosed -= Form_FormClosed;
    }

    private static class NativeMethods
    {
        internal const uint ABM_NEW = 0x00000000;
        internal const uint ABM_REMOVE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        internal struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        internal static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    }
}
