using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LightCapture;

internal sealed record MagnifierLayout(float Scale, int CellSize, Rectangle Panel, Rectangle Preview,
    Rectangle Dimensions, Rectangle Hex, Rectangle CopyHint, Rectangle ExitHint)
{
    internal Size Size => Panel.Size;
    internal Rectangle Place(Point cursor, Rectangle screen)
    {
        int margin = (int)Math.Ceiling(8 * Scale), gap = (int)Math.Ceiling(18 * Scale);
        int x = cursor.X + gap, y = cursor.Y + gap;
        if (x + Size.Width > screen.Right - margin) x = cursor.X - Size.Width - gap;
        if (y + Size.Height > screen.Bottom - margin) y = cursor.Y - Size.Height - gap;
        x = Math.Clamp(x, screen.Left + margin, Math.Max(screen.Left + margin, screen.Right - Size.Width - margin));
        y = Math.Clamp(y, screen.Top + margin, Math.Max(screen.Top + margin, screen.Bottom - Size.Height - margin));
        return new(new Point(x, y), Size);
    }
}

internal sealed class CaptureMagnifier : IDisposable
{
    internal const int Columns = 19, Rows = 13;
    internal const string CopyHintText = "按 C 复制色号", ExitHintText = "鼠标右键退出截图";
    readonly UiFonts fonts = new();
    static Size MeasureText(Graphics graphics, string text, Font font)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        return Size.Ceiling(graphics.MeasureString(text, font, int.MaxValue, format));
    }
    static void DrawText(Graphics graphics, string text, Font font, Rectangle bounds)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, Brushes.White, bounds, format);
    }
    internal MagnifierLayout Measure(Graphics graphics, float requestedScale)
    {
        float scale = Math.Clamp(requestedScale, 1, 4);
        int S(float value) => (int)Math.Ceiling(value * scale);
        var font = fonts.Get(13 * scale);
        var texts = new[] { "99999 x 99999", "RGB:#DDDDDD", CopyHintText, ExitHintText }.Select(text => MeasureText(graphics, text, font)).ToArray();
        int padding = S(8), border = 1;
        int requiredWidth = texts.Max(size => size.Width) + padding * 2;
        int cell = Math.Max(S(8), (int)Math.Ceiling((requiredWidth - border * 2) / (double)Columns));
        int width = Columns * cell + border * 2;
        var preview = new Rectangle(border, border, Columns * cell, Rows * cell);
        int lineHeight = texts.Max(size => size.Height), y = preview.Bottom + border + S(4);
        var dimensions = new Rectangle(padding, y, width - padding * 2, lineHeight);
        var hex = new Rectangle(padding, dimensions.Bottom, dimensions.Width, lineHeight);
        var copy = new Rectangle(padding, hex.Bottom, dimensions.Width, lineHeight);
        var exit = new Rectangle(padding, copy.Bottom, dimensions.Width, lineHeight);
        return new(scale, cell, new(0, 0, width, exit.Bottom + S(6)), preview, dimensions, hex, copy, exit);
    }
    internal MagnifierLayout Fit(Graphics graphics, float scale, Rectangle screen)
    {
        var layout = Measure(graphics, scale);
        if (layout.Size.Width + 16 * layout.Scale > screen.Width || layout.Size.Height + 16 * layout.Scale > screen.Height)
        {
            float factor = Math.Min((screen.Width - 16 * layout.Scale) / layout.Size.Width, (screen.Height - 16 * layout.Scale) / layout.Size.Height);
            layout = Measure(graphics, Math.Max(1, scale * factor));
        }
        return layout;
    }
    internal void Draw(Graphics graphics, Bitmap image, Point cursor, Point origin, MagnifierLayout layout, Size regionSize = default)
    {
        var state = graphics.Save();
        try
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.None;
            Rectangle At(Rectangle rect) { rect.Offset(origin); return rect; }
            var panel = At(layout.Panel);
            using var background = new SolidBrush(Color.FromArgb(47, 47, 47));
            graphics.FillRectangle(background, panel);
            var preview = At(layout.Preview);
            var imageState = graphics.Save();
            graphics.SetClip(preview, CombineMode.Intersect);
            using (var fill = new SolidBrush(Ui.Surface)) graphics.FillRectangle(fill, preview);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var source = new Rectangle(cursor.X - Columns / 2, cursor.Y - Rows / 2, Columns, Rows);
            var valid = Rectangle.Intersect(source, new Rectangle(Point.Empty, image.Size));
            if (!valid.IsEmpty)
            {
                var dest = new Rectangle(preview.X + (valid.X - source.X) * layout.CellSize, preview.Y + (valid.Y - source.Y) * layout.CellSize,
                    valid.Width * layout.CellSize, valid.Height * layout.CellSize);
                graphics.DrawImage(image, dest, valid, GraphicsUnit.Pixel);
            }
            float cx = preview.X + preview.Width / 2f, cy = preview.Y + preview.Height / 2f;
            using (var cross = new Pen(Color.FromArgb(65, 194, 244), Math.Max(1, 4 * layout.Scale)))
            {
                graphics.DrawLine(cross, cx, preview.Top, cx, preview.Bottom);
                graphics.DrawLine(cross, preview.Left, cy, preview.Right, cy);
            }
            graphics.Restore(imageState);
            // Only the pixel preview has a fine outline; the information strip is flat.
            using (var border = new Pen(Color.FromArgb(40, 40, 40), 1)) graphics.DrawRectangle(border, panel.X, panel.Y, panel.Width - 1, layout.Preview.Bottom);
            using (var inner = new Pen(Color.White, 1)) graphics.DrawRectangle(inner, preview.X, preview.Y, preview.Width - 1, preview.Height - 1);
            if (regionSize.IsEmpty) regionSize = image.Size;
            var color = image.GetPixel(cursor.X, cursor.Y);
            var font = fonts.Get(13 * layout.Scale);
            DrawText(graphics, $"{regionSize.Width} x {regionSize.Height}", font, At(layout.Dimensions));
            DrawText(graphics, $"RGB:#{color.R:X2}{color.G:X2}{color.B:X2}", font, At(layout.Hex));
            DrawText(graphics, CopyHintText, font, At(layout.CopyHint));
            DrawText(graphics, ExitHintText, font, At(layout.ExitHint));
        }
        finally { graphics.Restore(state); }
    }
    public void Dispose() => fonts.Dispose();
}
