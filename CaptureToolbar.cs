using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace LightCapture;

internal enum ToolbarCommand
{
    Undo, Redo, Save, Pin, LongCapture, Ocr, Translate, Record, Cancel, Finish, Reset, ZoomOut, ZoomIn
}

internal enum ToolbarGlyph
{
    Select, Rectangle, Ellipse, Arrow, Brush, Text, Marker, Mosaic, Polygon,
    Undo, Redo, Save, Pin, LongCapture, Ocr, Translate, Record, Close, Check, Palette, Reset, ZoomOut, ZoomIn,
    Image, Settings, Help, Exit
}

internal sealed class ToolbarIconButton : Button
{
    public ToolbarGlyph Glyph { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal string Caption { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Color? Accent { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    bool hover;

    public ToolbarIconButton(ToolbarGlyph glyph, string name, Action click)
    {
        Glyph = glyph;
        AccessibleName = name;
        Name = "toolbar-" + glyph;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Click += (_, _) => click();
        MouseEnter += (_, _) => { hover = true; Invalidate(); };
        MouseLeave += (_, _) => { hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        Color fill = Primary ? (hover ? Color.FromArgb(29, 85, 193) : Ui.Blue)
            : Selected ? Color.FromArgb(228, 238, 255) : hover ? Color.FromArgb(239, 244, 251) : Color.White;
        using var background = new SolidBrush(fill);
        var rect = new RectangleF(1, 1, Width - 2, Height - 2);
        g.FillRoundedRectangle(background, rect, 10 * UiScale);
        if (Selected)
        {
            using var outline = new Pen(Color.FromArgb(151, 184, 241), Math.Max(1, UiScale));
            using var path = RoundedPath(rect, 10 * UiScale);
            g.DrawPath(outline, path);
        }
        Color ink = Primary ? Color.White : Selected ? Ui.Blue : Ui.Ink;
        float iconSize = 24 * UiScale;
        float iconX = Caption.Length == 0 ? (Width - iconSize) / 2 : 13 * UiScale;
        float iconY = (Height - iconSize) / 2;
        ToolbarIcons.Draw(g, Glyph, new RectangleF(iconX, iconY, iconSize, iconSize), ink);
        if (Caption.Length != 0)
        {
            using var font = new Font("Microsoft YaHei UI", 14 * UiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            var textRect = new Rectangle((int)(iconX + iconSize + 7 * UiScale), 0, Width - (int)(iconX + iconSize + 14 * UiScale), Height);
            TextRenderer.DrawText(g, Caption, font, textRect, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        if (Accent is Color color)
        {
            using var accent = new SolidBrush(color);
            g.FillRoundedRectangle(accent, new RectangleF(Width / 2f - 10 * UiScale, Height - 8 * UiScale, 20 * UiScale, 4 * UiScale), 2 * UiScale);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
    }

    static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class ToolbarIcons
{
    public static void Draw(Graphics graphics, ToolbarGlyph icon, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        void Line(float x1, float y1, float x2, float y2) => graphics.DrawLine(pen, x1, y1, x2, y2);
        void Path(params PointF[] points) => graphics.DrawLines(pen, points);
        void Letter(string text, float size, RectangleF rect)
        {
            using var font = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, brush, rect, format);
        }
        switch (icon)
        {
            case ToolbarGlyph.Select:
                Path(new(5, 3), new(6, 20), new(10, 15), new(14, 21), new(17, 19), new(13, 13), new(20, 12), new(5, 3));
                break;
            case ToolbarGlyph.Rectangle:
                graphics.DrawRectangle(pen, 4, 5, 16, 14);
                break;
            case ToolbarGlyph.Ellipse:
                graphics.DrawEllipse(pen, 3.5f, 5, 17, 14);
                break;
            case ToolbarGlyph.Arrow:
                Line(5, 19, 19, 5); Path(new(10, 5), new(19, 5), new(19, 14));
                break;
            case ToolbarGlyph.Brush:
                Path(new(8, 14), new(17, 4), new(21, 8), new(11, 17), new(8, 14));
                graphics.DrawBezier(pen, new(8, 15), new(1, 15), new(8, 22), new(3, 21));
                graphics.DrawBezier(pen, new(3, 21), new(11, 23), new(12, 18), new(10, 17));
                break;
            case ToolbarGlyph.Text:
                Line(5, 5, 19, 5); Line(5, 5, 5, 8); Line(19, 5, 19, 8); Line(12, 5, 12, 20); Line(8, 20, 16, 20);
                break;
            case ToolbarGlyph.Marker:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); Letter("1", 13, new(3, 2.5f, 18, 18));
                break;
            case ToolbarGlyph.Mosaic:
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        if ((x + y) % 2 == 0) graphics.FillRectangle(brush, 3 + x * 6.5f, 3 + y * 6.5f, 5, 5);
                        else graphics.DrawRectangle(pen, 3 + x * 6.5f, 3 + y * 6.5f, 5, 5);
                    }
                break;
            case ToolbarGlyph.Polygon:
                Path(new(5, 5), new(18, 3), new(21, 14), new(11, 21), new(3, 15), new(5, 5));
                foreach (var point in new PointF[] { new(5, 5), new(18, 3), new(21, 14), new(11, 21), new(3, 15) }) graphics.FillEllipse(brush, point.X - 1.6f, point.Y - 1.6f, 3.2f, 3.2f);
                break;
            case ToolbarGlyph.Undo:
                Path(new(8, 5), new(3, 10), new(8, 15));
                graphics.DrawBezier(pen, new(3, 10), new(20, 5), new(24, 19), new(12, 20));
                break;
            case ToolbarGlyph.Redo:
                Path(new(16, 5), new(21, 10), new(16, 15));
                graphics.DrawBezier(pen, new(21, 10), new(4, 5), new(0, 19), new(12, 20));
                break;
            case ToolbarGlyph.Save:
                Path(new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(3, 21), new(3, 4), new(4, 3));
                graphics.DrawRectangle(pen, 7, 3, 9, 6); graphics.DrawRectangle(pen, 7, 14, 10, 7);
                break;
            case ToolbarGlyph.Pin:
                Path(new(9, 3), new(19, 8), new(16, 10), new(16, 16), new(13, 18), new(6, 11), new(8, 8), new(9, 3)); Line(9, 15, 4, 21);
                break;
            case ToolbarGlyph.LongCapture:
                Path(new(8, 3), new(4, 3), new(4, 21), new(20, 21), new(20, 3), new(16, 3)); Line(12, 3, 12, 16); Path(new(8, 12), new(12, 16), new(16, 12));
                break;
            case ToolbarGlyph.Ocr:
                Path(new(8, 3), new(3, 3), new(3, 8)); Path(new(16, 3), new(21, 3), new(21, 8)); Path(new(3, 16), new(3, 21), new(8, 21)); Path(new(16, 21), new(21, 21), new(21, 16));
                Path(new(7, 17), new(12, 7), new(17, 17)); Line(9, 13, 15, 13);
                break;
            case ToolbarGlyph.Translate:
                Letter("文", 12, new(1, 0, 14, 17)); Letter("A", 12, new(10, 10, 14, 14));
                break;
            case ToolbarGlyph.Record:
                graphics.DrawRectangle(pen, 3, 5, 14, 14); Path(new(17, 10), new(22, 7), new(22, 17), new(17, 14)); graphics.FillEllipse(brush, 7, 9, 6, 6);
                break;
            case ToolbarGlyph.Close:
                Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                break;
            case ToolbarGlyph.Check:
                Path(new(4, 12), new(10, 18), new(21, 6));
                break;
            case ToolbarGlyph.Palette:
                graphics.DrawBezier(pen, new(13, 21), new(-1, 24), new(-3, 0), new(13, 3));
                graphics.DrawBezier(pen, new(13, 3), new(25, 3), new(24, 14), new(16, 13));
                graphics.DrawBezier(pen, new(16, 13), new(9, 12), new(20, 20), new(13, 21));
                graphics.FillEllipse(brush, 6, 7, 2.5f, 2.5f); graphics.FillEllipse(brush, 12, 6, 2.5f, 2.5f); graphics.FillEllipse(brush, 17, 8, 2.5f, 2.5f);
                break;
            case ToolbarGlyph.Reset:
                Path(new(8, 4), new(3, 4), new(3, 9)); graphics.DrawArc(pen, 3, 3, 18, 18, 205, 290); Line(3, 4, 9, 10);
                break;
            case ToolbarGlyph.ZoomIn:
            case ToolbarGlyph.ZoomOut:
                graphics.DrawEllipse(pen, 3, 3, 13, 13); Line(15, 15, 21, 21); Line(6.5f, 9.5f, 12.5f, 9.5f); if (icon == ToolbarGlyph.ZoomIn) Line(9.5f, 6.5f, 9.5f, 12.5f);
                break;
            case ToolbarGlyph.Image:
                graphics.DrawRectangle(pen, 3, 4, 18, 16); graphics.DrawEllipse(pen, 7, 7, 3, 3);
                Path(new(4, 18), new(10, 12), new(14, 16), new(17, 12), new(21, 17));
                break;
            case ToolbarGlyph.Settings:
                graphics.DrawEllipse(pen, 7, 7, 10, 10);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    Line(12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10);
                }
                graphics.DrawEllipse(pen, 10, 10, 4, 4); break;
            case ToolbarGlyph.Help:
                graphics.DrawEllipse(pen, 3, 3, 18, 18);
                graphics.DrawArc(pen, 9, 6.5f, 6, 6, 190, 260); Line(12, 12, 12, 14);
                graphics.FillEllipse(brush, 11, 17, 2, 2); break;
            case ToolbarGlyph.Exit:
                Path(new(10, 4), new(4, 4), new(4, 20), new(10, 20)); Line(10, 12, 21, 12);
                Path(new(17, 8), new(21, 12), new(17, 16)); break;
        }
        graphics.Restore(state);
    }
}

internal sealed class ColorSwatch : Button
{
    public Color ColorValue { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    public ColorSwatch(Color color, Action click)
    {
        ColorValue = color; AccessibleName = "颜色 " + ColorTranslator.ToHtml(color); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Click += (_, _) => click();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
        float inset = 3 * UiScale;
        using var brush = new SolidBrush(ColorValue);
        g.FillRoundedRectangle(brush, new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2), 3 * UiScale);
        using var pen = new Pen(Selected ? Ui.Blue : Color.FromArgb(208, 215, 225), Math.Max(1, Selected ? 1.6f * UiScale : UiScale));
        g.DrawRectangle(pen, Selected ? 1 : inset, Selected ? 1 : inset, Selected ? Width - 3 : Width - inset * 2, Selected ? Height - 3 : Height - inset * 2);
    }
}

internal sealed class CaptureToolbar : UserControl
{
    readonly AppSettings settings;
    readonly bool imageEditor;
    readonly List<Control> commands = [];
    readonly List<Control> parameterGroups = [];
    readonly List<Panel> separators = [];
    readonly Dictionary<DrawingTool, ToolbarIconButton> tools = [];
    readonly List<ColorSwatch> swatches = [];
    readonly Dictionary<Control, Font> ownedFonts = [];
    readonly ToolTip tips = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 6000 };
    readonly Panel palette = new();
    readonly Panel brushModes = new();
    readonly ToolbarIconButton normalBrush, mosaicBrush;
    readonly Panel widthGroup = new(), fontGroup = new();
    readonly Panel mosaicBrushGroup = new(), mosaicBlockGroup = new();
    readonly Label mosaicBrushLabel = new() { Text = "笔刷大小", TextAlign = ContentAlignment.MiddleLeft }, mosaicBlockLabel = new() { Text = "颗粒大小", TextAlign = ContentAlignment.MiddleLeft };
    readonly Label widthLabel = new() { Text = "粗细", TextAlign = ContentAlignment.MiddleLeft }, fontLabel = new() { Text = "字号", TextAlign = ContentAlignment.MiddleLeft };
    readonly NumericUpDown stroke, fontSize;
    internal readonly NumericUpDown MosaicBrushInput, MosaicBlockInput;
    readonly ToolbarIconButton colorPicker;
    DrawingTool selectedTool = DrawingTool.Select, brushMode = DrawingTool.Brush;
    float scale = 1;
    float screenDpiScale = 1;
    int dividerY;
    int previousMaxWidth = -1;
    int availableWidth = 1280;
    bool layingOut;
    public event Action<DrawingTool>? ToolSelected;
    public event Action<ToolbarCommand>? CommandSelected;
    public event Action? OptionsChanging;
    public event Action? OptionsChanged;
    public bool EditingParameter => stroke.ContainsFocus || fontSize.ContainsFocus || MosaicBrushInput.ContainsFocus || MosaicBlockInput.ContainsFocus;
    internal IEnumerable<ToolbarIconButton> IconButtons => commands.OfType<ToolbarIconButton>();
    internal IEnumerable<NumericUpDown> NumberInputs => [stroke, fontSize, MosaicBrushInput, MosaicBlockInput];
    internal IEnumerable<ToolbarIconButton> BrushModeButtons => [normalBrush, mosaicBrush];
    internal bool PaletteVisible => palette.Visible;
    internal bool BrushModesVisible => brushModes.Visible;
    internal float UiScale => scale;

    public CaptureToolbar(AppSettings settings, bool imageEditor = false)
    {
        this.settings = settings; this.imageEditor = imageEditor;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        DoubleBuffered = true;
        Name = "CaptureToolbar";
        AccessibleName = "截图图标工具栏";
        foreach (var (tool, glyph, name, hint) in new[] {
            (DrawingTool.Select,ToolbarGlyph.Select,"选择","移动选区或标注；拖动对象右下角缩放"),
            (DrawingTool.Rectangle,ToolbarGlyph.Rectangle,"矩形","Shift：正方形"),
            (DrawingTool.Ellipse,ToolbarGlyph.Ellipse,"椭圆","Shift：正圆"),
            (DrawingTool.Arrow,ToolbarGlyph.Arrow,"箭头","Shift：约束方向"),
            (DrawingTool.Brush,ToolbarGlyph.Brush,"画笔","普通 / 马赛克；选择后展开画笔参数"),
            (DrawingTool.Text,ToolbarGlyph.Text,"文字","多行文字；Ctrl+Enter 提交"),
            (DrawingTool.Marker,ToolbarGlyph.Marker,"序号笔","自动编号"),
            (DrawingTool.Polygon,ToolbarGlyph.Polygon,"自由选区","左键添加点；双击闭合") })
        {
            if (imageEditor && tool == DrawingTool.Polygon) continue;
            var button = AddButton(glyph, name, () => RequestTool(tool == DrawingTool.Brush ? brushMode : tool), name + " · " + hint);
            tools.Add(tool, button);
        }
        AddSeparator();
        AddCommand(ToolbarCommand.Undo, ToolbarGlyph.Undo, "撤销", "Ctrl+Z");
        AddCommand(ToolbarCommand.Redo, ToolbarGlyph.Redo, "重做", "Ctrl+Y");
        if (!imageEditor) AddCommand(ToolbarCommand.Reset, ToolbarGlyph.Reset, "重新选择区域", "清除当前选区，重新框选");
        AddSeparator();
        AddCommand(ToolbarCommand.Save, ToolbarGlyph.Save, "保存", "Ctrl+S");
        AddCommand(ToolbarCommand.Pin, ToolbarGlyph.Pin, "钉在桌面", "Ctrl+T");
        if (!imageEditor) AddCommand(ToolbarCommand.LongCapture, ToolbarGlyph.LongCapture, "长截图", "滚动捕获与拼接");
        AddCommand(ToolbarCommand.Ocr, ToolbarGlyph.Ocr, "识别文字", "本机 OCR");
        if (imageEditor)
        {
            AddCommand(ToolbarCommand.ZoomOut, ToolbarGlyph.ZoomOut, "缩小", "缩小查看");
            AddCommand(ToolbarCommand.ZoomIn, ToolbarGlyph.ZoomIn, "放大", "放大查看");
        }
        else
        {
            AddCommand(ToolbarCommand.Translate, ToolbarGlyph.Translate, "翻译", "识别后翻译文字");
            AddCommand(ToolbarCommand.Record, ToolbarGlyph.Record, "录屏", "录制当前区域");
            AddSeparator();
            AddCommand(ToolbarCommand.Cancel, ToolbarGlyph.Close, "退出截图", "右键 / Esc");
        }
        var finish = AddButton(ToolbarGlyph.Check, imageEditor ? "复制图片" : "完成截图", () => CommandSelected?.Invoke(ToolbarCommand.Finish), "复制图片 · Ctrl+Enter");
        finish.Primary = true; finish.Caption = imageEditor ? "复制" : "完成";

        normalBrush = new(ToolbarGlyph.Brush, "普通画笔", () => RequestTool(DrawingTool.Brush)) { Caption = "普通" };
        mosaicBrush = new(ToolbarGlyph.Mosaic, "马赛克画笔", () => RequestTool(DrawingTool.Mosaic)) { Caption = "马赛克" };
        brushModes.Controls.AddRange([normalBrush, mosaicBrush]);
        tips.SetToolTip(normalBrush, "普通画笔 · 使用所选颜色绘制");
        tips.SetToolTip(mosaicBrush, "马赛克画笔 · 笔刷大小控制范围，颗粒大小控制码块");

        colorPicker = new(ToolbarGlyph.Palette, "自定义颜色", PickColor) { Accent = Color.FromArgb(settings.ColorArgb) };
        tips.SetToolTip(colorPicker, "自定义颜色"); palette.Controls.Add(colorPicker);
        foreach (var color in Ui.Palette)
        {
            var button = new ColorSwatch(color, () => ChangeColor(color)); swatches.Add(button); palette.Controls.Add(button); tips.SetToolTip(button, ColorTranslator.ToHtml(color));
        }
        stroke = new() { Minimum = 1, Maximum = 20, Value = (decimal)settings.StrokeWidth, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, AccessibleName = "画笔粗细" };
        fontSize = new() { Minimum = 10, Maximum = 96, Value = (decimal)settings.FontSize, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, AccessibleName = "文字字号" };
        MosaicBrushInput = new() { Minimum = 4, Maximum = 240, Value = settings.MosaicBrushSize, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, AccessibleName = "马赛克笔刷大小" };
        MosaicBlockInput = new() { Minimum = 2, Maximum = 60, Value = settings.MosaicBlockSize, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, AccessibleName = "马赛克颗粒大小" };
        tips.SetToolTip(MosaicBrushInput, "涂抹直径（截图像素），不会改变颗粒大小");
        tips.SetToolTip(MosaicBlockInput, "码块边长（截图像素），数值越大颗粒越粗");
        stroke.ValueChanged += (_, _) => { OptionsChanging?.Invoke(); settings.StrokeWidth = (float)stroke.Value; OptionsChanged?.Invoke(); };
        fontSize.ValueChanged += (_, _) => { OptionsChanging?.Invoke(); settings.FontSize = (float)fontSize.Value; OptionsChanged?.Invoke(); };
        MosaicBrushInput.ValueChanged += (_, _) => { OptionsChanging?.Invoke(); settings.MosaicBrushSize = (int)MosaicBrushInput.Value; OptionsChanged?.Invoke(); };
        MosaicBlockInput.ValueChanged += (_, _) => { OptionsChanging?.Invoke(); settings.MosaicBlockSize = (int)MosaicBlockInput.Value; OptionsChanged?.Invoke(); };
        widthGroup.Controls.AddRange([widthLabel, stroke]); fontGroup.Controls.AddRange([fontLabel, fontSize]);
        mosaicBrushGroup.Controls.AddRange([mosaicBrushLabel, MosaicBrushInput]); mosaicBlockGroup.Controls.AddRange([mosaicBlockLabel, MosaicBlockInput]);
        parameterGroups.AddRange([brushModes, palette, widthGroup, fontGroup, mosaicBrushGroup, mosaicBlockGroup]);
        foreach (var group in parameterGroups) Controls.Add(group);
        UpdateColorState(); SetSelectedTool(DrawingTool.Select); Configure(1, 1280);
    }

    ToolbarIconButton AddButton(ToolbarGlyph icon, string name, Action click, string hint)
    {
        var button = new ToolbarIconButton(icon, name, click); commands.Add(button); Controls.Add(button); tips.SetToolTip(button, hint); return button;
    }
    void AddCommand(ToolbarCommand command, ToolbarGlyph icon, string name, string hint) => AddButton(icon, name, () => CommandSelected?.Invoke(command), name + " · " + hint);
    void AddSeparator()
    {
        var separator = new Panel { BackColor = Color.FromArgb(221, 228, 237) }; separators.Add(separator); commands.Add(separator); Controls.Add(separator);
    }
    void PickColor()
    {
        using var dialog = new ColorDialog { Color = Color.FromArgb(settings.ColorArgb), FullOpen = true };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) ChangeColor(dialog.Color);
    }
    void ChangeColor(Color color) { OptionsChanging?.Invoke(); settings.ColorArgb = color.ToArgb(); UpdateColorState(); OptionsChanged?.Invoke(); }
    void RequestTool(DrawingTool value)
    {
        ToolSelected?.Invoke(value);
        if (selectedTool != value) SetSelectedTool(value);
    }
    void UpdateColorState()
    {
        foreach (var swatch in swatches) { swatch.Selected = swatch.ColorValue.ToArgb() == settings.ColorArgb; swatch.Invalidate(); }
        colorPicker.Accent = Color.FromArgb(settings.ColorArgb); colorPicker.Invalidate();
    }
    public void SetSelectedTool(DrawingTool tool)
    {
        selectedTool = tool;
        if (tool is DrawingTool.Brush or DrawingTool.Mosaic) brushMode = tool;
        foreach (var pair in tools) { pair.Value.Selected = pair.Key == (tool == DrawingTool.Mosaic ? DrawingTool.Brush : tool); pair.Value.Invalidate(); }
        normalBrush.Selected = tool == DrawingTool.Brush; mosaicBrush.Selected = tool == DrawingTool.Mosaic;
        normalBrush.Invalidate(); mosaicBrush.Invalidate();
        palette.Visible = tool == DrawingTool.Brush;
        brushModes.Visible = tool is DrawingTool.Brush or DrawingTool.Mosaic;
        widthGroup.Visible = tool is DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Arrow or DrawingTool.Brush;
        fontGroup.Visible = tool is DrawingTool.Text or DrawingTool.Marker;
        mosaicBrushGroup.Visible = mosaicBlockGroup.Visible = tool == DrawingTool.Mosaic;
        previousMaxWidth = -1;
        Configure(screenDpiScale, availableWidth);
    }

    public void Configure(float dpiScale, int maxWidth)
    {
        if (layingOut) return;
        availableWidth = maxWidth;
        screenDpiScale = Math.Clamp(dpiScale, 1, 4);
        float effectiveScale = UiSizing.Toolbar(settings, screenDpiScale);
        if (scale == effectiveScale && previousMaxWidth == maxWidth) return;
        previousMaxWidth = maxWidth;
        layingOut = true; SuspendLayout();
        try
        {
            scale = effectiveScale;
            int S(float value) => (int)Math.Ceiling(value * scale);
            int padding = S(12), gap = S(4), buttonHeight = S(44), optionHeight = S(48);
            maxWidth = Math.Max(S(260), maxWidth);
            int CommandWidth(Control control)
            {
                if (control is not ToolbarIconButton button) return S(9);
                if (!button.Primary) return S(40);
                using var captionFont = new Font("Microsoft YaHei UI", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                return Math.Max(S(84), TextRenderer.MeasureText(button.Caption, captionFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + S(58));
            }
            int naturalWidth = padding * 2 + commands.Sum(CommandWidth) + gap * (commands.Count - 1);
            int deficit = naturalWidth - maxWidth;
            if (deficit > 0 && deficit <= (gap - S(2)) * (commands.Count - 1))
            {
                gap -= (int)Math.Ceiling(deficit / (double)(commands.Count - 1));
                naturalWidth = padding * 2 + commands.Sum(CommandWidth) + gap * (commands.Count - 1);
            }
            int width = Math.Min(naturalWidth, maxWidth);
            int x = padding, y = padding;
            foreach (var control in commands)
            {
                int controlWidth = CommandWidth(control);
                if (x + controlWidth > width - padding && x > padding) { x = padding; y += buttonHeight + gap; }
                if (control is ToolbarIconButton icon) { icon.UiScale = scale; control.Bounds = new(x, y, controlWidth, buttonHeight); }
                else control.Bounds = new(x + S(4), y + S(12), Math.Max(1, S(1)), buttonHeight - S(24));
                x += controlWidth + gap;
            }
            bool showOptions = parameterGroups.Any(ShowGroup);
            if (!showOptions)
            {
                dividerY = -1;
                Size = new(width, y + buttonHeight + padding);
                Invalidate(true);
                return;
            }
            dividerY = y + buttonHeight + S(10);
            y = dividerY + S(9); x = padding;
            colorPicker.UiScale = scale; colorPicker.Bounds = new(0, S(2), S(40), S(44));
            for (int i = 0; i < swatches.Count; i++)
            {
                var button = swatches[i]; button.UiScale = scale; button.Bounds = new(S(46 + (i % 8) * 23), S(1 + (i / 8) * 24), S(22), S(22));
            }
            palette.Size = new(S(230), optionHeight);
            normalBrush.UiScale = mosaicBrush.UiScale = scale;
            normalBrush.Bounds = new(0, S(2), S(96), buttonHeight);
            mosaicBrush.Bounds = new(S(102), S(2), S(112), buttonHeight);
            brushModes.Size = new(S(214), optionHeight);
            ConfigureNumber(widthGroup, widthLabel, stroke);
            ConfigureNumber(fontGroup, fontLabel, fontSize);
            ConfigureNumber(mosaicBrushGroup, mosaicBrushLabel, MosaicBrushInput);
            ConfigureNumber(mosaicBlockGroup, mosaicBlockLabel, MosaicBlockInput);
            foreach (var control in parameterGroups.Where(ShowGroup))
            {
                if (control is ToolbarIconButton icon) { icon.UiScale = scale; control.Size = new(S(40), buttonHeight); }
                if (x + control.Width > width - padding && x > padding) { x = padding; y += optionHeight + S(6); }
                control.Location = new(x, y + (optionHeight - control.Height) / 2); x += control.Width + S(12);
            }
            Size = new(width, y + optionHeight + padding);
            Invalidate(true);
        }
        finally { ResumeLayout(false); layingOut = false; }
    }

    bool ShowGroup(Control control) => control == palette ? selectedTool == DrawingTool.Brush
        : control == brushModes ? selectedTool is DrawingTool.Brush or DrawingTool.Mosaic
        : control == widthGroup ? selectedTool is DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Arrow or DrawingTool.Brush
        : control == mosaicBrushGroup || control == mosaicBlockGroup ? selectedTool == DrawingTool.Mosaic
        : control == fontGroup && (selectedTool is DrawingTool.Text or DrawingTool.Marker);

    void ConfigureNumber(Panel group, Label label, NumericUpDown input)
    {
        int S(float value) => (int)Math.Ceiling(value * scale);
        ReplaceFont(label, new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel));
        ReplaceFont(input, new Font("Segoe UI", 16 * scale, FontStyle.Regular, GraphicsUnit.Pixel));
        int labelWidth = label.GetPreferredSize(Size.Empty).Width + S(8);
        int fieldWidth = Math.Max(S(84), TextRenderer.MeasureText(input.Maximum.ToString(), input.Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(38));
        group.Size = new(labelWidth + fieldWidth, S(48)); label.Bounds = new(0, 0, labelWidth, S(48));
        input.Width = fieldWidth; input.Height = Math.Max(S(36), input.PreferredHeight); input.Location = new(labelWidth, (group.Height - input.Height) / 2);
    }

    void ReplaceFont(Control control, Font next)
    {
        // WinForms ignores equal-valued Font assignments, retaining the old instance.
        if (control.Font.Equals(next)) { next.Dispose(); return; }
        ownedFonts.Remove(control, out var old);
        control.Font = next;
        ownedFonts.Add(control, next);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(210, 220, 233), Math.Max(1, scale));
        using var outline = UiShape.Round(new(.5f, .5f, Width - 1, Height - 1), 16 * scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(border, outline);
        using var divider = new Pen(Color.FromArgb(232, 237, 244), Math.Max(1, scale));
        if (dividerY >= 0) e.Graphics.DrawLine(divider, 12 * scale, dividerY, Width - 12 * scale, dividerY);
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); UiShape.Clip(this, 16 * scale);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
        if (disposing) { foreach (var font in ownedFonts.Values) font.Dispose(); ownedFonts.Clear(); }
    }
}
