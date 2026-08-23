using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// Flicker-only fix for Windows Show desktop.
///
/// Windows' Show desktop raises the desktop in the Z order while true topmost
/// windows remain above it. The legacy overlay path made Slim Monitor PC an
/// owned top-level window of Shell_TrayWnd, coupling its visibility to Explorer.
/// This shim keeps the overlay ownerless and explicitly topmost without touching
/// Explorer, installing global hooks, intercepting input, or adding fast timers.
/// </summary>
internal sealed class TaskbarV0216OwnerlessTopmost : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly FieldInfo? _taskbarOwnerField;
    private bool _disposed;

    private TaskbarV0216OwnerlessTopmost(TaskbarOverlayFormV027 form)
    {
        _form = form;
        _taskbarOwnerField = typeof(TaskbarOverlayFormV027).GetField(
            "_taskbarOwner",
            BindingFlags.Instance | BindingFlags.NonPublic);

        _form.HandleCreated += Form_HandleCreated;
        _form.Shown += Form_Shown;
        _form.FormClosed += Form_FormClosed;

        if (_form.IsHandleCreated)
            Apply();
    }

    internal static TaskbarV0216OwnerlessTopmost Attach(TaskbarOverlayFormV027 form)
        => new(form);

    private void Form_HandleCreated(object? sender, EventArgs e) => Apply();

    private void Form_Shown(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);

        // Prevent the legacy MaintainShellState path from assigning Shell_TrayWnd
        // as this top-level window's owner. We intentionally remain ownerless.
        if (taskbar != IntPtr.Zero)
            _taskbarOwnerField?.SetValue(_form, taskbar);

        // Clear any owner that may have been assigned before this shim ran.
        NativeMethods.SetWindowLongPtr(_form.Handle, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);

        // Establish true topmost state directly. According to the Windows shell
        // model, Show desktop raises the desktop but leaves topmost windows above it.
        NativeMethods.SetWindowPos(
            _form.Handle,
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

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _form.HandleCreated -= Form_HandleCreated;
        _form.Shown -= Form_Shown;
        _form.FormClosed -= Form_FormClosed;
    }

    private static class NativeMethods
    {
        internal const int GWLP_HWNDPARENT = -8;
        internal static readonly IntPtr HWND_TOPMOST = new(-1);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

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
    }
}
