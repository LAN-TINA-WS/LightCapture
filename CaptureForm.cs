using System.Drawing.Drawing2D;

namespace LightCapture;

internal enum CaptureIntent
{
    Image, Ocr, Translate, Pin, Long, Record
}
internal enum CaptureAction
{
    Pin, Ocr, Translate, Long, Record
}

internal sealed class CaptureForm : Form, IMessageFilter
{
    readonly CaptureDocument document;
    readonly AppSettings settings;
    readonly Rectangle desktop;
    readonly List<Rectangle> windows;
    readonly CaptureIntent intent;
    readonly CaptureToolbar toolbar;
    readonly CaptureMagnifier magnifier = new();
    Rectangle selection, hover;
    Point cursorPoint, down, last;
    DrawingTool tool = DrawingTool.Select;
    Annotation? draft;
    TextBox? editor;
    Annotation? editingText;
    int selected = -1, resizeHandle = -1;
    string interaction = "";
    Rectangle startSelection;
    List<PointF>? startPolygon, polygon;
    Annotation? startAnnotation;
    bool suppressDoubleClick, ending;
    public event Action<CaptureAction, Bitmap, Rectangle>? Produced;
    public Rectangle Selection => selection;
    public CaptureDocument Document => document;

    public CaptureForm(Bitmap background, Rectangle desktop, AppSettings settings, CaptureIntent intent = CaptureIntent.Image, bool preselected = false)
    {
        this.settings = settings;
        toolbar = new(settings);
        this.desktop = desktop;
        this.intent = intent;
        document = new(background);
        windows = Native.VisibleWindows(desktop).Select(r => new Rectangle(r.X - desktop.X, r.Y - desktop.Y, r.Width, r.Height)).ToList();
        Text = "轻截 · " + (intent switch
        {
            CaptureIntent.Ocr => "屏幕识图",
            CaptureIntent.Translate => "屏幕翻译",
            CaptureIntent.Long => "长截图",
            CaptureIntent.Record => "区域录屏",
            _ => "屏幕截图"
        });
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = desktop;
        DoubleBuffered = true;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.Black;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        BuildToolbar();
        Controls.Add(toolbar);
        toolbar.Visible = false;
        if (preselected)
        {
            selection = new(Point.Empty, background.Size);
            Shown += (_, _) => PlaceToolbar();
        }
        Shown += (_, _) => { cursorPoint = PointToClient(Cursor.Position); UpdateHover(cursorPoint); Activate(); Focus(); Invalidate(); };
        FormClosed += (_, _) => { editor?.Dispose(); document.Dispose(); try { settings.Save(); } catch (Exception ex) { Program.Log(ex); } };
        Application.AddMessageFilter(this);
    }
    public bool PreFilterMessage(ref Message message)
    {
        const int WM_RBUTTONUP = 0x0205;
        if (message.Msg != WM_RBUTTONUP || IsDisposed)
            return false;
        // Include toolbar and text-input children so their native menus cannot open.
        var target = Control.FromChildHandle(message.HWnd);
        if (target?.FindForm() != this)
            return false;
        Close();
        return true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.RemoveMessageFilter(this);
        base.Dispose(disposing);
        if (disposing) magnifier.Dispose();
    }
    void BuildToolbar()
    {
        toolbar.ToolSelected += SetTool;
        toolbar.OptionsChanging += CommitText;
        toolbar.OptionsChanged += () => Invalidate();
        toolbar.CommandSelected += command =>
        {
            switch (command)
            {
                case ToolbarCommand.Undo: Undo(); break;
                case ToolbarCommand.Redo: Redo(); break;
                case ToolbarCommand.Save: Save(); break;
                case ToolbarCommand.Pin: Emit(CaptureAction.Pin); break;
                case ToolbarCommand.LongCapture: Emit(CaptureAction.Long); break;
                case ToolbarCommand.Ocr: Emit(CaptureAction.Ocr); break;
                case ToolbarCommand.Translate: Emit(CaptureAction.Translate); break;
                case ToolbarCommand.Record: Emit(CaptureAction.Record); break;
                case ToolbarCommand.Cancel: Close(); break;
                case ToolbarCommand.Finish: _ = FinishAsync(); break;
                case ToolbarCommand.Reset: ResetSelection(); break;
            }
        };
        SetTool(DrawingTool.Select);
    }
    void SetTool(DrawingTool value)
    {
        CommitText();
        tool = value;
        selected = -1;
        toolbar.SetSelectedTool(value);
        if (value == DrawingTool.Polygon)
        {
            polygon = [];
            selection = Rectangle.Empty;
            toolbar.Visible = false;
            interaction = "polygon";
        }
        else PlaceToolbar();
        Focus();
        Invalidate();
    }
    void ResetSelection()
    {
        CommitText();
        selection = Rectangle.Empty;
        polygon = null;
        draft = null;
        selected = -1;
        tool = DrawingTool.Select;
        toolbar.SetSelectedTool(tool);
        UpdateHover(cursorPoint);
        interaction = "";
        toolbar.Visible = false;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(document.Background, 0, 0);
        if (selection.Width > 0 && selection.Height > 0)
        {
            var state = g.Save();
            g.SetClip(selection);
            if (polygon is { Count: >= 3 })
            {
                using var p = new GraphicsPath();
                p.AddPolygon(polygon.ToArray());
                g.SetClip(p, CombineMode.Intersect);
            }
            document.DrawAnnotations(g);
            if (draft != null)
                CaptureDocument.DrawAnnotation(g, draft, document.Background);
            g.Restore(state);
        }
        var rect = CapturePreview.ActiveRectangle(selection, hover, interaction == "polygon");
        CapturePreview.DrawMask(g, ClientRectangle, rect, interaction != "polygon" && !selection.IsEmpty ? polygon : null);
        if (rect.Width > 0 && rect.Height > 0)
        {
            if (selection.Width > 0 && selection.Height > 0)
            {
                using var pen = new Pen(Color.FromArgb(90, 171, 255), 1);
                g.DrawRectangle(pen, rect.X, rect.Y, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
            }
            else
            {
                var absolute = new Point(desktop.X + cursorPoint.X, desktop.Y + cursorPoint.Y);
                CapturePreview.DrawOutline(g, rect, settings.PreviewBorderThickness, Native.DpiScaleAt(absolute));
            }
            if (selection.Width > 0 && interaction != "polygon")
            foreach (var h in Handles(selection))
            {
                g.FillRectangle(Brushes.White, h);
                g.DrawRectangle(Pens.DodgerBlue, h);
            }
            if (selection.Width > 0 && selection.Height > 0) DrawLabel(g, $"{rect.Width} × {rect.Height}", new Point(Math.Clamp(rect.Left, 8, Math.Max(8, Width - 150)), Math.Max(8, rect.Top - 32)));
        }
        if (polygon is { Count: > 0 })
        {
            using var p = new Pen(Color.FromArgb(110, 190, 255), 2);
            if (polygon.Count > 1)
                g.DrawLines(p, polygon.ToArray());
            if (interaction == "polygon")
                g.DrawLine(p, polygon[^1], cursorPoint);
            else if (polygon.Count >= 3)
                g.DrawLine(p, polygon[^1], polygon[0]);
            foreach (var point in polygon)
                g.FillEllipse(Brushes.White, point.X - 3, point.Y - 3, 6, 6);
        }
        if (selected >= 0 && selected < document.Annotations.Count)
        {
            var b = AnnotationBounds(document.Annotations[selected]);
            using var p = new Pen(Color.DodgerBlue, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
            g.FillRectangle(Brushes.White, b.Right - 4, b.Bottom - 4, 8, 8);
            g.DrawRectangle(Pens.DodgerBlue, b.Right - 4, b.Bottom - 4, 8, 8);
        }
        if (!toolbar.Visible && interaction != "moving-object" && interaction != "resizing-object" && draft == null)
            DrawMagnifier(g);
        if (selection.IsEmpty && (hover.IsEmpty || interaction == "polygon"))
        {
            string hint = interaction == "polygon" ? "自由选区：左键添加点 · 双击闭合 · 右键退出" : "拖动选择区域 · 单击吸附窗口 · 右键 / Esc 取消";
            DrawLabel(g, hint, new Point(Math.Max(16, Width / 2 - 210), 24));
        }
        base.OnPaint(e);
    }
    void UpdateHover(Point point)
    {
        hover = windows.FirstOrDefault(r => r.Contains(point));
        if (!hover.IsEmpty) return;
        var screen = Screen.FromPoint(PointToScreen(point)).Bounds;
        hover = Rectangle.Intersect(new Rectangle(screen.X - desktop.X, screen.Y - desktop.Y, screen.Width, screen.Height), ClientRectangle);
    }
    void DrawLabel(Graphics g, string text, Point at)
    {
        var size = TextRenderer.MeasureText(text, Font);
        var r = new Rectangle(at, new Size(size.Width + 16, size.Height + 12));
        using var b = new SolidBrush(Color.FromArgb(235, 27, 37, 52));
        g.FillRoundedRectangle(b, r, 5);
        TextRenderer.DrawText(g, text, Font, new Point(at.X + 8, at.Y + 6), Color.White, TextFormatFlags.NoPadding);
    }
    void DrawMagnifier(Graphics g)
    {
        if (cursorPoint.X < 0 || cursorPoint.Y < 0 || cursorPoint.X >= document.Background.Width || cursorPoint.Y >= document.Background.Height)
            return;
        var absolute = new Point(desktop.X + cursorPoint.X, desktop.Y + cursorPoint.Y);
        var area = Screen.FromPoint(absolute).WorkingArea;
        area.Offset(-desktop.X, -desktop.Y);
        var layout = magnifier.Fit(g, Native.DpiScaleAt(absolute), area);
        var dimensions = !selection.IsEmpty ? selection.Size : !hover.IsEmpty ? hover.Size : Screen.FromPoint(absolute).Bounds.Size;
        magnifier.Draw(g, document.Background, cursorPoint, layout.Place(cursorPoint, area).Location, layout, dimensions);
    }
    static List<Rectangle> Handles(Rectangle r)
    {
        Point[] p = [new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height / 2), new(r.Right, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2)];
        return p.Select(q => new Rectangle(q.X - 4, q.Y - 4, 8, 8)).ToList();
    }
    static RectangleF AnnotationBounds(Annotation a)
    {
        var b = a.Bounds;
        if (a.Tool == DrawingTool.Text)
            b = new RectangleF(a.Points[0], new SizeF(Math.Max(40, a.Text.Split('\n').Max(s => s.Length) * a.FontSize * .8f), Math.Max(28, a.FontSize * 1.5f * (a.Text.Count(c => c == '\n') + 1))));
        if (a.Tool == DrawingTool.Marker)
            b = new RectangleF(a.Points[0].X - 18, a.Points[0].Y - 18, 36, 36);
        return b;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        cursorPoint = e.Location;
        if (interaction == "selecting")
        {
            selection = Rectangle.Intersect(Geometry.Between(down, e.Location), ClientRectangle);
        }
        else if (interaction == "moving-selection")
        {
            selection = Geometry.MoveInside(startSelection, new(e.X - down.X, e.Y - down.Y), ClientRectangle);
            if (startPolygon != null)
                polygon = startPolygon.Select(p => new PointF(p.X + selection.X - startSelection.X, p.Y + selection.Y - startSelection.Y)).ToList();
        }
        else if (interaction == "resizing-selection")
        {
            int l = startSelection.Left, t = startSelection.Top, r = startSelection.Right, b = startSelection.Bottom;
            if (resizeHandle is 0 or 6 or 7)
                l = e.X;
            if (resizeHandle is 0 or 1 or 2)
                t = e.Y;
            if (resizeHandle is 2 or 3 or 4)
                r = e.X;
            if (resizeHandle is 4 or 5 or 6)
                b = e.Y;
            selection = Rectangle.Intersect(Geometry.Between(new(l, t), new(r, b)), ClientRectangle);
            if (startPolygon != null && startSelection.Width > 0 && startSelection.Height > 0)
                polygon = startPolygon.Select(p => new PointF(selection.X + (p.X - startSelection.X) * selection.Width / startSelection.Width, selection.Y + (p.Y - startSelection.Y) * selection.Height / startSelection.Height)).ToList();
        }
        else if (interaction == "moving-object" && selected >= 0)
        {
            document.Annotations[selected].Move(e.X - last.X, e.Y - last.Y);
        }
        else if (interaction == "resizing-object" && selected >= 0 && startAnnotation != null)
        {
            var a = document.Annotations[selected];
            var b = AnnotationBounds(startAnnotation);
            float sx = Math.Max(.1f, (e.X - b.Left) / Math.Max(1, b.Width)), sy = Math.Max(.1f, (e.Y - b.Top) / Math.Max(1, b.Height));
            a.Points = startAnnotation.Points.Select(p => new PointF(b.Left + (p.X - b.Left) * sx, b.Top + (p.Y - b.Top) * sy)).ToList();
            if (a.Tool is DrawingTool.Text or DrawingTool.Marker)
                a.FontSize = Math.Clamp(startAnnotation.FontSize * sy, 10, 140);
        }
        else if (interaction == "drawing" && draft != null)
        {
            var p = new PointF(Math.Clamp(e.X, selection.Left, selection.Right), Math.Clamp(e.Y, selection.Top, selection.Bottom));
            if (draft.Tool is DrawingTool.Brush or DrawingTool.Mosaic)
            {
                if (draft.Points.Count == 0 || Math.Abs(p.X - draft.Points[^1].X) + Math.Abs(p.Y - draft.Points[^1].Y) > 1)
                    draft.Points.Add(p);
            }
            else
            {
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    float dx = p.X - down.X, dy = p.Y - down.Y;
                    if (draft.Tool == DrawingTool.Arrow)
                    {
                        double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4), len = Math.Sqrt(dx * dx + dy * dy);
                        p = new(down.X + (float)(Math.Cos(angle) * len), down.Y + (float)(Math.Sin(angle) * len));
                    }
                    else
                    {
                        float size = Math.Min(Math.Abs(dx), Math.Abs(dy));
                        p = new(down.X + Math.Sign(dx) * size, down.Y + Math.Sign(dy) * size);
                    }
                }
                draft.Points[^1] = p;
            }
        }
        else if (selection.IsEmpty && interaction != "polygon")
        {
            UpdateHover(e.Location);
        }
        last = e.Location;
        Invalidate();
        base.OnMouseMove(e);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }
        CommitText();
        Focus();
        down = last = cursorPoint = e.Location;
        suppressDoubleClick = false;
        if (interaction == "polygon")
        {
            polygon!.Add(e.Location);
            Invalidate();
            return;
        }
        if (selection.IsEmpty)
        {
            interaction = "selecting";
            selection = Rectangle.Empty;
            Capture = true;
            return;
        }
        if (tool == DrawingTool.Select)
        {
            if (selected >= 0 && selected < document.Annotations.Count)
            {
                var b = AnnotationBounds(document.Annotations[selected]);
                if (new RectangleF(b.Right - 8, b.Bottom - 8, 16, 16).Contains(e.Location))
                {
                    document.Checkpoint();
                    startAnnotation = document.Annotations[selected].Clone();
                    interaction = "resizing-object";
                    Capture = true;
                    return;
                }
            }
            int hit = document.Annotations.FindLastIndex(a => a.Hit(e.Location));
            if (hit >= 0)
            {
                selected = hit;
                document.Checkpoint();
                interaction = "moving-object";
                Capture = true;
                Invalidate();
                return;
            }
            selected = -1;
            resizeHandle = Handles(selection).FindIndex(h => h.Contains(e.Location));
            startSelection = selection;
            startPolygon = polygon?.ToList();
            if (resizeHandle >= 0)
            {
                interaction = "resizing-selection";
                Capture = true;
                toolbar.Visible = false;
                return;
            }
            if (selection.Contains(e.Location))
            {
                interaction = "moving-selection";
                Capture = true;
                toolbar.Visible = false;
                return;
            }
            return;
        }
        if (!selection.Contains(e.Location))
            return;
        if (tool == DrawingTool.Text)
        {
            BeginText(e.Location);
            return;
        }
        var a = new Annotation { Tool = tool, ColorArgb = settings.ColorArgb, Width = settings.StrokeWidth, FontSize = settings.FontSize, Points = [e.Location], MosaicBrushSize = settings.MosaicBrushSize, MosaicBlockSize = settings.MosaicBlockSize };
        if (tool == DrawingTool.Marker)
        {
            a.Number = document.NextNumber;
            document.Checkpoint();
            document.Annotations.Add(a);
            Invalidate();
            return;
        }
        if (tool is not DrawingTool.Brush and not DrawingTool.Mosaic)
            a.Points.Add(e.Location);
        draft = a;
        interaction = "drawing";
        Capture = true;
        toolbar.Visible = false;
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Close();
            return;
        }
        if (e.Button != MouseButtons.Left || interaction == "polygon")
            return;
        Capture = false;
        if (interaction == "selecting")
        {
            if (selection.Width < 3 || selection.Height < 3)
                selection = hover.IsEmpty ? new Rectangle(0, 0, Width, Height) : Rectangle.Intersect(hover, ClientRectangle);
            suppressDoubleClick = true;
            if (intent != CaptureIntent.Image)
            {
                var action = intent switch
                {
                    CaptureIntent.Ocr => CaptureAction.Ocr,
                    CaptureIntent.Translate => CaptureAction.Translate,
                    CaptureIntent.Pin => CaptureAction.Pin,
                    CaptureIntent.Long => CaptureAction.Long,
                    _ => CaptureAction.Record
                };
                interaction = "";
                Emit(action);
                return;
            }
        }
        if (interaction == "drawing" && draft != null)
        {
            document.Checkpoint();
            document.Annotations.Add(draft);
            draft = null;
        }
        interaction = "";
        PlaceToolbar();
        Invalidate();
        base.OnMouseUp(e);
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (interaction == "polygon")
        {
            CompletePolygon();
            return;
        }
        if (suppressDoubleClick)
            return;
        if (tool == DrawingTool.Select && selected >= 0 && document.Annotations[selected].Tool == DrawingTool.Text)
        {
            BeginText(Point.Round(document.Annotations[selected].Points[0]), document.Annotations[selected]);
            return;
        }
        if (tool == DrawingTool.Select && selection.Contains(e.Location) && selected < 0)
            _ = FinishAsync();
        base.OnMouseDoubleClick(e);
    }
    void CompletePolygon()
    {
        if (polygon is not { Count: >= 3 })
            return;
        while (polygon.Count > 3 && polygon[^1] == polygon[^2])
            polygon.RemoveAt(polygon.Count - 1);
        selection = Rectangle.Intersect(Rectangle.Ceiling(Geometry.BoundsOf(polygon)), ClientRectangle);
        if (selection.Width < 1 || selection.Height < 1)
        {
            ResetSelection();
            return;
        }
        interaction = "";
        tool = DrawingTool.Select;
        toolbar.SetSelectedTool(tool);
        PlaceToolbar();
        Invalidate();
    }
    void PlaceToolbar()
    {
        if (ending || selection.Width < 1 || selection.Height < 1)
            return;
        var anchor = new Point(desktop.X + selection.Right - 1, desktop.Y + selection.Bottom - 1);
        var s = Screen.FromPoint(anchor).WorkingArea;
        float scale = Native.DpiScaleAt(anchor);
        int margin = (int)Math.Ceiling(12 * scale);
        toolbar.Configure(scale, s.Width - margin * 2);
        toolbar.Visible = true;
        s.Offset(-desktop.X, -desktop.Y);
        int x = Math.Clamp(selection.Right - toolbar.Width, s.Left + margin, Math.Max(s.Left + margin, s.Right - toolbar.Width - margin)), y = selection.Bottom + margin;
        if (y + toolbar.Height > s.Bottom - margin)
            y = selection.Top - toolbar.Height - margin;
        if (y < s.Top + margin)
            y = Math.Clamp(selection.Bottom - toolbar.Height - margin, s.Top + margin, Math.Max(s.Top + margin, s.Bottom - toolbar.Height - margin));
        toolbar.Location = new(x, y);
        toolbar.BringToFront();
    }
    void BeginText(Point p, Annotation? existing = null)
    {
        CommitText();
        editingText = existing;
        editor = new TextBox { Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.FixedSingle, Location = p, Size = new Size(Math.Min(360, Math.Max(80, selection.Right - p.X)), Math.Min(160, Math.Max(50, selection.Bottom - p.Y))), Font = new Font("Microsoft YaHei UI", existing?.FontSize ?? settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel), ForeColor = existing == null ? Color.FromArgb(settings.ColorArgb) : Color.FromArgb(existing.ColorArgb), BackColor = Color.White, Text = existing?.Text ?? "", AccessibleName = "截图文字编辑" };
        editor.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { CancelText(); e.SuppressKeyPress = true; } else if (e.Control && e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; } };
        Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
        editor.SelectAll();
    }
    void CancelText()
    {
        if (editor == null)
            return;
        Controls.Remove(editor);
        editor.Dispose();
        editor = null;
        editingText = null;
        Focus();
        Invalidate();
    }
    void CommitText()
    {
        if (editor == null)
            return;
        string text = editor.Text;
        var p = editor.Location;
        if (editingText != null)
        {
            if (editingText.Text != text)
            {
                document.Checkpoint();
                editingText.Text = text;
            }
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            document.Checkpoint();
            document.Annotations.Add(new()
            {
                Tool = DrawingTool.Text,
                Points = [p],
                Text = text,
                ColorArgb = settings.ColorArgb,
                FontSize = settings.FontSize
            });
        }
        CancelText();
    }
    void Undo()
    {
        CommitText();
        document.Undo();
        selected = -1;
        Invalidate();
    }
    void Redo()
    {
        CommitText();
        document.Redo();
        selected = -1;
        Invalidate();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (toolbar.EditingParameter && keyData is not Keys.Escape && keyData != (Keys.Control | Keys.S) && keyData != (Keys.Control | Keys.Enter))
            return base.ProcessCmdKey(ref msg, keyData);
        if (editor != null)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                CommitText();
                Save();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        switch (keyData)
        {
            case Keys.Escape:
                if (interaction == "polygon")
                {
                    ResetSelection();
                    return true;
                }
                Close();
                return true;
            case Keys.Control | Keys.Enter:
                _ = FinishAsync();
                return true;
            case Keys.Control | Keys.S:
                Save();
                return true;
            case Keys.Control | Keys.Z:
                Undo();
                return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z:
                Redo();
                return true;
            case Keys.Control | Keys.T:
                Emit(CaptureAction.Pin);
                return true;
            case Keys.Delete:
                if (selected >= 0)
                {
                    document.Checkpoint();
                    document.Annotations.RemoveAt(selected);
                    selected = -1;
                    Invalidate();
                }
                return true;
            case Keys.C:
                if (cursorPoint.X >= 0 && cursorPoint.Y >= 0 && cursorPoint.X < document.Background.Width && cursorPoint.Y < document.Background.Height)
                {
                    var c = document.Background.GetPixel(cursorPoint.X, cursorPoint.Y);
                    try
                    {
                        Clipboard.SetText($"#{c.R:X2}{c.G:X2}{c.B:X2}");
                    }
                    catch (Exception ex) { Ui.Error(this, ex); }
                    return true;
                }
                break;
        }
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down && !selection.IsEmpty)
        {
            int n = (keyData & Keys.Shift) != 0 ? 10 : 1;
            var delta = new Point(key == Keys.Left ? -n : key == Keys.Right ? n : 0, key == Keys.Up ? -n : key == Keys.Down ? n : 0);
            if (selected >= 0)
            {
                document.Checkpoint();
                document.Annotations[selected].Move(delta.X, delta.Y);
            }
            else
            {
                var moved = Geometry.MoveInside(selection, delta, ClientRectangle);
                if (polygon != null)
                    polygon = polygon.Select(p => new PointF(p.X + moved.X - selection.X, p.Y + moved.Y - selection.Y)).ToList();
                selection = moved;
            }
            PlaceToolbar();
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    Bitmap Render()
    {
        CommitText();
        return document.Render(selection, polygon);
    }
    async Task FinishAsync()
    {
        if (ending || selection.IsEmpty)
            return;
        ending = true;
        try
        {
            using var result = Render();
            await ImageOutput.CopyAsync(result);
            Close();
        }
        catch (Exception ex) { ending = false; Ui.Error(this, ex); PlaceToolbar(); }
    }
    void Save()
    {
        if (selection.IsEmpty)
            return;
        try
        {
            using var result = Render();
            if (ImageOutput.Save(this, result, settings))
                Close();
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    void Emit(CaptureAction action)
    {
        if (ending || selection.IsEmpty)
            return;
        try
        {
            var result = Render();
            var absolute = new Rectangle(selection.X + desktop.X, selection.Y + desktop.Y, selection.Width, selection.Height);
            ending = true;
            Close();
            Produced?.Invoke(action, result, absolute);
        }
        catch (Exception ex) { ending = false; Ui.Error(this, ex); }
    }
}
