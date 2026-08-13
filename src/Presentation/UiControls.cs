using System.Drawing.Drawing2D;

namespace WorkflowLooper;

internal static class AppTheme
{
    internal static readonly Color Canvas = Color.FromArgb(7, 11, 13);
    internal static readonly Color Surface = Color.FromArgb(13, 20, 24);
    internal static readonly Color Raised = Color.FromArgb(18, 28, 33);
    internal static readonly Color Border = Color.FromArgb(43, 59, 67);
    internal static readonly Color Text = Color.FromArgb(237, 247, 244);
    internal static readonly Color Muted = Color.FromArgb(145, 165, 170);
    internal static readonly Color Mint = Color.FromArgb(72, 224, 181);
    internal static readonly Color MintDark = Color.FromArgb(20, 62, 52);
    internal static readonly Color MintWash = Color.FromArgb(16, 43, 39);
    internal static readonly Color Amber = Color.FromArgb(244, 185, 66);
    internal static readonly Color Coral = Color.FromArgb(255, 104, 104);
}

internal enum ButtonTone { Primary, Secondary, Danger, Ghost }
internal enum ConsoleButtonIcon { None, Play, Stop, Target, Settings }

internal enum WindowChromeKind { Minimize, Maximize, Close }

internal sealed class WindowChromeButton : Button
{
    private readonly WindowChromeKind kind;
    private bool hovered;
    private bool pressed;
    private bool isMaximized;

    internal bool IsMaximized
    {
        get => isMaximized;
        set { isMaximized = value; Invalidate(); }
    }

    internal WindowChromeButton(WindowChromeKind kind)
    {
        this.kind = kind;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        TabStop = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Default;
        AccessibleName = kind switch
        {
            WindowChromeKind.Minimize => "Minimize window",
            WindowChromeKind.Maximize => "Maximize or restore window",
            _ => "Close window",
        };
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var parentColor = Parent?.BackColor ?? AppTheme.Surface;
        var hoverColor = kind == WindowChromeKind.Close ? AppTheme.Coral : AppTheme.Raised;
        var fill = pressed ? ControlPaint.Dark(hoverColor, 0.12F) : hovered ? hoverColor : parentColor;
        var foreground = hovered && kind == WindowChromeKind.Close ? AppTheme.Canvas : AppTheme.Text;
        e.Graphics.Clear(fill);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = DeviceDpi / 96F;
        var centerX = ClientSize.Width / 2F;
        var centerY = ClientSize.Height / 2F;
        using var pen = new Pen(foreground, Math.Max(1.2F, 1.2F * scale))
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square,
            LineJoin = LineJoin.Miter,
        };
        using var border = new Pen(AppTheme.Border, Math.Max(1F, scale));
        e.Graphics.DrawRectangle(border, 0.5F * scale, 0.5F * scale, ClientSize.Width - scale, ClientSize.Height - scale);

        switch (kind)
        {
            case WindowChromeKind.Minimize:
                e.Graphics.DrawLine(pen, centerX - 5F * scale, centerY + 3.5F * scale, centerX + 5F * scale, centerY + 3.5F * scale);
                break;
            case WindowChromeKind.Maximize when isMaximized:
                e.Graphics.DrawRectangle(pen, centerX - 2.5F * scale, centerY - 5F * scale, 8F * scale, 8F * scale);
                e.Graphics.DrawRectangle(pen, centerX - 5.5F * scale, centerY - 2F * scale, 8F * scale, 8F * scale);
                break;
            case WindowChromeKind.Maximize:
                e.Graphics.DrawRectangle(pen, centerX - 5F * scale, centerY - 5F * scale, 10F * scale, 10F * scale);
                break;
            default:
                e.Graphics.DrawLine(pen, centerX - 4.5F * scale, centerY - 4.5F * scale, centerX + 4.5F * scale, centerY + 4.5F * scale);
                e.Graphics.DrawLine(pen, centerX + 4.5F * scale, centerY - 4.5F * scale, centerX - 4.5F * scale, centerY + 4.5F * scale);
                break;
        }
    }
}

internal sealed class ConsoleButton : Button
{
    private bool hovered;
    private bool pressed;
    private ButtonTone tone;

