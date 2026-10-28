using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LightCapture;

internal sealed class CaptureBackdrop : IDisposable
{
    readonly Bitmap original;
    readonly CaptureBitmapCopy originalCopy, shadedCopy;
    internal CaptureBackdrop(Bitmap original)
    {
        this.original = original;
        originalCopy = new(original);
        try
        {
            using var shaded = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppRgb);
            using var graphics = Graphics.FromImage(shaded);
            CapturePreview.DrawBackground(graphics, original, new(Point.Empty, original.Size));
            CapturePreview.DrawMask(graphics, new(Point.Empty, original.Size), Rectangle.Empty);
            shadedCopy = new(shaded);
        }
        catch { originalCopy.Dispose(); throw; }
    }
    internal void Draw(Graphics graphics, Rectangle clip, Rectangle highlight, IReadOnlyList<PointF>? polygon = null)
    {
        using var region = graphics.Clip;
        var viewport = new Rectangle(Point.Empty, original.Size);
        region.Intersect(clip); region.Intersect(viewport);
        using var bright = region.Clone(); bright.Intersect(highlight);
        if (polygon is { Count: >= 3 })
        {
            using var path = new GraphicsPath(); path.AddPolygon(polygon.ToArray()); bright.Intersect(path);
        }
        if (CaptureBitmapCopy.Draw(graphics, region, bright, viewport.Size, shadedCopy, originalCopy)) return;
        // Keep the managed path for destinations that cannot perform native bitmap copies.
        CapturePreview.DrawBackground(graphics, original, clip);
        CapturePreview.DrawMask(graphics, viewport, highlight, polygon);
    }
    public void Dispose() { originalCopy.Dispose(); shadedCopy.Dispose(); }
}

internal sealed class CaptureBitmapCopy : IDisposable
{
    IntPtr bitmap, context, previous;
    [DllImport("gdi32", SetLastError = true)] static extern IntPtr CreateCompatibleDC(IntPtr context);
    [DllImport("gdi32")] static extern IntPtr SelectObject(IntPtr context, IntPtr obj);
    [DllImport("gdi32")] static extern bool DeleteDC(IntPtr context);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32")] static extern int SaveDC(IntPtr context);
    [DllImport("gdi32")] static extern bool RestoreDC(IntPtr context, int saved);
    [DllImport("gdi32")] static extern int ExtSelectClipRgn(IntPtr context, IntPtr region, int mode);
    [DllImport("gdi32")] static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    internal CaptureBitmapCopy(Bitmap source)
    {
        try
        {
            bitmap = source.GetHbitmap(); context = CreateCompatibleDC(IntPtr.Zero);
            if (context == IntPtr.Zero) throw new Win32Exception();
            previous = SelectObject(context, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
        }
        catch { Dispose(); throw; }
    }
    internal Graphics CreateGraphics() => Graphics.FromHdc(context);
    internal Bitmap Snapshot() => Bitmap.FromHbitmap(bitmap);
    internal static bool Draw(Graphics graphics, Region dirty, Region bright, Size size, CaptureBitmapCopy shaded, CaptureBitmapCopy original)
    {
        // GetHdc does not carry the GDI+ clip; explicitly intersect both native regions.
        var dirtyHandle = dirty.GetHrgn(graphics);
        IntPtr brightHandle = IntPtr.Zero, destination = IntPtr.Zero;
        int saved = 0;
        try
        {
            brightHandle = bright.GetHrgn(graphics); destination = graphics.GetHdc(); saved = SaveDC(destination);
            if (saved == 0 || ExtSelectClipRgn(destination, dirtyHandle, 1 /* RGN_AND */) == 0) return false;
            if (!BitBlt(destination, 0, 0, size.Width, size.Height, shaded.context, 0, 0, 0x00CC0020 /* SRCCOPY */)) return false;
            if (ExtSelectClipRgn(destination, brightHandle, 1) == 0) return false;
            return BitBlt(destination, 0, 0, size.Width, size.Height, original.context, 0, 0, 0x00CC0020);
        }
        finally
        {
            if (saved != 0) RestoreDC(destination, saved);
            if (destination != IntPtr.Zero) graphics.ReleaseHdc(destination);
            DeleteObject(dirtyHandle); if (brightHandle != IntPtr.Zero) DeleteObject(brightHandle);
        }
    }
    public void Dispose()
    {
        if (context != IntPtr.Zero)
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(context, previous);
            DeleteDC(context); context = IntPtr.Zero;
        }
        if (bitmap != IntPtr.Zero) { DeleteObject(bitmap); bitmap = IntPtr.Zero; }
    }
}

