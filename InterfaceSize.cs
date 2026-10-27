using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LightCapture;

internal static class UiSizing
{
    internal const int Minimum = 50, Maximum = 125, Default = 75;
    internal static int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);
    internal static float Toolbar(AppSettings settings, float dpi) => Math.Clamp(dpi, 1, 4) * settings.ToolbarSizePercent / 100f;
    internal static float Menu(AppSettings settings, float dpi) => Math.Clamp(dpi, 1, 4) * settings.TrayMenuSizePercent / 100f;
}

internal sealed class SizeSlider : Control
{
    int value = UiSizing.Default;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal int Value
    {
        get => value;
        set
        {
            int next = UiSizing.Clamp((int)Math.Round(value / 5.0) * 5);
            if (this.value == next) return;
            this.value = next; Invalidate(); ValueChanged?.Invoke();
        }
    }
    internal event Action? ValueChanged;
    internal SizeSlider(string name)
    {
        AccessibleName = name; AccessibleRole = AccessibleRole.Slider; TabStop = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    void SetFromPoint(int x)
    {
        float inset = 10 * UiScale, fraction = Math.Clamp((x - inset) / Math.Max(1, Width - inset * 2), 0, 1);
        Value = UiSizing.Minimum + (int)Math.Round(fraction * (UiSizing.Maximum - UiSizing.Minimum));
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromPoint(e.X); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (Capture && e.Button == MouseButtons.Left) SetFromPoint(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Capture) { SetFromPoint(e.X); Capture = false; }
        base.OnMouseUp(e);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: Value -= 5; break;
            case Keys.Right: case Keys.Up: Value += 5; break;
            case Keys.PageDown: Value -= 10; break;
            case Keys.PageUp: Value += 10; break;
            case Keys.Home: Value = UiSizing.Minimum; break;
            case Keys.End: Value = UiSizing.Maximum; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true; e.SuppressKeyPress = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        float start = 10 * UiScale, end = Width - start, y = Height / 2f;
        float thumb = start + (end - start) * (Value - UiSizing.Minimum) / (UiSizing.Maximum - UiSizing.Minimum);
        using var track = new Pen(UiShape.Border, 4 * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progress = new Pen(Ui.Blue, 4 * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(track, start, y, end, y); if (thumb > start) g.DrawLine(progress, start, y, thumb, y);
        if (Focused) { using var focus = new SolidBrush(UiShape.Tint); g.FillEllipse(focus, thumb - 13 * UiScale, y - 13 * UiScale, 26 * UiScale, 26 * UiScale); }
        g.FillEllipse(Brushes.White, thumb - 8 * UiScale, y - 8 * UiScale, 16 * UiScale, 16 * UiScale);
        using var outline = new Pen(Ui.Blue, 2 * UiScale); g.DrawEllipse(outline, thumb - 8 * UiScale, y - 8 * UiScale, 16 * UiScale, 16 * UiScale);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibility(this);
    sealed class SliderAccessibility(SizeSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value + "%"; set { if (int.TryParse(value?.TrimEnd('%'), out int next)) owner.Value = next; } }
    }
}
