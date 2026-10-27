using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LightCapture;

internal sealed class UiFonts : IDisposable
{
    readonly Dictionary<(float, FontStyle), Font> fonts = [];
    internal Font Get(float pixels, FontStyle style = FontStyle.Regular)
    {
        if (!fonts.TryGetValue((pixels, style), out var font))
            fonts.Add((pixels, style), font = new Font("Microsoft YaHei UI", pixels, style, GraphicsUnit.Pixel));
        return font;
    }
    public void Dispose() { foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); }
}

internal static class UiShape
{
    internal static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    internal static void Clip(Control control, float radius)
    {
        if (control.Width < 2 || control.Height < 2) return;
        using var path = Round(new(0, 0, control.Width, control.Height), radius);
        var old = control.Region; control.Region = new Region(path); old?.Dispose();
    }
    internal static readonly Color Border = Color.FromArgb(222, 229, 239);
    internal static readonly Color Muted = Color.FromArgb(111, 123, 143);
    internal static readonly Color Tint = Color.FromArgb(235, 242, 255);
}

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Field { get; set; }
    internal RoundedPanel()
    {
        BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ControlAdded += (_, e) => { if (e.Control != null) { e.Control.Enter += (_, _) => Invalidate(); e.Control.Leave += (_, _) => Invalidate(); } };
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Ui.Surface);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiShape.Round(new(.5f, .5f, Width - 1, Height - 1), (Field ? 10 : 16) * UiScale);
        using var fill = new SolidBrush(BackColor); e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Field && ContainsFocus ? Ui.Blue : UiShape.Border, Math.Max(1, UiScale));
        e.Graphics.DrawPath(pen, path); base.OnPaint(e);
    }
}

internal sealed class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Primary { get; set; }
    bool hover, down;
    internal ModernButton(string text, Action action)
    {
        Text = text; Cursor = Cursors.Hand; UseMnemonic = false; BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Click += (_, _) => action();
        MouseEnter += (_, _) => { hover = true; Invalidate(); };
        MouseLeave += (_, _) => { hover = false; down = false; Invalidate(); };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } };
        MouseUp += (_, _) => { down = false; Invalidate(); };
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Ui.Surface);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // ButtonBase marks the control Opaque, so WM_PAINT can skip
        // OnPaintBackground. Fill the corners before drawing the rounded face.
        g.Clear(Parent?.BackColor ?? Ui.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = Primary ? (down ? Color.FromArgb(25, 75, 180) : hover ? Color.FromArgb(34, 91, 206) : Ui.Blue)
            : down ? Color.FromArgb(226, 234, 248) : hover ? Color.FromArgb(242, 246, 253) : BackColor;
        if (!Enabled) fill = Color.FromArgb(238, 241, 246);
        using var path = UiShape.Round(new(.5f, .5f, Width - 1, Height - 1), 10 * UiScale);
        using var brush = new SolidBrush(fill); g.FillPath(brush, path);
        if (!Primary) { using var pen = new Pen(UiShape.Border, Math.Max(1, UiScale)); g.DrawPath(pen, path); }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, !Enabled ? UiShape.Muted : Primary ? Color.White : Ui.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class ModifierChip : CheckBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    internal ModifierChip(string text, bool value)
    {
        Text = text; Checked = value; Cursor = Cursors.Hand; AccessibleName = text + " 组合键";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckedChanged += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiShape.Round(new(.5f, .5f, Width - 1, Height - 1), 10 * UiScale);
        using var brush = new SolidBrush(Checked ? UiShape.Tint : Color.White); g.FillPath(brush, path);
        using var pen = new Pen(Checked ? Color.FromArgb(161, 190, 243) : UiShape.Border, Math.Max(1, UiScale)); g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Checked ? Ui.Blue : Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class ModernSwitch : CheckBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    internal ModernSwitch()
    {
        Cursor = Cursors.Hand; AccessibleName = "登录 Windows 后启动轻截";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckedChanged += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new SolidBrush(Checked ? Ui.Blue : Color.FromArgb(204, 214, 228));
        g.FillRoundedRectangle(track, new(1, 1, Width - 2, Height - 2), Height / 2f);
        float inset = 4 * UiScale, size = Height - inset * 2;
        g.FillEllipse(Brushes.White, Checked ? Width - size - inset : inset, inset, size, size);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}

internal sealed class PaintedComboBox : ComboBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    internal PaintedComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SelectedIndexChanged += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        var textBounds = new Rectangle(0, 0, Math.Max(1, Width - (int)(30 * UiScale)), Height);
        TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font, textBounds, Ui.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        float x = Width - 12 * UiScale, y = Height / 2f;
        using var pen = new Pen(UiShape.Muted, 1.7f * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new PointF[] { new(x - 4 * UiScale, y - 2 * UiScale), new(x, y + 2 * UiScale), new(x + 4 * UiScale, y - 2 * UiScale) });
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textBounds, -2, -2));
    }
}

// Retains the native combo box, its popup, keyboard handling and accessibility.
internal sealed class ChoiceField : RoundedPanel
{
    internal readonly PaintedComboBox Input = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, FlatStyle = FlatStyle.Flat, IntegralHeight = false, BackColor = Color.White };
    internal ChoiceField(string name)
    {
        Field = true; Input.AccessibleName = name; Controls.Add(Input);
        Input.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var brush = new SolidBrush(selected ? UiShape.Tint : Color.White); e.Graphics.FillRectangle(brush, e.Bounds);
            var rect = Rectangle.Inflate(e.Bounds, -4, 0);
            TextRenderer.DrawText(e.Graphics, Input.Items[e.Index]?.ToString(), Input.Font, rect, Ui.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        };
        Input.SelectedIndexChanged += (_, _) => Invalidate();
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { Input.Focus(); Input.DroppedDown = true; } };
    }
    internal void Arrange(float scale, Font font)
    {
        UiScale = scale; Input.UiScale = scale; Input.Font = font;
        Input.ItemHeight = (int)Math.Ceiling(26 * scale);
        Input.DropDownHeight = (int)Math.Ceiling(286 * scale); Input.DropDownWidth = Width;
        int inset = (int)Math.Ceiling(12 * scale);
        Input.SetBounds(inset, (Height - Input.PreferredHeight) / 2, Math.Max(24, Width - inset * 2), Input.PreferredHeight);
        Invalidate();
    }
}
