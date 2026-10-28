using System.Runtime.InteropServices;
using System.Text;

namespace LightCapture;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312, WM_CAPTURE = 0x8001;
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32")] public static extern bool EnumWindows(EnumProc proc, IntPtr parameter);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32")] static extern int GetUpdateRgn(IntPtr hwnd, IntPtr region, bool erase);
    [DllImport("gdi32")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr obj);
    internal static Region? PaintRegion(IntPtr hwnd)
    {
        var handle = CreateRectRgn(0, 0, 0, 0);
        if (handle == IntPtr.Zero) return null;
        try { return GetUpdateRgn(hwnd, handle, false) > 1 ? Region.FromHrgn(handle) : null; }
        finally { DeleteObject(handle); }
    }
    [DllImport("user32")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    public static float DpiScaleAt(Point point)
    {
        try { return GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out uint x, out _) == 0 ? Math.Clamp(x / 96f, 1, 4) : 1; }
        catch { return 1; }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom; public Rectangle Rect => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    public static List<Rectangle> VisibleWindows(Rectangle desktop)
    {
        var found = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;
            if (DwmGetWindowAttribute(hwnd, 9, out var r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r))
                return true;
            var rect = Rectangle.Intersect(r.Rect, desktop);
            if (rect.Width >= 30 && rect.Height >= 30)
                found.Add(rect);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}

internal static class Geometry
{
    public static Rectangle Between(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    public static Rectangle Clamp(Rectangle r, Rectangle bounds) => Rectangle.Intersect(r, bounds);
    public static Rectangle MoveInside(Rectangle r, Point delta, Rectangle bounds)
    {
        int x = Math.Clamp(r.X + delta.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - r.Width));
        int y = Math.Clamp(r.Y + delta.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - r.Height));
        return new Rectangle(x, y, r.Width, r.Height);
    }
    public static RectangleF BoundsOf(IEnumerable<PointF> points)
    {
        var list = points.ToArray();
        if (list.Length == 0)
            return RectangleF.Empty;
        return RectangleF.FromLTRB(list.Min(p => p.X), list.Min(p => p.Y), list.Max(p => p.X), list.Max(p => p.Y));
    }
}
