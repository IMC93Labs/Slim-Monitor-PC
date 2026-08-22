using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// Samples the already-rendered taskbar immediately beside the overlay at the same
/// vertical level. This avoids the transparent/shadow area included in Shell_TrayWnd
/// on some Windows 11 builds.
/// </summary>
internal sealed class TaskbarV0213ExactColour : IDisposable
{
    private readonly Form _form;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1200 };
    private bool _disposed;

    private TaskbarV0213ExactColour(Form form, TaskbarV0213Integration integration)
    {
        _form = form;

        // Disable the broader sampler in the integration class. The real-machine
        // recording showed that Shell_TrayWnd can include a transparent band above
        // the visible taskbar, so only this same-height sampler is authoritative.
        var field = typeof(TaskbarV0213Integration).GetField("_colorTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(integration) is System.Windows.Forms.Timer previous)
            previous.Stop();

        _form.Shown += Form_Shown;
        _form.LocationChanged += Form_MovedOrSized;
        _form.SizeChanged += Form_MovedOrSized;
        _form.FormClosed += (_, _) => Dispose();
        _timer.Tick += (_, _) => Refresh();
    }

    internal static TaskbarV0213ExactColour Attach(Form form, TaskbarV0213Integration integration)
        => new(form, integration);

    private void Form_Shown(object? sender, EventArgs e)
    {
        Refresh();
        _timer.Start();
    }

    private void Form_MovedOrSized(object? sender, EventArgs e)
        => Refresh();

    private void Refresh()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated || !_form.Visible || _form.Width <= 0 || _form.Height <= 0)
            return;

        var colour = SampleAdjacentTaskbar();
        if (colour is null || colour.Value.ToArgb() == _form.BackColor.ToArgb())
            return;

        _form.BackColor = colour.Value;
        _form.Invalidate(true);
    }

    private Color? SampleAdjacentTaskbar()
    {
        var screen = Screen.FromControl(_form).Bounds;
        var overlay = _form.Bounds;
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;

        try
        {
            var counts = new Dictionary<int, int>();
            var ys = new[]
            {
                overlay.Top + Math.Max(2, overlay.Height / 3),
                overlay.Top + Math.Max(3, overlay.Height / 2),
                overlay.Bottom - Math.Max(3, overlay.Height / 4)
            };

            // Sample the full taskbar row but never the overlay itself. Exact RGB
            // mode wins; icons/text are sparse while the composited background is
            // repeated across hundreds of points.
            for (var x = screen.Left + 8; x < screen.Right - 16; x += 4)
            {
                if (x >= overlay.Left - 2 && x <= overlay.Right + 2)
                    continue;

                foreach (var y in ys)
                {
                    if (y < screen.Top || y >= screen.Bottom)
                        continue;

                    var raw = GetPixel(dc, x, y);
                    if (raw == 0xFFFFFFFF)
                        continue;

                    var r = (int)(raw & 0xFF);
                    var g = (int)((raw >> 8) & 0xFF);
                    var b = (int)((raw >> 16) & 0xFF);
                    if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 3)
                        continue;

                    var key = (r << 16) | (g << 8) | b;
                    counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }

            if (counts.Count == 0)
                return null;

            var best = counts.OrderByDescending(x => x.Value).First();
            if (best.Value < 12)
                return null;

            return Color.FromArgb((best.Key >> 16) & 0xFF, (best.Key >> 8) & 0xFF, best.Key & 0xFF);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _form.Shown -= Form_Shown;
        _form.LocationChanged -= Form_MovedOrSized;
        _form.SizeChanged -= Form_MovedOrSized;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int x, int y);
}
