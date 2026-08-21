using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlimMonitorPC;

public sealed class CalendarPopup : Form
{
    private DateTime _displayMonth;
    private DateTime _selectedDate;
    private readonly bool _light;
    private Rectangle _previousRect;
    private Rectangle _nextRect;
    private Rectangle _todayRect;
    private readonly List<(Rectangle Rect, DateTime Date)> _dayRects = new();

    private Color PanelColor => _light ? Color.FromArgb(248, 248, 248) : Color.FromArgb(36, 36, 36);
    private Color TextColor => _light ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 245, 245);
    private Color MutedColor => _light ? Color.FromArgb(95, 95, 95) : Color.FromArgb(170, 170, 170);
    private Color HoverColor => _light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(55, 55, 55);
    private Color AccentColor => Color.FromArgb(0, 120, 212);

    public CalendarPopup(bool light)
    {
        _light = light;
        _selectedDate = DateTime.Today;
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = PanelColor;
        ForeColor = TextColor;

        Deactivate += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        };
        MouseUp += CalendarPopup_MouseUp;
        MouseWheel += (_, e) =>
        {
            _displayMonth = _displayMonth.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyRoundedCorners();
    }

    public void ShowNear(Rectangle anchor, Rectangle taskbar)
    {
        var scale = DeviceDpi / 96f;
        var width = (int)Math.Round(336 * scale);
        var height = (int)Math.Round(356 * scale);
        Size = new Size(width, height);

        var screen = Screen.FromRectangle(anchor).WorkingArea;
        var x = Math.Clamp(anchor.Right - width, screen.Left, screen.Right - width);
        int y;

        var horizontalTaskbar = taskbar.Width >= taskbar.Height;
        var taskbarAtBottom = horizontalTaskbar && taskbar.Top >= screen.Bottom - Math.Max(taskbar.Height * 2, 120);
        if (taskbarAtBottom || anchor.Top > screen.Top + screen.Height / 2)
            y = Math.Max(screen.Top, anchor.Top - height - ScalePx(8));
        else
            y = Math.Min(screen.Bottom - height, anchor.Bottom + ScalePx(8));

        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(PanelColor);
        _dayRects.Clear();

        var pad = ScalePx(16);
        var width = ClientSize.Width;
        var culture = CultureInfo.CurrentCulture;

        using var titleFont = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
        using var monthFont = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
        using var dayFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        using var weekdayFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
        using var todayFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

        var todayText = DateTime.Today.ToString("dddd, d 'de' MMMM", culture);
        todayText = culture.TextInfo.ToTitleCase(todayText);
        TextRenderer.DrawText(g, todayText, titleFont,
            new Rectangle(pad, ScalePx(14), width - pad * 2, ScalePx(34)),
            TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        using var separatorPen = new Pen(_light ? Color.FromArgb(220, 220, 220) : Color.FromArgb(62, 62, 62));
        g.DrawLine(separatorPen, pad, ScalePx(54), width - pad, ScalePx(54));

        var headerY = ScalePx(67);
        var navSize = ScalePx(30);
        _previousRect = new Rectangle(width - pad - navSize * 2 - ScalePx(4), headerY - ScalePx(4), navSize, navSize);
        _nextRect = new Rectangle(width - pad - navSize, headerY - ScalePx(4), navSize, navSize);

        var monthTitle = _displayMonth.ToString("MMMM 'de' yyyy", culture);
        monthTitle = culture.TextInfo.ToTitleCase(monthTitle);
        TextRenderer.DrawText(g, monthTitle, monthFont,
            new Rectangle(pad, headerY, _previousRect.Left - pad - ScalePx(4), ScalePx(24)),
            TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        DrawChevron(g, _previousRect, left: true);
        DrawChevron(g, _nextRect, left: false);

        var gridTop = ScalePx(107);
        var gridWidth = width - pad * 2;
        var columnWidth = gridWidth / 7f;
        var weekdayHeight = ScalePx(24);
        var cellHeight = ScalePx(36);

        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)firstDayOfWeek + i) % 7);
            var abbreviated = culture.DateTimeFormat.GetAbbreviatedDayName(day).TrimEnd('.');
            var label = string.IsNullOrWhiteSpace(abbreviated) ? "?" : abbreviated[..1].ToUpper(culture);
            var rect = new Rectangle((int)(pad + i * columnWidth), gridTop, (int)Math.Ceiling(columnWidth), weekdayHeight);
            TextRenderer.DrawText(g, label, weekdayFont, rect, MutedColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var firstOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        var offset = ((7 + (int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek) % 7);
        var cursor = firstOfMonth.AddDays(-offset);
        var rowTop = gridTop + weekdayHeight;

        for (var row = 0; row < 6; row++)
        {
            for (var col = 0; col < 7; col++)
            {
                var rect = new Rectangle(
                    (int)(pad + col * columnWidth),
                    rowTop + row * cellHeight,
                    (int)Math.Ceiling(columnWidth),
                    cellHeight);
                _dayRects.Add((rect, cursor));

                var isCurrentMonth = cursor.Month == _displayMonth.Month;
                var isToday = cursor.Date == DateTime.Today;
                var isSelected = cursor.Date == _selectedDate.Date;

                if (isSelected || isToday)
                {
                    var diameter = ScalePx(30);
                    var circle = new Rectangle(
                        rect.Left + (rect.Width - diameter) / 2,
                        rect.Top + (rect.Height - diameter) / 2,
                        diameter,
                        diameter);
                    using var brush = new SolidBrush(isSelected ? AccentColor : (_light ? Color.FromArgb(225, 239, 252) : Color.FromArgb(25, 70, 105)));
                    g.FillEllipse(brush, circle);
                }

                var color = isSelected ? Color.White : isCurrentMonth ? TextColor : MutedColor;
                TextRenderer.DrawText(g, cursor.Day.ToString(culture), dayFont, rect, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                cursor = cursor.AddDays(1);
            }
        }

        _todayRect = new Rectangle(pad, ClientSize.Height - ScalePx(42), ScalePx(82), ScalePx(28));
        using (var brush = new SolidBrush(HoverColor))
            FillRoundedRectangle(g, brush, _todayRect, ScalePx(7));
        TextRenderer.DrawText(g, "Hoy", todayFont, _todayRect, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void CalendarPopup_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_previousRect.Contains(e.Location))
        {
            _displayMonth = _displayMonth.AddMonths(-1);
            Invalidate();
            return;
        }

        if (_nextRect.Contains(e.Location))
        {
            _displayMonth = _displayMonth.AddMonths(1);
            Invalidate();
            return;
        }

        if (_todayRect.Contains(e.Location))
        {
            _selectedDate = DateTime.Today;
            _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Invalidate();
            return;
        }

        foreach (var cell in _dayRects)
        {
            if (!cell.Rect.Contains(e.Location))
                continue;

            _selectedDate = cell.Date;
            _displayMonth = new DateTime(cell.Date.Year, cell.Date.Month, 1);
            Invalidate();
            return;
        }
    }

    private void DrawChevron(Graphics g, Rectangle rect, bool left)
    {
        using var pen = new Pen(TextColor, Math.Max(1.2f, DeviceDpi / 96f * 1.3f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };

        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        var d = ScalePx(4);
        var points = left
            ? new[] { new Point(cx + d / 2, cy - d), new Point(cx - d / 2, cy), new Point(cx + d / 2, cy + d) }
            : new[] { new Point(cx - d / 2, cy - d), new Point(cx + d / 2, cy), new Point(cx - d / 2, cy + d) };
        g.DrawLines(pen, points);
    }

    private int ScalePx(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    private void ApplyRoundedCorners()
    {
        try
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            const int DWMWCP_ROUND = 2;
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch
        {
            // Rounded corners are cosmetic; Windows 10 simply keeps the rectangular popup.
        }
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
