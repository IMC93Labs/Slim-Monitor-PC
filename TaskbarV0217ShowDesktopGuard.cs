using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.17 flicker-only correction.
///
/// The legacy form owns a 200 ms shell timer that calls MaintainShellState().
/// During Windows Show Desktop, the foreground can temporarily be a shell/desktop
/// window. Treating that transient shell surface as a foreign fullscreen app can
/// make Slim Monitor PC call SW_HIDE on itself and then show again on a later tick,
/// which appears as the short clock/date flash seen in the real recordings.
///
/// This coordinator replaces only that timer cadence. While the foreground belongs
/// to the desktop shell transition, it leaves the already-visible overlay untouched.
/// For normal applications and real fullscreen games it delegates back to the exact
/// existing MaintainShellState() implementation.
/// </summary>
internal sealed class TaskbarV0217ShowDesktopGuard : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer? _legacyShellTimer;
    private readonly MethodInfo? _maintainShellState;
    private bool _disposed;

    private TaskbarV0217ShowDesktopGuard(TaskbarOverlayFormV027 form)
    {
        _form = form;

        _legacyShellTimer = typeof(TaskbarOverlayFormV027)
            .GetField("_shellTimer", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(form) as System.Windows.Forms.Timer;

        _maintainShellState = typeof(TaskbarOverlayFormV027)
            .GetMethod("MaintainShellState", BindingFlags.Instance | BindingFlags.NonPublic);

        _form.Shown += Form_Shown;
        _form.FormClosed += Form_FormClosed;
        _timer.Tick += Timer_Tick;

        if (_form.Visible)
            Activate();
    }

    internal static TaskbarV0217ShowDesktopGuard Attach(TaskbarOverlayFormV027 form)
        => new(form);

    private void Form_Shown(object? sender, EventArgs e)
        => Activate();

    private void Activate()
    {
        if (_disposed)
            return;

        // The base Shown handler starts its timer first. Stop only that timer and
        // keep the same 200 ms cadence here; no faster polling is introduced.
        _legacyShellTimer?.Stop();
        if (!_timer.Enabled)
            _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;

        // Critical difference: Show Desktop is a shell state, not a fullscreen app.
        // Do not let the legacy fullscreen detector hide the overlay during it.
        if (IsDesktopShellForeground())
            return;

        try
        {
            _maintainShellState?.Invoke(_form, new object?[] { false });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Preserve WinForms exception behavior instead of silently swallowing a
            // real shell-state error.
            throw ex.InnerException;
        }
    }

    private static bool IsDesktopShellForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        var root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = foreground;

        var shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow != IntPtr.Zero &&
            (foreground == shellWindow || root == shellWindow))
        {
            return true;
        }

        var className = GetClassName(root);
        if (IsDesktopShellClass(className))
            return true;

        // Some Windows 11 shell surfaces expose the input-site/bridge HWND as the
        // foreground root. Restrict PID-based handling to known shell-style classes
        // so ordinary File Explorer windows (CabinetWClass) are not excluded from
        // genuine fullscreen detection.
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(taskbar, out var taskbarPid);
            NativeMethods.GetWindowThreadProcessId(root, out var foregroundPid);
            if (taskbarPid != 0 && taskbarPid == foregroundPid && IsShellBridgeClass(className))
                return true;
        }

        return false;
    }

    private static bool IsDesktopShellClass(string className)
        => className is "Progman"
            or "WorkerW"
            or "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd";

    private static bool IsShellBridgeClass(string className)
        => className.Contains("DesktopWindowContentBridge", StringComparison.OrdinalIgnoreCase)
            || className.Contains("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase)
            || className.Contains("InputSite", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Desktop", StringComparison.OrdinalIgnoreCase);

    private static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private void Form_FormClosed(object? sender, FormClosedEventArgs e)
        => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _timer.Dispose();
        _form.Shown -= Form_Shown;
        _form.FormClosed -= Form_FormClosed;
    }

    private static class NativeMethods
    {
        internal const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
