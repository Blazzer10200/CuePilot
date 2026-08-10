using System.Drawing.Drawing2D;

namespace WorkflowLooper;

internal static class AppTheme
{
    internal static readonly Color Canvas = Color.FromArgb(11, 15, 18);
    internal static readonly Color Surface = Color.FromArgb(18, 24, 28);
    internal static readonly Color Raised = Color.FromArgb(25, 33, 38);
    internal static readonly Color RaisedHover = Color.FromArgb(32, 43, 48);
    internal static readonly Color Border = Color.FromArgb(43, 56, 63);
    internal static readonly Color Text = Color.FromArgb(244, 247, 245);
    internal static readonly Color Muted = Color.FromArgb(147, 162, 155);
    internal static readonly Color Accent = Color.FromArgb(98, 232, 179);
    internal static readonly Color AccentDark = Color.FromArgb(25, 72, 56);
    internal static readonly Color Coral = Color.FromArgb(255, 117, 107);
    internal static readonly Color Warning = Color.FromArgb(255, 201, 90);

    internal static Color Blend(Color from, Color to, double amount)
    {
        var value = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * value),
            (int)Math.Round(from.G + (to.G - from.G) * value),
            (int)Math.Round(from.B + (to.B - from.B) * value));
    }
}

internal sealed class SmoothPanel : Panel
{
    internal SmoothPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}

internal sealed class ThemedButton : Button
{
    private bool hovered;
    private bool pressed;

    internal Color FillColor { get; set; } = AppTheme.Raised;
    internal Color HoverColor { get; set; } = AppTheme.RaisedHover;
    internal Color PressedColor { get; set; } = AppTheme.AccentDark;
    internal Color LineColor { get; set; } = AppTheme.Border;
    internal Color LabelColor { get; set; } = AppTheme.Text;
    internal bool ShowChevron { get; set; }
    internal bool DrawBorder { get; set; } = true;
    internal ButtonGlyph Glyph { get; set; }

