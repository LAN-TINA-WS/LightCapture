using System.Drawing.Drawing2D;

namespace LightCapture;

internal enum TrayCommand { Capture, LongCapture, Ocr, Translate, Record, OpenImage, Settings, Help, Exit }
internal sealed record TrayItemVisual(ToolbarGlyph Glyph, bool Danger = false);

internal sealed class ModernTrayMenu : ContextMenuStrip
{
    readonly UiFonts fonts = new();
    readonly AppSettings settings;
    readonly ToolStripLabel heading = new("轻截 · 截图工具") { AutoSize = false };
    internal readonly List<ToolStripMenuItem> Commands = [];
    internal float UiScale { get; private set; } = 1;
    internal Font HeadingFont => fonts.Get(16 * UiScale, FontStyle.Bold);
    internal Font SmallFont => fonts.Get(12 * UiScale);
    internal string Shortcut => settings.HotkeyText.Replace("+", " + ");
    protected override Padding DefaultPadding => new((int)Math.Ceiling(8 * Math.Clamp(UiScale, .5f, 5)));
    internal ModernTrayMenu(AppSettings settings, Action<TrayCommand> action)
    {
        this.settings = settings; BackColor = Color.White; ForeColor = Ui.Ink;
        AutoSize = false; LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
        ShowImageMargin = false; ShowCheckMargin = false; DropShadowEnabled = true;
        Renderer = new TrayMenuRenderer(this); Items.Add(heading);
        void Add(TrayCommand command, string text, ToolbarGlyph glyph, bool danger = false)
        {
            var item = new ToolStripMenuItem(text) { AutoSize = false, Tag = new TrayItemVisual(glyph, danger), AccessibleName = text };
            item.Click += (_, _) => action(command); Commands.Add(item); Items.Add(item);
        }
        Add(TrayCommand.Capture, "屏幕截图", ToolbarGlyph.Select);
        Add(TrayCommand.LongCapture, "滚动长截图", ToolbarGlyph.LongCapture);
        Add(TrayCommand.Ocr, "识别文字", ToolbarGlyph.Ocr);
        Add(TrayCommand.Translate, "屏幕翻译", ToolbarGlyph.Translate);
        Add(TrayCommand.Record, "区域录屏", ToolbarGlyph.Record);
        Items.Add(new ToolStripSeparator());
        Add(TrayCommand.OpenImage, "打开图片编辑", ToolbarGlyph.Image);
        Add(TrayCommand.Settings, "偏好设置", ToolbarGlyph.Settings);
        Add(TrayCommand.Help, "使用说明", ToolbarGlyph.Help);
        Items.Add(new ToolStripSeparator());
        Add(TrayCommand.Exit, "退出轻截", ToolbarGlyph.Exit, true);
        Opening += (_, _) => Configure(Native.DpiScaleAt(Cursor.Position), Screen.FromPoint(Cursor.Position).WorkingArea.Height - 16);
        Configure(1, 1000);
    }
    internal void Configure(float scale, int availableHeight)
    {
        UiScale = Math.Min(UiSizing.Menu(settings, scale), Math.Max(.5f, (availableHeight - 8) / 492f));
        int S(float v) => (int)Math.Ceiling(v * UiScale);
        Font = fonts.Get(15 * UiScale);
        Padding = new Padding(S(8), S(8), S(8), S(8));
        heading.Size = new(S(300), S(58)); heading.Margin = Padding.Empty;
        int rowHeight = Math.Min(S(44), Math.Max(Font.Height + S(8), (availableHeight - heading.Height - S(36)) / Commands.Count));
        foreach (var item in Commands) { item.Font = Font; item.Size = new(S(316), rowHeight); item.Margin = Padding.Empty; item.Padding = Padding.Empty; }
        Commands[0].ToolTipText = settings.HotkeyText;
        foreach (var separator in Items.OfType<ToolStripSeparator>()) { separator.AutoSize = false; separator.Size = new(S(300), S(9)); separator.Margin = Padding.Empty; }
        Size = new(S(316), Padding.Vertical + Items.Cast<ToolStripItem>().Sum(item => item.Height));
        PerformLayout(); UiShape.Clip(this, S(16)); Invalidate();
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); UiShape.Clip(this, 16 * UiScale);
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) fonts.Dispose(); }

    // Also used for deterministic previews without opening a desktop popup.
    internal void PaintItem(Graphics graphics, ToolStripItem item, Rectangle bounds, bool selected)
    {
        float s = UiScale; graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (item == heading)
        {
            TextRenderer.DrawText(graphics, "轻截", HeadingFont, new Rectangle(bounds.X + (int)(16 * s), bounds.Y + (int)(7 * s), bounds.Width - (int)(32 * s), (int)(25 * s)), Ui.Ink, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(graphics, "截图工具 · 后台常驻", SmallFont, new Rectangle(bounds.X + (int)(16 * s), bounds.Y + (int)(34 * s), bounds.Width - (int)(32 * s), (int)(19 * s)), UiShape.Muted, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return;
        }
        if (item is ToolStripSeparator)
        {
            using var pen = new Pen(UiShape.Border); graphics.DrawLine(pen, bounds.Left + 14 * s, bounds.Top + bounds.Height / 2f, bounds.Right - 14 * s, bounds.Top + bounds.Height / 2f); return;
        }
        if (item.Tag is not TrayItemVisual visual) return;
        if (selected)
        {
            using var path = UiShape.Round(new(bounds.X + 8 * s, bounds.Y + s, bounds.Width - 16 * s, bounds.Height - 2 * s), 10 * s);
            using var brush = new SolidBrush(visual.Danger ? Color.FromArgb(255, 239, 241) : UiShape.Tint); graphics.FillPath(brush, path);
        }
        Color ink = visual.Danger ? Color.FromArgb(204, 66, 83) : selected ? Ui.Blue : Ui.Ink;
        ToolbarIcons.Draw(graphics, visual.Glyph, new(bounds.X + 24 * s, bounds.Y + (bounds.Height - 22 * s) / 2, 22 * s, 22 * s), ink);
        var textBounds = new Rectangle(bounds.X + (int)(62 * s), bounds.Y, bounds.Width - (int)(86 * s), bounds.Height);
        if (item == Commands[0])
        {
            int hintWidth = Math.Min((int)(140 * s), TextRenderer.MeasureText(graphics, Shortcut, SmallFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
            var hintRect = new Rectangle(bounds.Right - (int)(24 * s) - hintWidth, bounds.Y, hintWidth, bounds.Height);
            TextRenderer.DrawText(graphics, Shortcut, SmallFont, hintRect, UiShape.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            textBounds.Width = hintRect.Left - textBounds.Left - (int)(12 * s);
        }
        TextRenderer.DrawText(graphics, item.Text, Font, textBounds, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class TrayMenuRenderer(ModernTrayMenu menu) : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Color.White);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiShape.Round(new(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 16 * menu.UiScale);
        using var pen = new Pen(UiShape.Border, Math.Max(1, menu.UiScale)); e.Graphics.DrawPath(pen, path);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => menu.PaintItem(e.Graphics, e.Item, new(Point.Empty, e.Item.Size), e.Item.Selected);
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripLabel) menu.PaintItem(e.Graphics, e.Item, new(Point.Empty, e.Item.Size), false);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) => menu.PaintItem(e.Graphics, e.Item, new(Point.Empty, e.Item.Size), false);
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
}
