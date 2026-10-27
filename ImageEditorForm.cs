using System.Drawing.Drawing2D;

namespace LightCapture;

internal sealed class ImageEditorForm : Form
{
    readonly CaptureDocument document;
    readonly AppSettings settings;
    readonly CaptureToolbar toolbar;
    readonly Canvas canvas = new() { Location = new Point(16, 16), BackColor = Color.White };
    readonly Panel viewport = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(226, 231, 239) };
    readonly Label status = new() { AutoSize = true, Padding = new Padding(8) };
    DrawingTool tool = DrawingTool.Select;
    Annotation? draft;
    int selected = -1;
    PointF last;
    bool moving;
    float zoom = 1;
    public ImageEditorForm(Bitmap image, AppSettings settings, Action<Bitmap> pin)
    {
        this.settings = settings;
        toolbar = new(settings, imageEditor: true) { Dock = DockStyle.Top };
        document = new(image);
        Text = "轻截 · 图片编辑";
        float initialScale = Native.DpiScaleAt(Cursor.Position);
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new(Math.Min((int)(1180 * initialScale), workArea.Width - 32), Math.Min((int)(820 * initialScale), workArea.Height - 32));
        MinimumSize = new(Math.Min((int)(640 * initialScale), Size.Width), Math.Min((int)(480 * initialScale), Size.Height));
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9);
        Icon = Ui.CreateIcon();
        KeyPreview = true;
        toolbar.ToolSelected += value => { tool = value; selected = -1; toolbar.SetSelectedTool(value); canvas.Invalidate(); };
        toolbar.OptionsChanged += () => canvas.Invalidate();
        toolbar.CommandSelected += command =>
        {
            switch (command)
            {
                case ToolbarCommand.Undo: document.Undo(); selected = -1; canvas.Invalidate(); break;
                case ToolbarCommand.Redo: document.Redo(); selected = -1; canvas.Invalidate(); break;
                case ToolbarCommand.Save: Save(); break;
                case ToolbarCommand.Finish: _ = CopyAsync(); break;
                case ToolbarCommand.Pin: pin(Render()); break;
                case ToolbarCommand.Ocr: new TextResultForm(Render(), settings, false).Show(); break;
                case ToolbarCommand.ZoomOut: SetZoom(zoom / 1.2f); break;
                case ToolbarCommand.ZoomIn: SetZoom(zoom * 1.2f); break;
            }
        };
        canvas.Paint += PaintCanvas;
        canvas.MouseDown += MouseDownCanvas;
        canvas.MouseMove += MouseMoveCanvas;
        canvas.MouseUp += MouseUpCanvas;
        viewport.Controls.Add(canvas);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Color.White };
        bottom.Controls.Add(status);
        Controls.Add(viewport);
        Controls.Add(toolbar);
        Controls.Add(bottom);
        void LayoutToolbar()
        {
            if (IsDisposed || ClientSize.Width < 240) return;
            toolbar.Configure(DeviceDpi / 96f, ClientSize.Width);
        }
        Resize += (_, _) => LayoutToolbar();
        DpiChanged += (_, _) => LayoutToolbar();
        Shown += (_, _) => { LayoutToolbar(); SetZoom(Math.Min(1, (viewport.ClientSize.Width - 48) / (float)image.Width)); };
        FormClosed += (_, _) => { document.Dispose(); settings.Save(); };
    }
    Bitmap Render() => document.Render(new Rectangle(Point.Empty, document.Background.Size));
    internal void RefreshAppearance() => toolbar.Configure(DeviceDpi / 96f, ClientSize.Width);
    void SetZoom(float value)
    {
        zoom = Math.Clamp(value, .1f, 3);
        canvas.Size = new((int)Math.Ceiling(document.Background.Width * zoom), (int)Math.Ceiling(document.Background.Height * zoom));
        status.Text = $"{document.Background.Width} × {document.Background.Height}  ·  {zoom:P0}  ·  Ctrl+S 保存 / Ctrl+Enter 复制";
        canvas.Invalidate();
    }
    void PaintCanvas(object? sender, PaintEventArgs e)
    {
        e.Graphics.ScaleTransform(zoom, zoom);
        e.Graphics.DrawImageUnscaled(document.Background, 0, 0);
        document.DrawAnnotations(e.Graphics);
        if (draft != null)
            CaptureDocument.DrawAnnotation(e.Graphics, draft, document.Background);
        if (selected >= 0 && selected < document.Annotations.Count)
        {
            var r = document.Annotations[selected].Bounds;
            r.Inflate(5, 5);
            using var p = new Pen(Color.DodgerBlue, 1 / zoom) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(p, r.X, r.Y, Math.Max(8, r.Width), Math.Max(8, r.Height));
        }
    }
    PointF ImagePoint(MouseEventArgs e) => new(Math.Clamp(e.X / zoom, 0, document.Background.Width - 1), Math.Clamp(e.Y / zoom, 0, document.Background.Height - 1));
    void MouseDownCanvas(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        var p = ImagePoint(e);
        last = p;
        if (tool == DrawingTool.Select)
        {
            selected = document.Annotations.FindLastIndex(a => a.Hit(Point.Round(p)));
            if (selected >= 0)
            {
                document.Checkpoint();
                moving = true;
                canvas.Capture = true;
            }
            canvas.Invalidate();
            return;
        }
        if (tool == DrawingTool.Text)
        {
            using var dialog = new TextInputForm();
            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value))
            {
                document.Checkpoint();
                document.Annotations.Add(new()
                {
                    Tool = tool,
                    Points = [p],
                    Text = dialog.Value,
                    FontSize = settings.FontSize,
                    ColorArgb = settings.ColorArgb
                });
                canvas.Invalidate();
            }
            return;
        }
        draft = new()
        {
            Tool = tool,
            Points = [p],
            ColorArgb = settings.ColorArgb,
            Width = settings.StrokeWidth,
            MosaicBrushSize = settings.MosaicBrushSize,
            MosaicBlockSize = settings.MosaicBlockSize,
            FontSize = settings.FontSize
        };
        if (tool == DrawingTool.Marker)
        {
            draft.Number = document.NextNumber;
            document.Checkpoint();
            document.Annotations.Add(draft);
            draft = null;
            canvas.Invalidate();
            return;
        }
        if (tool is not DrawingTool.Brush and not DrawingTool.Mosaic)
            draft.Points.Add(p);
        canvas.Capture = true;
    }
    void MouseMoveCanvas(object? sender, MouseEventArgs e)
    {
        var p = ImagePoint(e);
        if (moving && selected >= 0)
        {
            document.Annotations[selected].Move(p.X - last.X, p.Y - last.Y);
            last = p;
            canvas.Invalidate();
            return;
        }
        if (draft == null)
            return;
        if (draft.Tool is DrawingTool.Brush or DrawingTool.Mosaic)
            draft.Points.Add(p);
        else
            draft.Points[^1] = p;
        canvas.Invalidate();
    }
    void MouseUpCanvas(object? sender, MouseEventArgs e)
    {
        moving = false;
        canvas.Capture = false;
        if (draft != null)
        {
            document.Checkpoint();
            document.Annotations.Add(draft);
            draft = null;
            canvas.Invalidate();
        }
    }
    async Task CopyAsync()
    {
        try
        {
            using var b = Render();
            await ImageOutput.CopyAsync(b);
            status.Text = "已复制到剪贴板";
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    void Save()
    {
        try
        {
            using var b = Render();
            if (ImageOutput.Save(this, b, settings))
                status.Text = "图片已保存";
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    protected override bool ProcessCmdKey(ref Message m, Keys key)
    {
        if (toolbar.EditingParameter && key != (Keys.Control | Keys.S) && key != (Keys.Control | Keys.Enter))
            return base.ProcessCmdKey(ref m, key);
        switch (key)
        {
            case Keys.Control | Keys.S:
                Save();
                return true;
            case Keys.Control | Keys.Enter:
                _ = CopyAsync();
                return true;
            case Keys.Control | Keys.Z:
                document.Undo();
                selected = -1;
                canvas.Invalidate();
                return true;
            case Keys.Control | Keys.Y:
                document.Redo();
                selected = -1;
                canvas.Invalidate();
                return true;
            case Keys.Delete:
                if (selected >= 0)
                {
                    document.Checkpoint();
                    document.Annotations.RemoveAt(selected);
                    selected = -1;
                    canvas.Invalidate();
                }
                return true;
        }
        return base.ProcessCmdKey(ref m, key);
    }
    sealed class Canvas : Control
    {
        public Canvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
    }
}

internal sealed class TextInputForm : Form
{
    readonly TextBox input = new() { Multiline = true, Dock = DockStyle.Fill, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    public string Value => input.Text;
    public TextInputForm()
    {
        Text = "输入文字";
        Size = new(450, 260);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 12);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        var ok = Ui.Button("添加", () => { DialogResult = DialogResult.OK; Close(); }, 80);
        bar.Controls.Add(ok);
        bar.Controls.Add(Ui.Button("取消", () => Close(), 80));
        Controls.Add(input);
        Controls.Add(bar);
        Shown += (_, _) => input.Focus();
    }
}
