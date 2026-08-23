using System.Drawing.Drawing2D;
using System.Globalization;

namespace SlimMonitorPC;

internal sealed class CalendarPopup : Form
{
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private readonly bool _light;
    private Rectangle _previousButton;
    private Rectangle _nextButton;

    internal CalendarPopup(bool light)
    {
        _light = light;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        Size = new Size(324, 306);
        Padding = Padding.Empty;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = _light ? Color.FromArgb(248, 248, 248) : Color.FromArgb(32, 32, 32);
        ForeColor = _light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);
        Deactivate += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => false;

    internal void ShowNear(Rectangle widgetBounds, Rectangle taskbarBounds, IWin32Window owner)
    {
        var screen = Screen.FromRectangle(widgetBounds);
        var x = Math.Clamp(widgetBounds.Right - Width, screen.WorkingArea.Left, screen.WorkingArea.Right - Width);
        var y = taskbarBounds.Top >= screen.Bounds.Bottom - taskbarBounds.Height - 2
            ? taskbarBounds.Top - Height - 8
            : taskbarBounds.Bottom + 8;
        y = Math.Clamp(y, screen.WorkingArea.Top, screen.WorkingArea.Bottom - Height);
        Location = new Point(x, y);
        Show(owner);
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var border = _light ? Color.FromArgb(215, 215, 215) : Color.FromArgb(58, 58, 58);
        using (var pen = new Pen(border))
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var headerHeight = 54;
        var pad = 14;
        _previousButton = new Rectangle(Width - 82, 10, 30, 30);
        _nextButton = new Rectangle(Width - 44, 10, 30, 30);

        using var titleFont = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            e.Graphics,
            _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
            titleFont,
            new Rectangle(pad, 10, Width - 110, 30),
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        DrawChevron(e.Graphics, _previousButton, left: true);
        DrawChevron(e.Graphics, _nextButton, left: false);

        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var gridTop = headerHeight;
        var columnWidth = (Width - pad * 2) / 7f;
        var rowHeight = 34f;

        using var smallFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
        var muted = _light ? Color.FromArgb(100, 100, 100) : Color.FromArgb(170, 170, 170);
        for (var column = 0; column < 7; column++)
        {
            var dayIndex = ((int)firstDay + column) % 7;
            var rect = Rectangle.Round(new RectangleF(pad + column * columnWidth, gridTop, columnWidth, 24));
            TextRenderer.DrawText(e.Graphics, dayNames[dayIndex], smallFont, rect, muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var offset = ((7 + (int)_month.DayOfWeek - (int)firstDay) % 7);
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var today = DateTime.Today;
        var firstCellTop = gridTop + 26;

        for (var day = 1; day <= days; day++)
        {
            var index = offset + day - 1;
            var row = index / 7;
            var column = index % 7;
            var rectF = new RectangleF(pad + column * columnWidth, firstCellTop + row * rowHeight, columnWidth, rowHeight);
            var rect = Rectangle.Round(rectF);
            var isToday = today.Year == _month.Year && today.Month == _month.Month && today.Day == day;

            if (isToday)
            {
                var circleSize = 29;
                var circle = new Rectangle(
                    rect.Left + (rect.Width - circleSize) / 2,
                    rect.Top + (rect.Height - circleSize) / 2,
                    circleSize,
                    circleSize);
                using var brush = new SolidBrush(Color.FromArgb(0, 120, 212));
                e.Graphics.FillEllipse(brush, circle);
            }

            TextRenderer.DrawText(
                e.Graphics,
                day.ToString(CultureInfo.CurrentCulture),
                Font,
                rect,
                isToday ? Color.White : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawChevron(Graphics graphics, Rectangle bounds, bool left)
    {
        var hover = bounds.Contains(PointToClient(Cursor.Position));
        if (hover)
        {
            using var brush = new SolidBrush(_light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(50, 50, 50));
            graphics.FillEllipse(brush, bounds);
        }

        var x = bounds.Left + bounds.Width / 2;
        var y = bounds.Top + bounds.Height / 2;
        using var pen = new Pen(ForeColor, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (left)
            graphics.DrawLines(pen, new[] { new Point(x + 3, y - 5), new Point(x - 2, y), new Point(x + 3, y + 5) });
        else
            graphics.DrawLines(pen, new[] { new Point(x - 3, y - 5), new Point(x + 2, y), new Point(x - 3, y + 5) });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_previousButton.Contains(e.Location) || _nextButton.Contains(e.Location))
            Invalidate(new Rectangle(Width - 90, 5, 86, 40));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate(new Rectangle(Width - 90, 5, 86, 40));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;

        if (_previousButton.Contains(e.Location))
        {
            _month = _month.AddMonths(-1);
            Invalidate();
        }
        else if (_nextButton.Contains(e.Location))
        {
            _month = _month.AddMonths(1);
            Invalidate();
        }
    }
}
