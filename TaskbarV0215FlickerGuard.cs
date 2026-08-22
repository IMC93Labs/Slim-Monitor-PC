using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// Flicker-only refinement for v0.2.15.
/// Applies documented DWM attributes only to Slim Monitor PC's own HWND so the
/// overlay is excluded from Peek/Show desktop visual transitions. No Explorer
/// hooks, no global mouse/keyboard hooks and no polling timers are used here.
/// </summary>
internal sealed class TaskbarV0215FlickerGuard : IDisposable
{
    private readonly Form _form;
    private bool _disposed;

    private TaskbarV0215FlickerGuard(Form form)
    {
        _form = form;
        _form.HandleCreated += Form_HandleCreated;
        _form.FormClosed += Form_FormClosed;

        if (_form.IsHandleCreated)
            Apply();
    }

    internal static TaskbarV0215FlickerGuard Attach(Form form) => new(form);

    private void Form_HandleCreated(object? sender, EventArgs e) => Apply();

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    private void Apply()
    {
        if (_disposed || !_form.IsHandleCreated || _form.IsDisposed)
            return;

        var enabled = 1;

        // Keep this window out of DWM's task-switch / Peek transition pipeline.
        // These calls affect only our own HWND and fail harmlessly if unsupported.
        _ = NativeMethods.DwmSetWindowAttribute(
            _form.Handle,
            NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED,
            ref enabled,
            sizeof(int));

        _ = NativeMethods.DwmSetWindowAttribute(
            _form.Handle,
            NativeMethods.DWMWA_DISALLOW_PEEK,
            ref enabled,
            sizeof(int));

        _ = NativeMethods.DwmSetWindowAttribute(
            _form.Handle,
            NativeMethods.DWMWA_EXCLUDED_FROM_PEEK,
            ref enabled,
            sizeof(int));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _form.HandleCreated -= Form_HandleCreated;
        _form.FormClosed -= Form_FormClosed;
    }

    private static class NativeMethods
    {
        internal const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        internal const int DWMWA_DISALLOW_PEEK = 11;
        internal const int DWMWA_EXCLUDED_FROM_PEEK = 12;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);
    }
}
