using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LightCapture;

internal static class CaptureSelectionProof
{
    readonly record struct Frame(Rectangle Selection, Point Cursor, Rectangle Hover = default, PointF[]? Polygon = null);

    internal static IEnumerable<object> Generate(string directory)
    {
        foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
            yield return CheckSequence(directory, scale);
        yield return CheckLargeSelection(directory);
    }

    static Bitmap Source(Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var gradient = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.CornflowerBlue, Color.LightGoldenrodYellow, 35);
        graphics.FillRectangle(gradient, 0, 0, size.Width, size.Height);
        for (int x = 0; x < size.Width; x += 71) graphics.DrawLine(Pens.White, x, 0, x, size.Height);
        for (int y = 0; y < size.Height; y += 47) graphics.DrawLine(Pens.DarkSlateBlue, 0, y, size.Width, y);
        return bitmap;
    }

    sealed class Scene : IDisposable
    {
        readonly Bitmap source;
        readonly CaptureBackdrop backdrop;
        readonly CaptureMagnifier magnifier = new();
        readonly Font font;
        readonly float scale;
        readonly bool annotations;
        internal Rectangle Viewport { get; }
        internal Scene(Bitmap source, float scale, bool annotations = false)
        {
            this.source = source; this.scale = scale; this.annotations = annotations;
            Viewport = new(Point.Empty, source.Size); backdrop = new(source);
            font = new Font("Microsoft YaHei UI", 9);
        }
        internal Rectangle MagnifierBounds(Graphics graphics, Frame frame)
            => magnifier.Fit(graphics, scale, Viewport).Place(frame.Cursor, Viewport);
        internal Region Damage(Graphics graphics, Frame before, Frame after)
        {
            var region = CaptureSelectionDamage.Create(graphics, before.Selection, after.Selection, before.Hover, after.Hover,
                Viewport, font, before.Polygon == null && after.Polygon == null);
            using var lens = MagnifierDamage.Create(MagnifierBounds(graphics, before), MagnifierBounds(graphics, after), Viewport);
            region.Union(lens); return region;
        }
        internal void Paint(Graphics graphics, Frame frame, bool reference)
        {
            var highlight = CapturePreview.ActiveRectangle(frame.Selection, frame.Hover, false);
            if (reference) CapturePreview.DrawBackground(graphics, source, Viewport);
            else backdrop.Draw(graphics, Rectangle.Ceiling(graphics.ClipBounds), highlight, frame.Polygon);
            if (CaptureSelectionVisuals.HasArea(frame.Selection) && annotations)
            {
                var state = graphics.Save(); graphics.SetClip(frame.Selection, CombineMode.Intersect);
                if (frame.Polygon != null)
                {
                    using var path = new GraphicsPath(); path.AddPolygon(frame.Polygon); graphics.SetClip(path, CombineMode.Intersect);
                }
                CaptureDocument.DrawAnnotation(graphics, new Annotation
                {
                    Tool = DrawingTool.Brush, Width = 9, ColorArgb = Color.OrangeRed.ToArgb(),
                    Points = [new(20, 60), new(700, 400), new(50, 530)]
                }, source);
                graphics.Restore(state);
            }
            if (reference) CapturePreview.DrawMask(graphics, Viewport, highlight, frame.Polygon);
            if (CaptureSelectionVisuals.HasArea(frame.Selection)) CaptureSelectionVisuals.Draw(graphics, frame.Selection, Viewport, font);
            else CapturePreview.DrawOutline(graphics, highlight, 6, scale);
            if (frame.Polygon != null)
            {
                using var path = new GraphicsPath(); path.AddPolygon(frame.Polygon);
                using var pen = new Pen(Color.DeepSkyBlue, 2); graphics.DrawPath(pen, path);
            }
            var layout = magnifier.Fit(graphics, scale, Viewport);
            magnifier.Draw(graphics, source, frame.Cursor, layout.Place(frame.Cursor, Viewport).Location, layout, highlight.Size);
            if (!CaptureSelectionVisuals.HasArea(frame.Selection) && frame.Hover.IsEmpty)
                CaptureSelectionVisuals.DrawLabel(graphics, CaptureSelectionVisuals.GuideText, font, CaptureSelectionVisuals.GuidePosition(Viewport));
        }
        public void Dispose() { backdrop.Dispose(); magnifier.Dispose(); font.Dispose(); }
    }

    static object CheckSequence(string directory, float scale)
    {
        using var source = Source(new(1024, 640));
        using var expected = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var actual = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        expected.SetResolution(96 * scale, 96 * scale); actual.SetResolution(96 * scale, 96 * scale);
        using var full = Graphics.FromImage(expected); using var partial = Graphics.FromImage(actual);
        using var scene = new Scene(source, scale, true);
        Frame[] frames = [
            new(Rectangle.Empty, new(210, 120)),
            new(new(200, 120, 2, 2), new(202, 122)),
            new(new(200, 120, 500, 360), new(700, 480)),
            new(new(200, 120, 503, 362), new(703, 482)),
            new(new(200, 120, 140, 60), new(340, 180)),
            new(new(130, 80, 70, 40), new(130, 80)),
            new(new(200, 80, 0, 180), new(200, 260)),
            new(new(80, 120, 120, 0), new(80, 120)),
            new(Rectangle.Empty, new(210, 120)),
            new(new(0, 0, 1024, 640), new(1023, 639)),
            new(new(0, 0, 900, 560), new(900, 560)),
            new(new(9, 5, 900, 560), new(909, 565)),
            new(new(14, 8, 900, 560), new(914, 568)),
            // A rate-limited magnifier can stay still while its size text changes.
            new(new(14, 8, 800, 520), new(914, 568)),
            new(new(40, 40, 500, 450), new(540, 490), Polygon: [new(40, 40), new(540, 110), new(350, 490)]),
            new(new(60, 70, 500, 450), new(560, 520), Polygon: [new(60, 70), new(560, 140), new(370, 520)]),
            new(Rectangle.Empty, new(220, 160), new(0, 0, 1024, 640)),
            new(new(220, 160, 1, 1), new(221, 161), new(0, 0, 1024, 640)),
            new(Rectangle.Empty, new(980, 30), new(850, 0, 174, 130)),
            new(new(960, 20, 24, 15), new(984, 35), new(850, 0, 174, 130))
        ];
        scene.Paint(partial, frames[0], false);
        int comparisons = 0;
        for (int i = 1; i < frames.Length; i++)
        {
            using var dirty = scene.Damage(partial, frames[i - 1], frames[i]);
            var state = partial.Save(); partial.SetClip(dirty, CombineMode.Intersect); scene.Paint(partial, frames[i], false); partial.Restore(state);
            scene.Paint(full, frames[i], true); SamePixels(expected, actual, directory, $"selection-{scale}-frame-{i}"); comparisons++;
        }
        // Windows can coalesce several invalidations before delivering WM_PAINT.
        using var combined = new Region(); combined.MakeEmpty();
        for (int i = frames.Length - 1; i > 0; i--) { using var dirty = scene.Damage(partial, frames[i], frames[i - 1]); combined.Union(dirty); }
        var saved = partial.Save(); partial.SetClip(combined, CombineMode.Intersect); scene.Paint(partial, frames[0], false); partial.Restore(saved);
        scene.Paint(full, frames[0], true); SamePixels(expected, actual, directory, $"selection-{scale}-coalesced"); comparisons++;
        actual.Save(Path.Combine(directory, $"selection-drag-{(int)(scale * 100)}.png"), ImageFormat.Png);
        return new { name = $"selection-drag-pixels-{scale}", scale, comparisons, status = "passed" };
    }

    static object CheckLargeSelection(string directory)
    {
        using var source = Source(new(3840, 2160));
        // Use native memory drawing contexts, as in the capture window's double buffer.
        // Graphics.FromImage would include unrelated full-image conversions in GetHdc.
        using var expectedSurface = new CaptureBitmapCopy(source); using var actualSurface = new CaptureBitmapCopy(source);
        using var full = expectedSurface.CreateGraphics(); using var partial = actualSurface.CreateGraphics();
        using var scene = new Scene(source, 2);
        Frame At(int i) => new(new(20, 20, 3650 + i * 3, 2000 + i * 2), new(3670 + i * 3, 2020 + i * 2));
        scene.Paint(partial, At(0), false);
        using var sample = scene.Damage(partial, At(0), At(1)); using var matrix = new Matrix();
        double dirtyPixels = sample.GetRegionScans(matrix).Sum(rectangle => (double)rectangle.Width * rectangle.Height);
        double dirtyFraction = dirtyPixels / ((double)source.Width * source.Height);
        Check(dirtyFraction < .1, "A small drag near full-screen still repaints most of a 4K screen.");
        var timer = Stopwatch.StartNew();
        for (int i = 1; i <= 24; i++) scene.Paint(full, At(i), true);
        double fullMilliseconds = timer.Elapsed.TotalMilliseconds; timer.Restart();
        for (int i = 1; i <= 24; i++)
        {
            using var dirty = scene.Damage(partial, At(i - 1), At(i));
            var state = partial.Save(); partial.SetClip(dirty, CombineMode.Intersect); scene.Paint(partial, At(i), false); partial.Restore(state);
        }
        double partialMilliseconds = timer.Elapsed.TotalMilliseconds;
        using var expected = expectedSurface.Snapshot(); using var actual = actualSurface.Snapshot();
        SamePixels(expected, actual, directory, "selection-4k-final");
        using var preview = new Bitmap(960, 540); using (var graphics = Graphics.FromImage(preview)) graphics.DrawImage(actual, 0, 0, 960, 540);
        preview.Save(Path.Combine(directory, "selection-drag-4k-preview.png"), ImageFormat.Png);
        return new { name = "selection-drag-4k", frames = 24, dirtyFraction, fullMilliseconds, partialMilliseconds, benchmark = "synthetic-native-buffer-paint", status = "passed" };
    }

    static unsafe void SamePixels(Bitmap expected, Bitmap actual, string directory, string name)
    {
        int differentX = -1, differentY = -1;
        var area = new Rectangle(Point.Empty, expected.Size);
        var a = expected.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var b = actual.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < expected.Height && differentY < 0; y++)
            {
                var first = new ReadOnlySpan<int>((byte*)a.Scan0 + y * a.Stride, expected.Width);
                var second = new ReadOnlySpan<int>((byte*)b.Scan0 + y * b.Stride, actual.Width);
                if (first.SequenceEqual(second)) continue;
                for (int x = 0; x < first.Length; x++) if (first[x] != second[x]) { differentX = x; differentY = y; break; }
            }
        }
        finally { expected.UnlockBits(a); actual.UnlockBits(b); }
        if (differentY < 0) return;
        expected.Save(Path.Combine(directory, name + "-expected.png"), ImageFormat.Png);
        actual.Save(Path.Combine(directory, name + "-actual.png"), ImageFormat.Png);
        throw new InvalidOperationException($"{name}: stale or incorrect pixel at {differentX}, {differentY}.");
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