    internal ThemedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = AppTheme.Raised;
        ForeColor = AppTheme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var fill = !Enabled ? AppTheme.Blend(FillColor, AppTheme.Canvas, 0.45) : pressed ? PressedColor : hovered ? HoverColor : FillColor;
        using var background = new SolidBrush(fill);
        e.Graphics.FillRectangle(background, ClientRectangle);
        if (DrawBorder)
        {
            using var border = new Pen(Focused ? AppTheme.Accent : LineColor);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        var textColor = Enabled ? LabelColor : AppTheme.Blend(LabelColor, AppTheme.Canvas, 0.48);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var bounds = new Rectangle(14, 0, Width - (ShowChevron ? 48 : 28), Height);
        if (TextAlign == ContentAlignment.MiddleCenter)
        {
            flags |= TextFormatFlags.HorizontalCenter;
            bounds = new Rectangle(8, 0, Width - 16, Height);
        }
        else
        {
            flags |= TextFormatFlags.Left;
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColor, flags);
        if (ShowChevron)
        {
            var centerX = Width - 22;
            var centerY = Height / 2;
            using var pen = new Pen(textColor, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(pen, centerX - 5, centerY - 2, centerX, centerY + 3);
            e.Graphics.DrawLine(pen, centerX, centerY + 3, centerX + 5, centerY - 2);
        }


        if (Glyph != ButtonGlyph.None)
        {
            DrawGlyph(e.Graphics, textColor);
        }
    }

    private void DrawGlyph(Graphics graphics, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = Width / 2F;
        var centerY = Height / 2F;
        using var pen = new Pen(color, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (Glyph)
        {
            case ButtonGlyph.Minimize:
                graphics.DrawLine(pen, centerX - 6, centerY + 3, centerX + 6, centerY + 3);
                break;
            case ButtonGlyph.Close:
                graphics.DrawLine(pen, centerX - 5, centerY - 5, centerX + 5, centerY + 5);
                graphics.DrawLine(pen, centerX + 5, centerY - 5, centerX - 5, centerY + 5);
                break;
            case ButtonGlyph.Refresh:
                graphics.DrawArc(pen, centerX - 7, centerY - 7, 14, 14, -55, 285);
                using (var arrow = new SolidBrush(color))
                {
                    graphics.FillPolygon(arrow, [new PointF(centerX + 7, centerY - 6), new PointF(centerX + 7, centerY + 1), new PointF(centerX + 2, centerY - 2)]);
                }

                break;
            case ButtonGlyph.Folder:
                graphics.DrawRectangle(pen, centerX - 7, centerY - 3, 12, 9);
                graphics.DrawLine(pen, centerX - 7, centerY - 3, centerX - 4, centerY - 7);
                graphics.DrawLine(pen, centerX - 4, centerY - 7, centerX, centerY - 7);
                graphics.DrawLine(pen, centerX + 1, centerY + 1, centerX + 8, centerY - 6);
                graphics.DrawLine(pen, centerX + 4, centerY - 6, centerX + 8, centerY - 6);
                graphics.DrawLine(pen, centerX + 8, centerY - 6, centerX + 8, centerY - 2);
                break;
        }
    }
}

internal enum ButtonGlyph
{
    None,
    Minimize,
    Close,
    Refresh,
    Folder,
}

internal sealed class StepperControl : Control
{
    private int value = 1;
    private int hoverZone;

    internal int Minimum { get; set; }
    internal int Maximum { get; set; } = 100;
    internal int Step { get; set; } = 1;
    internal string Suffix { get; set; } = string.Empty;
    internal event EventHandler? ValueChanged;

    internal int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (this.value == next)
            {
                return;
            }

            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal StepperControl()
    {
        BackColor = AppTheme.Raised;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI Semibold", 9.5F);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var zoneWidth = 34;
        DrawZone(e.Graphics, new Rectangle(Width - zoneWidth * 2, 0, zoneWidth, Height), hoverZone == 1, "−");
        DrawZone(e.Graphics, new Rectangle(Width - zoneWidth, 0, zoneWidth, Height), hoverZone == 2, "+");
        using var border = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        e.Graphics.DrawLine(border, Width - zoneWidth * 2, 0, Width - zoneWidth * 2, Height);
        e.Graphics.DrawLine(border, Width - zoneWidth, 0, Width - zoneWidth, Height);
        TextRenderer.DrawText(
            e.Graphics,
            $"{Value}{Suffix}",
            Font,
            new Rectangle(12, 0, Width - zoneWidth * 2 - 18, Height),
            ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    private void DrawZone(Graphics graphics, Rectangle bounds, bool active, string glyph)
    {
        using var brush = new SolidBrush(active ? AppTheme.AccentDark : AppTheme.Raised);
        graphics.FillRectangle(brush, bounds);
        TextRenderer.DrawText(graphics, glyph, new Font("Segoe UI Semibold", 12F), bounds, active ? AppTheme.Accent : AppTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var next = e.X >= Width - 34 ? 2 : e.X >= Width - 68 ? 1 : 0;
        if (next != hoverZone)
        {
            hoverZone = next;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverZone = 0;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left)
        {
            if (e.X >= Width - 34)
            {
                Value += Step;
            }
            else if (e.X >= Width - 68)
            {
                Value -= Step;
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += e.Delta > 0 ? Step : -Step;
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Right)
        {
            Value += Step;
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Down or Keys.Left)
        {
            Value -= Step;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool isChecked;
    private bool hovered;

    internal event EventHandler? CheckedChanged;

    internal bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
            {
                return;
            }

            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal ToggleSwitch()
    {
        Size = new Size(46, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 2, Width - 1, Height - 5);
        using var path = Rounded(track, track.Height / 2);
        using var trackBrush = new SolidBrush(Checked ? AppTheme.AccentDark : hovered ? AppTheme.RaisedHover : AppTheme.Raised);
        using var border = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        e.Graphics.FillPath(trackBrush, path);
        e.Graphics.DrawPath(border, path);
        var knobSize = 14;
        var knobX = Checked ? Width - knobSize - 6 : 6;
        using var knob = new SolidBrush(Checked ? AppTheme.Accent : AppTheme.Muted);
        e.Graphics.FillEllipse(knob, knobX, (Height - knobSize) / 2 - 1, knobSize, knobSize);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 90, 180);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}

internal sealed record PatternListItem(string Path, WorkflowPattern Pattern);

internal sealed class PatternListControl : Control
{
    private const int ItemHeight = 56;
    private readonly List<PatternListItem> items = [];
    private int selectedIndex = -1;
    private int scrollOffset;
    private bool draggingThumb;
    private int thumbDragOffset;

    internal event EventHandler? SelectionChanged;
    internal PatternListItem? SelectedItem => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;

    internal PatternListControl()
    {
        BackColor = AppTheme.Raised;
        ForeColor = AppTheme.Text;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    internal void SetItems(IEnumerable<PatternListItem> entries, string? selectPath)
    {
        items.Clear();
        items.AddRange(entries);
        selectedIndex = string.IsNullOrWhiteSpace(selectPath)
            ? -1
            : items.FindIndex(item => string.Equals(item.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        scrollOffset = Math.Clamp(scrollOffset, 0, MaximumScroll);
        Invalidate();
    }

    internal void SelectFirst()
    {
        if (items.Count == 0)
        {
            return;
        }

        selectedIndex = 0;
        EnsureSelectedVisible();
        Invalidate();
    }

    internal void ClearSelection()
    {
        selectedIndex = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SetClip(new Rectangle(0, 0, Width - 10, Height));
        if (items.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "NO PATTERNS YET\nRecord a workflow or add a preset.", new Font("Segoe UI", 9F), ClientRectangle, AppTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var y = index * ItemHeight - scrollOffset;
            if (y + ItemHeight < 0 || y > Height)
            {
                continue;
            }

            var selected = index == selectedIndex;
            using var fill = new SolidBrush(selected ? AppTheme.AccentDark : AppTheme.Raised);
            e.Graphics.FillRectangle(fill, 0, y, Width - 10, ItemHeight - 1);
            if (selected)
            {
                using var marker = new SolidBrush(AppTheme.Accent);
                e.Graphics.FillRectangle(marker, 0, y, 3, ItemHeight - 1);
            }

            var item = items[index];
            TextRenderer.DrawText(e.Graphics, item.Pattern.Name, new Font("Segoe UI Semibold", 9.5F), new Rectangle(12, y + 7, Width - 34, 20), selected ? AppTheme.Accent : AppTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var duration = TimeSpan.FromMilliseconds(item.Pattern.DurationMicroseconds / 1_000d);
            TextRenderer.DrawText(e.Graphics, $"{item.Pattern.Events.Count:N0} EVENTS   ·   {duration:mm\\:ss\\.fff}", new Font("Consolas", 8F), new Rectangle(12, y + 31, Width - 34, 18), AppTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        e.Graphics.ResetClip();
        DrawScrollbar(e.Graphics);
        using var border = new Pen(Focused ? AppTheme.Accent : AppTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    private void DrawScrollbar(Graphics graphics)
    {
        if (MaximumScroll <= 0)
        {
            return;
        }

        using var track = new SolidBrush(AppTheme.Surface);
        graphics.FillRectangle(track, Width - 7, 2, 5, Height - 4);
        var thumb = ThumbRectangle;
        using var brush = new SolidBrush(AppTheme.Border);
        graphics.FillRectangle(brush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (MaximumScroll > 0 && ThumbRectangle.Contains(e.Location))
        {
            draggingThumb = true;
            thumbDragOffset = e.Y - ThumbRectangle.Y;
        }
        else if (e.X < Width - 10)
        {
            var index = (e.Y + scrollOffset) / ItemHeight;
            if (index >= 0 && index < items.Count && index != selectedIndex)
            {
                selectedIndex = index;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingThumb)
        {
            var trackHeight = Height - 4;
            var thumbHeight = ThumbRectangle.Height;
            var thumbY = Math.Clamp(e.Y - thumbDragOffset - 2, 0, trackHeight - thumbHeight);
            scrollOffset = (int)Math.Round(thumbY / (double)Math.Max(1, trackHeight - thumbHeight) * MaximumScroll);
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        draggingThumb = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scrollOffset = Math.Clamp(scrollOffset - Math.Sign(e.Delta) * ItemHeight, 0, MaximumScroll);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (items.Count > 0 && e.KeyCode is Keys.Up or Keys.Down)
        {
            selectedIndex = Math.Clamp(selectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, items.Count - 1);
            EnsureSelectedVisible();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private int MaximumScroll => Math.Max(0, items.Count * ItemHeight - Height);

    private Rectangle ThumbRectangle
    {
        get
        {
            var trackHeight = Height - 4;
            var thumbHeight = Math.Max(28, (int)Math.Round(trackHeight * Math.Min(1d, Height / (double)Math.Max(Height, items.Count * ItemHeight))));
            var y = MaximumScroll == 0 ? 2 : 2 + (int)Math.Round(scrollOffset / (double)MaximumScroll * (trackHeight - thumbHeight));
            return new Rectangle(Width - 7, y, 5, thumbHeight);
        }
    }

    private void EnsureSelectedVisible()
    {
        var top = selectedIndex * ItemHeight;
        if (top < scrollOffset)
        {
            scrollOffset = top;
        }
        else if (top + ItemHeight > scrollOffset + Height)
        {
            scrollOffset = top + ItemHeight - Height;
        }
    }
}

internal sealed class PresetOptionControl : Control
{
    private bool hovered;

    internal WorkflowPreset Preset { get; }
    internal bool Selected { get; set; }
    internal event EventHandler? SelectedPreset;

    internal PresetOptionControl(WorkflowPreset preset)
    {
        Preset = preset;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var fill = new SolidBrush(hovered || Selected ? AppTheme.AccentDark : AppTheme.Raised);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        if (Selected)
        {
            using var marker = new SolidBrush(AppTheme.Accent);
            e.Graphics.FillRectangle(marker, 0, 0, 3, Height);
        }

        TextRenderer.DrawText(e.Graphics, Preset.Name, new Font("Segoe UI Semibold", 9F), new Rectangle(12, 5, Width - 46, 19), Selected ? AppTheme.Accent : AppTheme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, Preset.Description, new Font("Segoe UI", 7.8F), new Rectangle(12, 25, Width - 24, 34), AppTheme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (Selected)
        {
            TextRenderer.DrawText(e.Graphics, "✓", new Font("Segoe UI Semibold", 9F), new Rectangle(Width - 32, 7, 20, 20), AppTheme.Accent, TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        SelectedPreset?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            SelectedPreset?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
