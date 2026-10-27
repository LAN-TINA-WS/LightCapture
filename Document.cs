using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LightCapture;

internal enum DrawingTool
{
    Select, Rectangle, Ellipse, Arrow, Brush, Text, Marker, Mosaic, Polygon
}
internal sealed class Annotation
{
    public DrawingTool Tool
    {
        get; set;
    }
    public List<PointF> Points { get; set; } = [];
    public int ColorArgb
    {
        get; set;
    }
    public float Width { get; set; } = 3;
    public int MosaicBrushSize { get; set; } = 20;
    public int MosaicBlockSize { get; set; } = 10;
    public float FontSize { get; set; } = 22;
    public string Text { get; set; } = "";
    public int Number
    {
        get; set;
    }
    public Annotation Clone() => new() { Tool = Tool, Points = [.. Points], ColorArgb = ColorArgb, Width = Width, FontSize = FontSize, Text = Text, Number = Number, MosaicBrushSize = MosaicBrushSize, MosaicBlockSize = MosaicBlockSize };
    public RectangleF Bounds => Geometry.BoundsOf(Points);
    public void Move(float x, float y)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new(Points[i].X + x, Points[i].Y + y);
    }
    public bool Hit(Point p)
    {
        var r = Bounds;
        float hitRadius = Tool == DrawingTool.Mosaic ? Math.Max(2, MosaicBrushSize / 2f) : Math.Max(8, Width + 2);
        r.Inflate(hitRadius, hitRadius);
        if (Tool == DrawingTool.Text)
            r = new RectangleF(Points[0], new SizeF(Math.Max(60, Text.Length * FontSize), FontSize * (Text.Count(c => c == '\n') + 2)));
        if (Tool == DrawingTool.Marker)
            r = new RectangleF(Points[0].X - 18, Points[0].Y - 18, 36, 36);
        return r.Contains(p);
    }
}

