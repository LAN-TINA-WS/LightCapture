using System.Drawing.Drawing2D;

namespace LightCapture;

internal static class CapturePreview
{
    internal static readonly Color BorderColor = Color.FromArgb(38, 166, 255);
    internal static void DrawBackground(Graphics graphics, Bitmap image, Rectangle clip)
    {
        var area = Rectangle.Intersect(clip, new Rectangle(Point.Empty, image.Size));
        if (area.Width > 0 && area.Height > 0) graphics.DrawImage(image, area, area, GraphicsUnit.Pixel);
    }
    internal static Rectangle ActiveRectangle(Rectangle selection, Rectangle hover, bool choosingPolygon)
        => selection.Width > 0 && selection.Height > 0 ? selection : choosingPolygon ? Rectangle.Empty : hover;

    internal static void DrawMask(Graphics graphics, Rectangle viewport, Rectangle highlight, IReadOnlyList<PointF>? polygon = null)
    {
        using var outside = new Region(viewport);
        if (highlight.Width > 0 && highlight.Height > 0)
        {
            if (polygon is { Count: >= 3 })
            {
                using var path = new GraphicsPath(); path.AddPolygon(polygon.ToArray()); outside.Exclude(path);
            }
            else outside.Exclude(highlight);
        }
        using var shade = new SolidBrush(Color.FromArgb(115, 10, 17, 27));
        graphics.FillRegion(shade, outside);
    }
    internal static void DrawOutline(Graphics graphics, Rectangle rect, int thickness, float dpiScale)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        int pixels = Math.Max(1, (int)Math.Ceiling(Math.Clamp(thickness, 1, 6) * Math.Clamp(dpiScale, 1, 4)));
        int horizontal = Math.Min(pixels, rect.Width), vertical = Math.Min(pixels, rect.Height);
        using var brush = new SolidBrush(BorderColor);
        // Draw inside the candidate bounds so even screen-edge outlines retain full width.
        graphics.FillRectangle(brush, rect.Left, rect.Top, rect.Width, vertical);
        graphics.FillRectangle(brush, rect.Left, rect.Bottom - vertical, rect.Width, vertical);
        graphics.FillRectangle(brush, rect.Left, rect.Top, horizontal, rect.Height);
        graphics.FillRectangle(brush, rect.Right - horizontal, rect.Top, horizontal, rect.Height);
    }
}