    internal Color? HoverBackColor { get; set; }
    internal Color? HoverForeColor { get; set; }
    internal ConsoleButtonIcon IconKind { get; set; }
    internal int CornerRadius { get; set; }

    internal ButtonTone Tone
    {
        get => tone;
        set { tone = value; Invalidate(); }
    }

    internal ConsoleButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 8F);
        Height = 44;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var parentColor = Parent?.BackColor ?? AppTheme.Canvas;
        e.Graphics.Clear(parentColor);
        var baseColor = Tone switch
        {
            ButtonTone.Primary => AppTheme.Mint,
            ButtonTone.Danger => AppTheme.Coral,
            ButtonTone.Ghost => parentColor,
            _ => AppTheme.Raised,
        };
        var fill = !Enabled
            ? AppTheme.Surface
            : pressed
                ? ControlPaint.Dark(baseColor, 0.08F)
                : hovered
                    ? HoverBackColor ?? (Tone == ButtonTone.Ghost ? AppTheme.Raised : ControlPaint.Light(baseColor, 0.08F))
                    : baseColor;
        var foreground = !Enabled
            ? AppTheme.Muted
            : hovered && HoverForeColor is not null
                ? HoverForeColor.Value
                : Tone is ButtonTone.Primary or ButtonTone.Danger ? AppTheme.Canvas : AppTheme.Text;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(ClientRectangle, CornerRadius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (Tone is ButtonTone.Secondary or ButtonTone.Ghost)
        {
            var borderBounds = ClientRectangle;
            borderBounds.Inflate(-1, -1);
            using var borderPath = Rounded(borderBounds, Math.Max(0, CornerRadius - 1));
            var showFocus = Focused && ShowFocusCues;
            using var border = new Pen(showFocus ? AppTheme.Mint : AppTheme.Border, showFocus ? 2 : 1);
            e.Graphics.DrawPath(border, borderPath);
        }
        else if (Focused && ShowFocusCues)
        {
            var focusBounds = ClientRectangle;
            focusBounds.Inflate(-3, -3);
            using var focusPath = Rounded(focusBounds, Math.Max(0, CornerRadius - 2));
            using var focus = new Pen(AppTheme.Text, 2);
            e.Graphics.DrawPath(focus, focusPath);
        }
        if (IconKind == ConsoleButtonIcon.None)
        {
            var textBounds = ClientRectangle;
            textBounds.Inflate(-4, 0);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            var measured = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            const int iconSize = 14;
            const int iconGap = 8;
            var groupWidth = iconSize + iconGap + measured.Width;
            var groupLeft = Math.Max(4, (Width - groupWidth) / 2);
            var iconBounds = new Rectangle(groupLeft, (Height - iconSize) / 2, iconSize, iconSize);
            DrawIcon(e.Graphics, iconBounds, foreground);
            var textBounds = new Rectangle(iconBounds.Right + iconGap, 0, Math.Max(0, Width - iconBounds.Right - iconGap - 4), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.2F) { LineJoin = LineJoin.Miter };
        var cx = bounds.Left + bounds.Width / 2F;
        var cy = bounds.Top + bounds.Height / 2F;
        switch (IconKind)
        {
            case ConsoleButtonIcon.Play:
                using (var play = new GraphicsPath())
                {
                    play.AddPolygon([new PointF(bounds.Left + 4, bounds.Top + 2), new PointF(bounds.Right - 2, cy), new PointF(bounds.Left + 4, bounds.Bottom - 2)]);
                    using var brush = new SolidBrush(color);
                    graphics.FillPath(brush, play);
                }
                break;
            case ConsoleButtonIcon.Stop:
                graphics.DrawRectangle(pen, bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);
                break;
            case ConsoleButtonIcon.Target:
                graphics.DrawEllipse(pen, bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, bounds.Height - 4);
                graphics.DrawLine(pen, cx, bounds.Top, cx, bounds.Bottom);
                graphics.DrawLine(pen, bounds.Left, cy, bounds.Right, cy);
                break;
            case ConsoleButtonIcon.Settings:
                using (var brush = new SolidBrush(color))
                for (var index = 0; index < 3; index++)
                {
                    var y = bounds.Top + 3 + index * 4;
                    graphics.DrawLine(pen, bounds.Left + 1, y, bounds.Right - 1, y);
                    var knobX = index == 1 ? bounds.Left + 4 : bounds.Left + 9;
                    graphics.FillEllipse(brush, knobX - 1.5F, y - 1.5F, 3, 3);
                }
                break;
        }
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        bounds.Width -= 1; bounds.Height -= 1;
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class HealthBadge : Label
{
    private Color accent = AppTheme.Muted;

    internal void Set(string text, Color color)
    {
        Text = $"●  {text}";
        accent = color;
        ForeColor = color;
        Invalidate();
    }

    internal HealthBadge()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
        Font = new Font("Segoe UI Semibold", 8.5F);
        ForeColor = accent;
    }
}

internal sealed class TargetBadge : Control
{
    private string displayText = "NO TARGET SELECTED";

    internal TargetBadge()
    {
        BackColor = AppTheme.Canvas;
        ForeColor = AppTheme.Text;
        Font = new Font("Consolas", 7F);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void SetValue(string value)
    {
        displayText = value;
        Text = value;
        AccessibleName = value;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(AppTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var showLock = displayText.Contains("TARGET LOCKED", StringComparison.Ordinal);
        var textLeft = showLock ? 24 : 10;
        if (showLock)
        {
            using var dot = new SolidBrush(AppTheme.Mint);
            e.Graphics.FillEllipse(dot, 10, Height / 2F - 3, 6, 6);
        }
        using var textBrush = new SolidBrush(ForeColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        e.Graphics.DrawString(displayText, Font, textBrush,
            new RectangleF(textLeft, 0, Math.Max(0, Width - textLeft - 8), Height), format);
    }
}

internal sealed class DimmingOverlay : Control
{
    internal DimmingOverlay()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var veil = new SolidBrush(Color.FromArgb(148, 2, 4, 5));
        e.Graphics.FillRectangle(veil, ClientRectangle);
    }
}

internal sealed class MetricCard : Panel
{
    private readonly Label caption = new();
    private readonly Label value = new();

    internal MetricCard(string title)
    {
        BackColor = AppTheme.Raised;
        Padding = new Padding(14, 10, 14, 10);
        caption.Text = title.ToUpperInvariant();
        caption.ForeColor = AppTheme.Muted;
        caption.Font = new Font("Segoe UI Semibold", 7.5F);
        caption.Dock = DockStyle.Top;
        caption.Height = 18;
        value.Text = "—";
        value.ForeColor = AppTheme.Text;
        value.Font = new Font("Segoe UI Semibold", 12F);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(value);
        Controls.Add(caption);
    }

    internal void SetValue(string text, Color? color = null)
    {
        value.Text = text;
        value.ForeColor = color ?? AppTheme.Text;
    }
}

internal sealed class TelemetryReadout : Control
{
    private readonly Font captionFont = new("Consolas", 7.5F, FontStyle.Bold);
    private readonly Font valueFont = new("Consolas", 9.5F, FontStyle.Bold);
    private string value = "—";
    private Color valueColor = AppTheme.Text;

    internal string Caption { get; }
    internal string ValueForTest => value;
    internal Color ValueColorForTest => valueColor;

    internal TelemetryReadout(string caption)
    {
        Caption = caption;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Canvas;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void SetValue(string text, Color? color = null)
    {
        value = text;
        valueColor = color ?? AppTheme.Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var content = new Rectangle(14, 5, Math.Max(0, Width - 28), Math.Max(0, Height - 10));
        TextRenderer.DrawText(e.Graphics, Caption, captionFont, new Rectangle(content.X, content.Y, content.Width, 17), AppTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, value, valueFont, new Rectangle(content.X, content.Y + 19, content.Width, Math.Max(0, content.Height - 19)), valueColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            captionFont.Dispose();
            valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class NumericSetting : UserControl
{
    private readonly TextBox input = new();
    private readonly decimal minimum;
    private readonly decimal maximum;
    private readonly decimal increment;
    private decimal numericValue;

    internal decimal Value
    {
        get => numericValue;
        set
        {
            numericValue = Math.Clamp(value, minimum, maximum);
            input.Text = decimal.Truncate(numericValue).ToString();
        }
    }

    internal NumericSetting(string title, decimal minimum, decimal maximum, decimal value, decimal increment, string suffix = "")
    {
        this.minimum = minimum;
        this.maximum = maximum;
        this.increment = increment;
        Height = 64;
        BackColor = AppTheme.Surface;
        var label = new Label
        {
            Text = title.ToUpperInvariant(), ForeColor = AppTheme.Muted, Font = new Font("Consolas", 7.5F, FontStyle.Bold),
            Dock = DockStyle.Top, Height = 21,
        };
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = AppTheme.Raised };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, string.IsNullOrWhiteSpace(suffix) ? 0 : 30));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        input.BackColor = AppTheme.Raised;
        input.ForeColor = AppTheme.Text;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = new Font("Segoe UI Semibold", 9.5F);
        input.Dock = DockStyle.Fill;
        input.TextAlign = HorizontalAlignment.Left;
        input.AccessibleName = title;
        input.AccessibleDescription = $"Value from {minimum:0} to {maximum:0}{suffix}.";
        input.Leave += (_, _) => CommitText();
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Up) { Value += this.increment; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Down) { Value -= this.increment; e.SuppressKeyPress = true; }
        };
        var unit = new Label
        {
            Text = suffix.ToUpperInvariant(),
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Mint,
            Font = new Font("Consolas", 7.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var minus = new ConsoleButton { Text = "−", Tone = ButtonTone.Ghost, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var plus = new ConsoleButton { Text = "+", Tone = ButtonTone.Ghost, Dock = DockStyle.Fill, Margin = Padding.Empty };
        minus.AccessibleName = $"Decrease {title}";
        plus.AccessibleName = $"Increase {title}";
        minus.Click += (_, _) => Value -= this.increment;
        plus.Click += (_, _) => Value += this.increment;
        editor.Controls.Add(input, 0, 0); editor.Controls.Add(unit, 1, 0); editor.Controls.Add(minus, 2, 0); editor.Controls.Add(plus, 3, 0);
        Controls.Add(editor);
        Controls.Add(label);
        Value = value;
    }

    internal void FocusInput() => input.Focus();
    internal bool InputFocusedForTest => input.Focused;

    private void CommitText() => Value = decimal.TryParse(input.Text, out var parsed) ? parsed : numericValue;
}

internal sealed class ConsoleChoice : Control
{
    private readonly List<string> items = [];
    private int selectedIndex = -1;
    private bool hovered;

    internal IList<string> Items => items;
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = items.Count == 0 ? -1 : Math.Clamp(value, 0, items.Count - 1);
            Invalidate();
        }
    }

    internal ConsoleChoice()
    {
        BackColor = AppTheme.Raised;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI Semibold", 9F);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void AddRange(IEnumerable<string> values)
    {
        items.AddRange(values);
        if (selectedIndex < 0 && items.Count > 0) selectedIndex = 0;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); ShowChoices(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { ShowChoices(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(hovered ? Color.FromArgb(27, 37, 42) : AppTheme.Raised);
        var showFocus = Focused && ShowFocusCues;
        using var border = new Pen(showFocus ? AppTheme.Mint : AppTheme.Border, showFocus ? 2 : 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var text = selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : "SELECT";
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(10, 0, Math.Max(0, Width - 38), Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var center = Height / 2;
        using var chevron = new Pen(AppTheme.Muted, 1.5F);
        e.Graphics.DrawLine(chevron, Width - 23, center - 2, Width - 19, center + 2);
        e.Graphics.DrawLine(chevron, Width - 19, center + 2, Width - 15, center - 2);
    }

    private void ShowChoices()
    {
        if (items.Count == 0) return;
        var menu = new ContextMenuStrip
        {
            AutoSize = false,
            Width = Width,
            Height = items.Count * 34 + 4,
            BackColor = AppTheme.Raised,
            ForeColor = AppTheme.Text,
            ShowImageMargin = false,
        };
        for (var index = 0; index < items.Count; index++)
        {
            var itemIndex = index;
            var item = new ToolStripMenuItem(items[index])
            {
                AutoSize = false,
                Width = Width - 4,
                Height = 34,
                BackColor = index == selectedIndex ? AppTheme.MintDark : AppTheme.Raised,
                ForeColor = AppTheme.Text,
                Font = Font,
            };
            item.Click += (_, _) => SelectedIndex = itemIndex;
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this, new Point(0, Height));
    }
}