internal sealed class CaptureDocument : IDisposable
{
    public Bitmap Background
    {
        get;
    }
    public List<Annotation> Annotations { get; } = [];
    readonly Stack<List<Annotation>> undo = new(), redo = new();
    public CaptureDocument(Bitmap background)
    {
        Background = background;
    }
    public void Checkpoint()
    {
        undo.Push(Annotations.Select(a => a.Clone()).ToList());
        if (undo.Count > 100)
        {
            var keep = undo.Take(100).Reverse().ToArray();
            undo.Clear();
            foreach (var state in keep)
                undo.Push(state);
        }
        redo.Clear();
    }
    public bool Undo()
    {
        if (undo.Count == 0)
            return false;
        redo.Push(Annotations.Select(a => a.Clone()).ToList());
        Restore(undo.Pop());
        return true;
    }
    public bool Redo()
    {
        if (redo.Count == 0)
            return false;
        undo.Push(Annotations.Select(a => a.Clone()).ToList());
        Restore(redo.Pop());
        return true;
    }
    void Restore(List<Annotation> list)
    {
        Annotations.Clear();
        Annotations.AddRange(list);
    }
    public int NextNumber => Annotations.Where(a => a.Tool == DrawingTool.Marker).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;
    public Bitmap Render(Rectangle selection, IReadOnlyList<PointF>? polygon = null)
    {
        selection = Rectangle.Intersect(selection, new Rectangle(Point.Empty, Background.Size));
        if (selection.Width < 1 || selection.Height < 1)
            throw new InvalidOperationException("请先选择截图区域。");
        using var full = new Bitmap(Background.Width, Background.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            g.DrawImageUnscaled(Background, 0, 0);
            DrawAnnotations(g);
        }
        var result = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.Transparent);
            if (polygon is { Count: >= 3 })
            {
                using var path = new GraphicsPath();
                path.AddPolygon(polygon.Select(p => new PointF(p.X - selection.X, p.Y - selection.Y)).ToArray());
                g.SetClip(path);
            }
            g.DrawImage(full, new Rectangle(Point.Empty, result.Size), selection, GraphicsUnit.Pixel);
        }
        return result;
    }
    public void DrawAnnotations(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        foreach (var a in Annotations)
            DrawAnnotation(g, a, Background);
    }
    public static void DrawAnnotation(Graphics g, Annotation a, Bitmap background)
    {
        if (a.Points.Count == 0)
            return;
        using var pen = new Pen(Color.FromArgb(a.ColorArgb), a.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var r = a.Bounds;
        using var brush = new SolidBrush(pen.Color);
        switch (a.Tool)
        {
            case DrawingTool.Rectangle:
                if (r.Width > 0 && r.Height > 0)
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                break;
            case DrawingTool.Ellipse:
                if (r.Width > 0 && r.Height > 0)
                    g.DrawEllipse(pen, r);
                break;
            case DrawingTool.Arrow:
                if (a.Points.Count > 1)
                {
                    using var cap = new AdjustableArrowCap(4, 5, true);
                    pen.CustomEndCap = cap;
                    g.DrawLine(pen, a.Points[0], a.Points[^1]);
                }
                break;
            case DrawingTool.Brush:
                if (a.Points.Count > 1)
                    g.DrawLines(pen, a.Points.ToArray());
                else
                    g.FillEllipse(brush, a.Points[0].X - a.Width / 2, a.Points[0].Y - a.Width / 2, a.Width, a.Width);
                break;
            case DrawingTool.Text:
                using (var font = new Font("Microsoft YaHei UI", a.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                    g.DrawString(a.Text, font, brush, a.Points[0], StringFormat.GenericTypographic);
                break;
            case DrawingTool.Marker:
                float radius = Math.Max(14, a.FontSize * .7f);
                g.FillEllipse(brush, a.Points[0].X - radius, a.Points[0].Y - radius, radius * 2, radius * 2);
                using (var font = new Font("Segoe UI", Math.Max(12, a.FontSize * .72f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.White))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(a.Number.ToString(), font, white, new RectangleF(a.Points[0].X - radius, a.Points[0].Y - radius, radius * 2, radius * 2), fmt);
                break;
            case DrawingTool.Mosaic:
                int size = Math.Clamp(a.MosaicBlockSize, 2, 60);
                float diameter = Math.Clamp(a.MosaicBrushSize, 4, 240), radiusM = diameter / 2;
                var old = g.Save();
                using (var mask = new GraphicsPath(FillMode.Winding))
                {
                    foreach (var p in a.Points)
                        mask.AddEllipse(p.X - radiusM, p.Y - radiusM, radiusM * 2, radiusM * 2);
                    using var region = new Region(mask);
                    if (a.Points.Count > 1 && a.Points.Any(point => point != a.Points[0]))
                    {
                        using var stroke = new GraphicsPath(); stroke.AddLines(a.Points.ToArray());
                        using var maskPen = new Pen(Color.Black, diameter) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        stroke.Widen(maskPen); region.Union(stroke);
                    }
                    g.SetClip(region, CombineMode.Intersect);
                    int padding = (int)Math.Ceiling(radiusM);
                    var bounds = Rectangle.Intersect(Rectangle.Inflate(Rectangle.Ceiling(r), padding, padding), new Rectangle(Point.Empty, background.Size));
                    for (int y = Math.Max(0, bounds.Top / size * size); y < bounds.Bottom; y += size)
                    for (int x = Math.Max(0, bounds.Left / size * size); x < bounds.Right; x += size)
                    {
                        using var block = new SolidBrush(background.GetPixel(Math.Min(x + size / 2, background.Width - 1), Math.Min(y + size / 2, background.Height - 1)));
                        g.FillRectangle(block, x, y, size, size);
                    }
                }
                g.Restore(old);
                break;
        }
    }
    public void Dispose() => Background.Dispose();
}

internal static class ImageOutput
{
    public static Bitmap Snapshot(Rectangle bounds)
    {
        if ((long)bounds.Width * bounds.Height > 100_000_000)
            throw new InvalidOperationException("桌面像素面积过大，请减少显示器捕获范围。");
        var b = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var g = Graphics.FromImage(b);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return b;
        }
        catch { b.Dispose(); throw; }
    }
    public static async Task CopyAsync(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, true, bitmap);
        data.SetData("PNG", false, stream);
        for (int i = 0; ; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true, 3, 80);
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (i < 4) { await Task.Delay(90); }
        }
    }
    public static bool Save(IWin32Window owner, Bitmap bitmap, AppSettings settings)
    {
        using var dlg = new SaveFileDialog { Title = "保存截图", Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp", DefaultExt = "png", FileName = "轻截_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), InitialDirectory = Directory.Exists(settings.SaveDirectory) ? settings.SaveDirectory : "" };
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return false;
        SaveFile(bitmap, dlg.FileName);
        settings.SaveDirectory = Path.GetDirectoryName(dlg.FileName)!;
        settings.Save();
        return true;
    }
    public static void SaveFile(Bitmap bitmap, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant(), dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (ext is ".jpg" or ".jpeg" or ".bmp")
            {
                using var flat = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(flat))
                {
                    g.Clear(Color.White);
                    g.DrawImageUnscaled(bitmap, 0, 0);
                }
                flat.Save(tmp, ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Jpeg);
            }
            else
                bitmap.Save(tmp, ImageFormat.Png);
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
