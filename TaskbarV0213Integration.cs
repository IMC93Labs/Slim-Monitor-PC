using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// v0.2.13: avoids the Windows Show desktop shell transition entirely for clicks on
/// the far-right Show desktop strip, while matching the real composited taskbar
/// background colour. No Explorer injection, DWM cloak/Peek hooks or fast Z-order
/// polling are used here.
/// </summary>
internal sealed class TaskbarV0213Integration : IDisposable
{
    private readonly Form _form;
    private readonly System.Windows.Forms.Timer _colorTimer = new() { Interval = 1500 };
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly List<IntPtr> _minimizedByUs = new();
    private IntPtr _mouseHook;
    private bool _disposed;
    private bool _swallowShowDesktopClick;

    private TaskbarV0213Integration(Form form)
    {
        _form = form;
        _mouseProc = MouseHookProc;

        _form.Shown += Form_Shown;
        _form.FormClosed += Form_FormClosed;
        _colorTimer.Tick += (_, _) => RefreshTaskbarColour();
    }

    internal static TaskbarV0213Integration Attach(Form form) => new(form);

    private void Form_Shown(object? sender, EventArgs e)
    {
        RefreshTaskbarColour();
        _colorTimer.Start();
        InstallMouseHook();
    }

    private void Form_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? IntPtr.Zero : NativeMethods.GetModuleHandle(module.ModuleName);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            var msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_LBUTTONUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var overShowDesktop = IsInsideShowDesktopStrip(data.pt.X, data.pt.Y);

                if (msg == NativeMethods.WM_LBUTTONDOWN)
                {
                    _swallowShowDesktopClick = overShowDesktop;
                    if (overShowDesktop)
                        return new IntPtr(1);
                }
                else if (_swallowShowDesktopClick)
                {
                    _swallowShowDesktopClick = false;
                    if (overShowDesktop)
                    {
                        try
                        {
                            if (!_form.IsDisposed && _form.IsHandleCreated)
                                _form.BeginInvoke((Action)ToggleDesktopPreservingOverlay);
                        }
                        catch
                        {
                            // The hook must stay minimal and must never affect Explorer.
                        }
                    }
                    return new IntPtr(1);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsInsideShowDesktopStrip(int x, int y)
    {
        if (!TryGetTaskbarRect(out var rect))
            return false;

        // Windows 11 uses a very narrow strip at the far-right edge on a horizontal
        // primary taskbar. Keep the interception deliberately tight so no tray icon
        // clicks are affected.
        if (rect.Width >= rect.Height)
            return x >= rect.Right - 11 && x < rect.Right && y >= rect.Top && y < rect.Bottom;

        return y >= rect.Bottom - 11 && y < rect.Bottom && x >= rect.Left && x < rect.Right;
    }

    private void ToggleDesktopPreservingOverlay()
    {
        if (_disposed || _form.IsDisposed)
            return;

        var visibleCandidates = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsDesktopCandidate(hwnd))
                visibleCandidates.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        if (visibleCandidates.Count > 0)
        {
            foreach (var hwnd in visibleCandidates)
            {
                if (!_minimizedByUs.Contains(hwnd))
                    _minimizedByUs.Add(hwnd);

                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MINIMIZE);
            }

            return;
        }

        for (var i = _minimizedByUs.Count - 1; i >= 0; i--)
        {
            var hwnd = _minimizedByUs[i];
            if (NativeMethods.IsWindow(hwnd) && NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
        }

        _minimizedByUs.Clear();
    }

    private bool IsDesktopCandidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _form.Handle)
            return false;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId)
            return false;

        var className = NativeMethods.GetWindowClassName(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow")
            return false;

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        var isToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
        var isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (isToolWindow && !isAppWindow)
            return false;
        if (owner != IntPtr.Zero && !isAppWindow)
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return false;

        return true;
    }

    private void RefreshTaskbarColour()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;
        if (!TryGetTaskbarRect(out var taskbar))
            return;

        var sampled = SampleCompositedTaskbarColour(taskbar, _form.Bounds);
        if (sampled is null)
            return;

        var colour = sampled.Value;
        if (_form.BackColor.ToArgb() == colour.ToArgb())
            return;

        _form.BackColor = colour;
        _form.Invalidate(true);
    }

    private static Color? SampleCompositedTaskbarColour(Rectangle taskbar, Rectangle overlay)
    {
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;

        try
        {
            var counts = new Dictionary<int, int>();
            var horizontal = taskbar.Width >= taskbar.Height;

            if (horizontal)
            {
                var ys = new[]
                {
                    taskbar.Top + Math.Max(3, taskbar.Height / 3),
                    taskbar.Top + Math.Max(4, taskbar.Height / 2),
                    taskbar.Bottom - Math.Max(4, taskbar.Height / 4)
                };

                for (var x = taskbar.Left + 6; x < taskbar.Right - 14; x += 5)
                {
                    foreach (var y in ys)
                    {
                        if (overlay.Contains(x, y))
                            continue;
                        AddPixel(dc, x, y, counts);
                    }
                }
            }
            else
            {
                var xs = new[]
                {
                    taskbar.Left + Math.Max(3, taskbar.Width / 3),
                    taskbar.Left + Math.Max(4, taskbar.Width / 2),
                    taskbar.Right - Math.Max(4, taskbar.Width / 4)
                };

                for (var y = taskbar.Top + 6; y < taskbar.Bottom - 14; y += 5)
                {
                    foreach (var x in xs)
                    {
                        if (overlay.Contains(x, y))
                            continue;
                        AddPixel(dc, x, y, counts);
                    }
                }
            }

            if (counts.Count == 0)
                return null;

            var best = counts.OrderByDescending(pair => pair.Value).First();
            if (best.Value < 8)
                return null;

            var r = (best.Key >> 16) & 0xFF;
            var g = (best.Key >> 8) & 0xFF;
            var b = best.Key & 0xFF;
            return Color.FromArgb(r, g, b);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static void AddPixel(IntPtr dc, int x, int y, Dictionary<int, int> counts)
    {
        var raw = NativeMethods.GetPixel(dc, x, y);
        if (raw == 0xFFFFFFFF)
            return;

        var r = (int)(raw & 0xFF);
        var g = (int)((raw >> 8) & 0xFF);
        var b = (int)((raw >> 16) & 0xFF);

        // Taskbar backgrounds are close to neutral; this rejects icons, text and
        // colourful app indicators while retaining the actual composited acrylic
        // background in both light and dark themes.
        if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 4)
            return;

        var key = (r << 16) | (g << 8) | b;
        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static bool TryGetTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
            return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var native))
            return false;

        rect = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return rect.Width > 0 && rect.Height > 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _colorTimer.Stop();
        _colorTimer.Dispose();

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _form.Shown -= Form_Shown;
        _form.FormClosed -= Form_FormClosed;
    }

    private static class NativeMethods
    {
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int SW_MINIMIZE = 6;
        internal const int SW_RESTORE = 9;
        internal const int GWL_EXSTYLE = -20;
        internal const int GW_OWNER = 4;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const long WS_EX_APPWINDOW = 0x00040000L;

        internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

        internal static string GetWindowClassName(IntPtr hWnd)
        {
            var text = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string? className, string? windowName);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern uint GetPixel(IntPtr hdc, int x, int y);
    }
}
