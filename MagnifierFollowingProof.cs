using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace LightCapture;

internal static class MagnifierFollowingProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        var old = AppSettings.Deserialize("{\"Hotkey\":65}");
        Check(old.MagnifierRefreshIntervalMs == 0, "Old settings must default to real-time following.");
        Check(new AppSettings { MagnifierRefreshIntervalMs = int.MaxValue }.MagnifierRefreshIntervalMs == 33
            && new AppSettings { MagnifierRefreshIntervalMs = int.MinValue }.MagnifierRefreshIntervalMs == 0, "Invalid follow intervals are not normalized.");
        foreach (var mode in MagnifierFollowOptions.Modes)
        {
            var restored = AppSettings.Deserialize(JsonSerializer.Serialize(new AppSettings { MagnifierRefreshIntervalMs = mode.Interval }));
            Check(restored.MagnifierRefreshIntervalMs == mode.Interval, "Follow preference did not round-trip.");
            var state = new MagnifierFollowState();
            int frames = 0; long lastFrame = -100;
            for (int time = 0; time <= 120; time++)
            {
                var position = new Point(100 + time, 200 + time);
                if (state.Request(position, time, mode.Interval))
                {
                    Check(time - lastFrame >= mode.Interval, "Follow rate exceeded its configured interval.");
                    Check(state.Position == position, "Following used an old mouse sample.");
                    lastFrame = time; frames++;
                }
            }
            Check(frames == (mode.Interval == 0 ? 121 : 120 / mode.Interval + 1), "Follow scheduling discarded or delayed a due frame.");
            state.Flush(200, mode.Interval);
            Check(state.Position == new Point(220, 320) && !state.Pending, "The final movement was not delivered after the mouse stopped.");
            state.Reset(); Check(!state.HasPosition && !state.Pending && state.Request(new(1, 1), 201, mode.Interval), "Following did not restart immediately.");
            yield return new { name = "magnifier-follow-interval-" + mode.Interval, interval = mode.Interval, frames, status = "passed" };
        }
        var coalescing = new MagnifierFollowState();
        coalescing.Request(new(10, 10), 0, 33); coalescing.Request(new(20, 20), 1, 33); coalescing.Request(new(30, 30), 2, 33);
        Check(coalescing.Pending && coalescing.RemainingMilliseconds(2, 33) == 31, "Pending frame deadline is wrong.");
        Check(coalescing.Flush(33, 33) && coalescing.Position == new Point(30, 30), "Buffered following replayed obsolete coordinates.");
        coalescing.Request(new(50, 50), 34, 33); coalescing.Request(new(30, 30), 35, 33);
        Check(!coalescing.Pending, "Returning to the displayed position left an obsolete update queued.");
        Check(coalescing.Request(new(60, 60), 36, 0), "Switching to real-time mode did not remove the pending delay.");
        yield return new { name = "magnifier-follow-coalescing-and-cancel", status = "passed" };

        CaptureDisplay[] displays = [new(new(-1920, -200, 1920, 1080), new(-1920, -200, 1920, 1040), 1),
            new(new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), 2), new(new(2560, -300, 1920, 1080), new(2560, -300, 1920, 1040), 1.25f)];
        Check(CaptureDisplay.At(displays, new(-100, 100)).Scale == 1 && CaptureDisplay.At(displays, new(100, 100)).Scale == 2
            && CaptureDisplay.At(displays, new(3000, -100)).Scale == 1.25f, "Cached monitor lookup lost mixed-DPI coordinates.");
        Check(CaptureDisplay.At(displays, new(100, -10)) == displays[1], "Monitor-gap fallback chose the wrong display.");
        yield return new { name = "magnifier-follow-cached-displays", status = "passed" };

        foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
        foreach (bool compact in new[] { false, true })
        {
            using var pane = new SettingsPane(old, () => { }, () => { });
            pane.Size = new((int)((compact ? 560 : 780) * scale), (int)((compact ? 430 : 810) * scale)); pane.Configure(scale); pane.CreateControl();
            pane.MagnifierSpeedField.Input.SelectedIndex = 3;
            Check(pane.MagnifierRefreshIntervalMs == 33 && old.MagnifierRefreshIntervalMs == 0, "Editing follow speed modified saved settings before save.");
            Check(pane.SizeCard.ClientRectangle.Contains(pane.MagnifierSpeedField.Bounds), "Follow-speed control is clipped.");
            var input = pane.MagnifierSpeedField.Input;
            Check(input.Width >= TextRenderer.MeasureText(input.SelectedItem!.ToString(), input.Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(20 * scale), "Follow-speed choice text is clipped.");
            pane.ResetSizesButton.PerformClick();
            Check(pane.MagnifierRefreshIntervalMs == 0 && pane.ToolbarSizePercent == 75 && pane.TrayMenuSizePercent == 75, "Appearance defaults did not restore real-time following.");
            if (!compact && scale == 1.5f)
            {
                using var preview = new Bitmap(pane.SizeCard.Width, pane.SizeCard.Height);
                pane.SizeCard.DrawToBitmap(preview, pane.SizeCard.ClientRectangle); preview.Save(Path.Combine(directory, "magnifier-follow-settings.png"), ImageFormat.Png);
            }
            yield return new { name = $"magnifier-follow-settings-{scale}-{compact}", scale, compact, status = "passed" };
        }
        yield return CheckPartialRendering(directory);
    }

    static object CheckPartialRendering(string directory)
    {
        using var source = new Bitmap(3840, 2160, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(source))
        {
            for (int y = 0; y < source.Height; y += 24)
            {
                using var stripe = new SolidBrush(Color.FromArgb((y * 7) % 256, (y * 11) % 256, (y * 17) % 256));
                graphics.FillRectangle(stripe, 0, y, source.Width, 24);
            }
            using var pen = new Pen(Color.White, 3);
            for (int x = 0; x < source.Width; x += 83) graphics.DrawLine(pen, x, 0, x, source.Height);
        }
        using var renderer = new CaptureMagnifier();
        using var expected = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var actual = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var fullGraphics = Graphics.FromImage(expected); using var partialGraphics = Graphics.FromImage(actual);
        var viewport = new Rectangle(Point.Empty, source.Size); var highlight = Rectangle.Inflate(viewport, -20, -20);
        var layout = renderer.Fit(fullGraphics, 2, viewport);
        Check(ReferenceEquals(layout, renderer.Fit(fullGraphics, 2, viewport)), "Magnifier layout is remeasured for each frame.");
        Rectangle Bounds(Point point) => layout.Place(point, viewport);
        void DrawFrame(Graphics graphics, Point point, Rectangle clip, bool full)
        {
            if (full) graphics.DrawImageUnscaled(source, 0, 0); else CapturePreview.DrawBackground(graphics, source, clip);
            CapturePreview.DrawMask(graphics, viewport, highlight); CapturePreview.DrawOutline(graphics, highlight, 2, 2);
            renderer.Draw(graphics, source, point, Bounds(point).Location, layout, highlight.Size);
        }
        Point previous = new(1000, 600); DrawFrame(partialGraphics, previous, viewport, true);
        int comparisons = 0;
        foreach (var point in new[] { new Point(1040, 620), new Point(3600, 1900), new Point(40, 40), new Point(43, 46) })
        {
            DrawFrame(fullGraphics, point, viewport, true);
            using var dirty = MagnifierDamage.Create(Bounds(previous), Bounds(point), viewport);
            var state = partialGraphics.Save(); partialGraphics.SetClip(dirty, CombineMode.Intersect);
            DrawFrame(partialGraphics, point, Rectangle.Ceiling(dirty.GetBounds(partialGraphics)), false); partialGraphics.Restore(state);
            CheckSamePixels(expected, actual); previous = point; comparisons++;
        }
        using var smallMove = MagnifierDamage.Create(Bounds(new(1000, 600)), Bounds(new(1005, 603)), viewport);
        using var matrix = new Matrix();
        double dirtyPixels = smallMove.GetRegionScans(matrix).Sum(rectangle => (double)rectangle.Width * rectangle.Height);
        double dirtyFraction = dirtyPixels / ((double)source.Width * source.Height);
        Check(dirtyFraction < .03, "A small mouse move still invalidates most of a 4K screen.");
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 24; i++) DrawFrame(fullGraphics, new(1000 + i * 5, 600 + i * 3), viewport, true);
        double fullMilliseconds = timer.Elapsed.TotalMilliseconds; timer.Restart();
        Point last = new(1000, 600);
        for (int i = 0; i < 24; i++)
        {
            var point = new Point(1000 + i * 5, 600 + i * 3);
            using var dirty = MagnifierDamage.Create(Bounds(last), Bounds(point), viewport);
            var state = partialGraphics.Save(); partialGraphics.SetClip(dirty, CombineMode.Intersect);
            DrawFrame(partialGraphics, point, Rectangle.Ceiling(dirty.GetBounds(partialGraphics)), false); partialGraphics.Restore(state); last = point;
        }
        double partialMilliseconds = timer.Elapsed.TotalMilliseconds;
        using var sample = actual.Clone(new Rectangle(Bounds(last).Location, layout.Size), PixelFormat.Format32bppArgb);
        sample.Save(Path.Combine(directory, "magnifier-follow-partial-preview.png"), ImageFormat.Png);
        return new { name = "magnifier-follow-partial-render-4k", comparisons, dirtyFraction, fullMilliseconds, partialMilliseconds, benchmark = "synthetic-offscreen-paint", status = "passed" };
    }
    static unsafe void CheckSamePixels(Bitmap expected, Bitmap actual)
    {
        var area = new Rectangle(Point.Empty, expected.Size);
        var first = expected.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var second = actual.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < expected.Height; y++)
            {
                var a = new ReadOnlySpan<byte>((byte*)first.Scan0 + y * first.Stride, expected.Width * 4);
                var b = new ReadOnlySpan<byte>((byte*)second.Scan0 + y * second.Stride, actual.Width * 4);
                Check(a.SequenceEqual(b), "Partial redraw left stale pixels, ghosts, or shifted source pixels.");
            }
        }
        finally { expected.UnlockBits(first); actual.UnlockBits(second); }
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
