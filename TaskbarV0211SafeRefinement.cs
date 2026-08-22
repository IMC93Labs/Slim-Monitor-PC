using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SlimMonitorPC;

/// <summary>
/// Safe v0.2.11 UI-only refinement layered on top of the proven v0.2.8 shell behavior.
/// It deliberately avoids DWM, Explorer hooks, cloaking APIs and aggressive Z-order timers.
/// </summary>
internal sealed class TaskbarV0211SafeRefinement : IDisposable
{
    private readonly TaskbarOverlayFormV027 _form;
    private readonly object _v028Integration;
    private readonly FieldInfo? _hoveredField;
    private readonly MethodInfo? _toggleCalendarMethod;
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 120 };

    private TableLayoutPanel? _layout;
    private Label? _sourceDownload;
    private Label? _sourceUpload;
    private RateCell? _downloadCell;
    private RateCell? _uploadCell;
    private bool _disposed;

    private TaskbarV0211SafeRefinement(TaskbarOverlayFormV027 form, object v028Integration)
    {
        _form = form;
        _v028Integration = v028Integration;
        _hoveredField = v028Integration.GetType().GetField("_hovered", BindingFlags.Instance | BindingFlags.NonPublic);
        _toggleCalendarMethod = typeof(TaskbarOverlayFormV027).GetMethod("ToggleCalendar", BindingFlags.Instance | BindingFlags.NonPublic);

        _form.Shown += Form_Shown;
        _form.FormClosed += (_, _) => Dispose();
        _hoverTimer.Tick += (_, _) => RefreshHoverState();
    }

    internal static TaskbarV0211SafeRefinement Attach(TaskbarOverlayFormV027 form, object v028Integration)
        => new(form, v028Integration);

    private void Form_Shown(object? sender, EventArgs e)
    {
        InstallStableRateCells();
        RefreshHoverState();
        _hoverTimer.Start();
    }

    private void RefreshHoverState()
    {
        if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            return;

        var hovered = _form.Visible && _form.Bounds.Contains(Control.MousePosition);
        if (_hoveredField?.GetValue(_v028Integration) is bool current && current == hovered)
            return;

        _hoveredField?.SetValue(_v028Integration, hovered);
        _form.Invalidate(true);
    }

    private void InstallStableRateCells()
    {
        if (_layout is not null)
            return;

        _layout = FindDescendants(_form).OfType<TableLayoutPanel>().FirstOrDefault();
        if (_layout is null || _layout.ColumnStyles.Count < 2)
            return;

        _layout.Padding = new Padding(4, 0, 3, 0);
        _layout.ColumnStyles[0].SizeType = SizeType.Percent;
        _layout.ColumnStyles[0].Width = 41.5f;
        _layout.ColumnStyles[1].SizeType = SizeType.Percent;
        _layout.ColumnStyles[1].Width = 58.5f;

        _sourceDownload = _layout.GetControlFromPosition(0, 0) as Label;
        _sourceUpload = _layout.GetControlFromPosition(0, 1) as Label;

        var time = _layout.GetControlFromPosition(1, 0) as Label;
        var date = _layout.GetControlFromPosition(1, 1) as Label;
        SetFont(time, 10.4f);
        SetFont(date, 8.45f);

        if (_sourceDownload is not null)
        {
            _sourceDownload.TextChanged += SourceDownload_TextChanged;
            _layout.Controls.Remove(_sourceDownload);
            _sourceDownload.Visible = false;
        }

        if (_sourceUpload is not null)
        {
            _sourceUpload.TextChanged += SourceUpload_TextChanged;
            _layout.Controls.Remove(_sourceUpload);
            _sourceUpload.Visible = false;
        }

        _downloadCell = new RateCell("↓") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _uploadCell = new RateCell("↑") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        ConfigureRateCell(_downloadCell);
        ConfigureRateCell(_uploadCell);

        _layout.Controls.Add(_downloadCell, 0, 0);
        _layout.Controls.Add(_uploadCell, 0, 1);

        _downloadCell.SetText(_sourceDownload?.Text ?? "↓ 0 B/s");
        _uploadCell.SetText(_sourceUpload?.Text ?? "↑ 0 B/s");
    }

    private void ConfigureRateCell(RateCell cell)
    {
        cell.ContextMenuStrip = _form.ContextMenuStrip;
        cell.Cursor = Cursors.Hand;
        cell.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _toggleCalendarMethod?.Invoke(_form, null);
        };
    }

    private static void SetFont(Label? label, float size)
    {
        if (label is null)
            return;

        var old = label.Font;
        label.Font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        old.Dispose();
    }

    private void SourceDownload_TextChanged(object? sender, EventArgs e)
        => _downloadCell?.SetText(_sourceDownload?.Text ?? "↓ —");

    private void SourceUpload_TextChanged(object? sender, EventArgs e)
        => _uploadCell?.SetText(_sourceUpload?.Text ?? "↑ —");

    private static IEnumerable<Control> FindDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in FindDescendants(child))
                yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hoverTimer.Stop();
        _hoverTimer.Dispose();

        if (_sourceDownload is not null)
            _sourceDownload.TextChanged -= SourceDownload_TextChanged;
        if (_sourceUpload is not null)
            _sourceUpload.TextChanged -= SourceUpload_TextChanged;

        _form.Shown -= Form_Shown;
    }

    private sealed class RateCell : Control
    {
        private readonly string _arrow;
        private string _value = "0";
        private string _unit = "B/s";
        private readonly Font _font = new("Segoe UI", 6.15f, FontStyle.Regular, GraphicsUnit.Point);

        internal RateCell(string arrow)
        {
            _arrow = arrow;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        internal void SetText(string text)
        {
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                _value = parts[1];
                _unit = parts[2];
            }
            else if (parts.Length >= 2)
            {
                _value = parts[1];
                _unit = string.Empty;
            }
            else
            {
                _value = "—";
                _unit = string.Empty;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var arrowWidth = 9;
            var unitWidth = Math.Min(27, Math.Max(21, Width / 3));
            var gap = 1;
            var valueWidth = Math.Max(12, Width - arrowWidth - gap - unitWidth);

            TextRenderer.DrawText(
                e.Graphics,
                _arrow,
                _font,
                new Rectangle(0, 0, arrowWidth, Height),
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                e.Graphics,
                _value,
                _font,
                new Rectangle(arrowWidth, 0, valueWidth, Height),
                ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                e.Graphics,
                _unit,
                _font,
                new Rectangle(arrowWidth + valueWidth + gap, 0, unitWidth, Height),
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
