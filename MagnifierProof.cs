using System.Drawing.Imaging;

namespace LightCapture;

internal static class MagnifierProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        using var renderer = new CaptureMagnifier(); using var fonts = new UiFonts();
        using var source = new Bitmap(64, 64);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb((x * 29 + y * 7) % 256, (x * 5 + y * 31) % 256, (x * 11 + y * 19) % 256));
        foreach (float scale in new[] { 1, 1.25f, 1.5f, 1.75f, 2, 2.5f, 3 })
        {
            using var canvas = new Bitmap(1600, 1600, PixelFormat.Format24bppRgb);
            canvas.SetResolution(96 * scale, 96 * scale);
            using var graphics = Graphics.FromImage(canvas);
            var layout = renderer.Measure(graphics, scale);
            Rectangle[] regions = [layout.Preview, layout.Dimensions, layout.Hex, layout.CopyHint, layout.ExitHint];
            foreach (var region in regions) Check(layout.Panel.Contains(region), "Magnifier content extends outside the panel.");
            for (int i = 0; i < regions.Length; i++)
            for (int j = i + 1; j < regions.Length; j++) Check(!regions[i].IntersectsWith(regions[j]), "Magnifier content overlaps.");
            void CheckText(string text, Font font, Rectangle rect)
            {
                var measured = Size.Ceiling(graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic));
                Check(rect.Width >= measured.Width && rect.Height >= measured.Height, $"Magnifier text clips: {text}, area={rect.Size}, measured={measured}");
            }
            CheckText("99999 x 99999", fonts.Get(13 * scale), layout.Dimensions);
            CheckText("RGB:#DDDDDD", fonts.Get(13 * scale), layout.Hex);
            CheckText(CaptureMagnifier.CopyHintText, fonts.Get(13 * scale), layout.CopyHint);
            CheckText(CaptureMagnifier.ExitHintText, fonts.Get(13 * scale), layout.ExitHint);
            Check(layout.Size.Width < 190 * scale && layout.Size.Height < 220 * scale, "Magnifier is no longer compact.");
            int placements = 0;
            foreach (var screen in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -200, 1920, 1040), new Rectangle(1920, 0, 2560, 1400) })
            foreach (var point in new[] { new Point(screen.Left + 2, screen.Top + 2), new Point(screen.Right - 2, screen.Top + 2), new Point(screen.Left + 2, screen.Bottom - 2), new Point(screen.Right - 2, screen.Bottom - 2), new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2) })
            {
                var placed = layout.Place(point, screen);
                Check(screen.Contains(placed), "Magnifier extends past its current monitor.");
                Check(!placed.Contains(point), "Magnifier covers the cursor."); placements++;
            }
            var smallScreen = new Rectangle(0, 0, 640, 480);
            var fitted = renderer.Fit(graphics, scale, smallScreen);
            Check(smallScreen.Contains(fitted.Place(new(638, 478), smallScreen)), "Magnifier does not fit on a small display.");
            foreach (var point in new[] { new Point(32, 32), Point.Empty, new Point(63, 0), new Point(0, 63), new Point(63, 63) })
            {
                graphics.Clear(Ui.Surface); renderer.Draw(graphics, source, point, Point.Empty, layout);
                int dx = point.X < 32 ? 2 : -2, dy = point.Y < 32 ? 2 : -2;
                int x = layout.Preview.X + (CaptureMagnifier.Columns / 2 + dx) * layout.CellSize + layout.CellSize / 2;
                int y = layout.Preview.Y + (CaptureMagnifier.Rows / 2 + dy) * layout.CellSize + layout.CellSize / 2;
                Check(canvas.GetPixel(x, y).ToArgb() == source.GetPixel(point.X + dx, point.Y + dy).ToArgb(), "Magnification distorts or shifts source pixels.");
            }
            using var example = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(example))
            {
                g.Clear(Color.FromArgb(239, 241, 245));
            }
            int padding = (int)Math.Ceiling(12 * scale);
            using var preview = new Bitmap(layout.Size.Width + padding * 2, layout.Size.Height + padding * 2, PixelFormat.Format24bppRgb);
            preview.SetResolution(96 * scale, 96 * scale);
            using (var g = Graphics.FromImage(preview)) { g.Clear(Ui.Surface); renderer.Draw(g, example, new(32, 32), new(padding, padding), layout, new(2560, 1504)); }
            string name = $"magnifier-{(int)(scale * 100)}.png";
            preview.Save(Path.Combine(directory, name), ImageFormat.Png);
            yield return new { name, scale, width = layout.Size.Width, height = layout.Size.Height, placements, edgeSamples = 5, status = "passed" };
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