internal static class CaptureSelectionVisuals
{
    internal const string GuideText = "拖动选择区域 · 单击吸附窗口 · 右键 / Esc 取消";
    internal static Point GuidePosition(Rectangle viewport) => new(Math.Max(16, viewport.Width / 2 - 210), 24);
    internal static bool HasArea(Rectangle rectangle) => rectangle.Width > 0 && rectangle.Height > 0;
    internal static string SizeText(Rectangle rectangle) => $"{rectangle.Width} × {rectangle.Height}";
    internal static Point BadgePosition(Rectangle rectangle, Rectangle viewport) => new(
        Math.Clamp(rectangle.Left, 8, Math.Max(8, viewport.Width - 150)), Math.Max(8, rectangle.Top - 32));
    internal static Rectangle LabelBounds(Graphics graphics, string text, Font font, Point point)
    {
        var size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.Default);
        return new(point, new Size(size.Width + 16, size.Height + 12));
    }
    internal static void DrawLabel(Graphics graphics, string text, Font font, Point point)
    {
        var bounds = LabelBounds(graphics, text, font, point);
        using var brush = new SolidBrush(Color.FromArgb(235, 27, 37, 52));
        graphics.FillRoundedRectangle(brush, bounds, 5);
        TextRenderer.DrawText(graphics, text, font, new Point(point.X + 8, point.Y + 6), Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
    }
    internal static List<Rectangle> Handles(Rectangle rectangle)
    {
        Point[] points = [new(rectangle.Left, rectangle.Top), new(rectangle.Left + rectangle.Width / 2, rectangle.Top), new(rectangle.Right, rectangle.Top),
            new(rectangle.Right, rectangle.Top + rectangle.Height / 2), new(rectangle.Right, rectangle.Bottom), new(rectangle.Left + rectangle.Width / 2, rectangle.Bottom),
            new(rectangle.Left, rectangle.Bottom), new(rectangle.Left, rectangle.Top + rectangle.Height / 2)];
        return points.Select(point => new Rectangle(point.X - 4, point.Y - 4, 8, 8)).ToList();
    }
    internal static void Draw(Graphics graphics, Rectangle selection, Rectangle viewport, Font font, bool handles = true)
    {
        if (!HasArea(selection)) return;
        using var pen = new Pen(Color.FromArgb(90, 171, 255), 1);
        graphics.DrawRectangle(pen, selection.X, selection.Y, Math.Max(0, selection.Width - 1), Math.Max(0, selection.Height - 1));
        if (handles) foreach (var handle in Handles(selection)) { graphics.FillRectangle(Brushes.White, handle); graphics.DrawRectangle(Pens.DodgerBlue, handle); }
        DrawLabel(graphics, SizeText(selection), font, BadgePosition(selection, viewport));
    }
}

internal static class CaptureSelectionDamage
{
    internal static bool IsGeometryDrag(string interaction) => interaction is "selecting" or "moving-selection" or "resizing-selection";
    internal static Region Create(Graphics graphics, Rectangle previousSelection, Rectangle selection, Rectangle previousHover,
        Rectangle hover, Rectangle viewport, Font font, bool rectangular)
    {
        var previous = CapturePreview.ActiveRectangle(previousSelection, previousHover, false);
        var current = CapturePreview.ActiveRectangle(selection, hover, false);
        var damage = new Region(previous);
        // The common interior is unchanged for rectangular geometry. Polygon interiors can change.
        if (rectangular) damage.Xor(current); else damage.Union(current);
        AddBorder(damage, previous, CaptureSelectionVisuals.HasArea(previousSelection) ? 6 : 26);
        AddBorder(damage, current, CaptureSelectionVisuals.HasArea(selection) ? 6 : 26);
        foreach (var rectangle in new[] { previousSelection, selection })
            if (CaptureSelectionVisuals.HasArea(rectangle))
                damage.Union(Rectangle.Inflate(CaptureSelectionVisuals.LabelBounds(graphics, CaptureSelectionVisuals.SizeText(rectangle), font,
                    CaptureSelectionVisuals.BadgePosition(rectangle, viewport)), 2, 2));
        if (CaptureSelectionVisuals.HasArea(previousSelection) != CaptureSelectionVisuals.HasArea(selection))
            damage.Union(Rectangle.Inflate(CaptureSelectionVisuals.LabelBounds(graphics, CaptureSelectionVisuals.GuideText, font,
                CaptureSelectionVisuals.GuidePosition(viewport)), 2, 2));
        damage.Intersect(viewport); return damage;
    }
    static void AddBorder(Region damage, Rectangle rectangle, int padding)
    {
        if (!CaptureSelectionVisuals.HasArea(rectangle)) return;
        using var edge = new Region(Rectangle.Inflate(rectangle, padding, padding));
        var interior = Rectangle.Inflate(rectangle, -padding, -padding);
        if (CaptureSelectionVisuals.HasArea(interior)) edge.Exclude(interior);
        damage.Union(edge);
    }
}
