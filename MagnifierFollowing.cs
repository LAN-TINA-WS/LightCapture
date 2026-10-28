namespace LightCapture;

internal static class MagnifierFollowOptions
{
    internal static readonly (int Interval, string Name)[] Modes = [(0, "实时（最快，默认）"), (8, "快速"), (16, "标准"), (33, "省电")];
    internal static int NormalizeInterval(int value) => Modes.MinBy(mode => Math.Abs((long)mode.Interval - value)).Interval;
    internal static int IndexOf(int interval) => Array.FindIndex(Modes, mode => mode.Interval == NormalizeInterval(interval));
}

// Keep the newest input only, so a final movement is never lost or replayed later.
internal sealed class MagnifierFollowState
{
    internal Point Position { get; private set; }
    internal Point Latest { get; private set; }
    internal bool HasPosition { get; private set; }
    internal bool Pending { get; private set; }
    long lastFrameMilliseconds;
    internal bool Request(Point position, long now, int interval, bool force = false)
    {
        Latest = position;
        if (HasPosition && position == Position && !force) { Pending = false; return false; }
        interval = MagnifierFollowOptions.NormalizeInterval(interval);
        if (!HasPosition || force || interval == 0 || now - lastFrameMilliseconds >= interval)
        {
            Position = position; HasPosition = true; Pending = false; lastFrameMilliseconds = now; return true;
        }
        Pending = true; return false;
    }
    internal bool Flush(long now, int interval) => Pending && Request(Latest, now, interval);
    internal int RemainingMilliseconds(long now, int interval) => !Pending ? 0
        : (int)Math.Max(1, MagnifierFollowOptions.NormalizeInterval(interval) - (now - lastFrameMilliseconds));
    internal void Reset() { HasPosition = Pending = false; }
}

internal sealed record CaptureDisplay(Rectangle Bounds, Rectangle WorkingArea, float Scale)
{
    internal static CaptureDisplay At(IReadOnlyList<CaptureDisplay> displays, Point point)
    {
        foreach (var display in displays) if (display.Bounds.Contains(point)) return display;
        return displays.MinBy(display =>
        {
            long dx = Math.Max(0L, Math.Max((long)display.Bounds.Left - point.X, (long)point.X - display.Bounds.Right + 1));
            long dy = Math.Max(0L, Math.Max((long)display.Bounds.Top - point.Y, (long)point.Y - display.Bounds.Bottom + 1));
            return dx * dx + dy * dy;
        }) ?? throw new InvalidOperationException("没有可用的显示器信息。");
    }
}

internal static class MagnifierDamage
{
    internal static Region Create(Rectangle previous, Rectangle current, Rectangle viewport)
    {
        var region = new Region(Rectangle.Empty);
        foreach (var bounds in new[] { previous, current })
        {
            if (bounds.IsEmpty) continue;
            var dirty = Rectangle.Intersect(Rectangle.Inflate(bounds, 2, 2), viewport);
            if (!dirty.IsEmpty) region.Union(dirty);
        }
        return region;
    }
}
