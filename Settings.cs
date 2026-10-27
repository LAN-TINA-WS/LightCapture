using System.Text.Json;

namespace LightCapture;

internal sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = 3;
    public Keys Hotkey { get; set; } = Keys.A;
    public int ColorArgb { get; set; } = Color.FromArgb(251, 56, 56).ToArgb();
    public float StrokeWidth { get; set; } = 3;
    int mosaicBrushSize = 20, mosaicBlockSize = 10;
    public int MosaicBrushSize { get => mosaicBrushSize; set => mosaicBrushSize = Math.Clamp(value, 4, 240); }
    public int MosaicBlockSize { get => mosaicBlockSize; set => mosaicBlockSize = Math.Clamp(value, 2, 60); }
    public float FontSize { get; set; } = 22;
    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string OcrLanguage { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public TranslationPreferences Translation { get; set; } = new();
    int toolbarSizePercent = UiSizing.Default, trayMenuSizePercent = UiSizing.Default;
    public int ToolbarSizePercent { get => toolbarSizePercent; set => toolbarSizePercent = UiSizing.Clamp(value); }
    public int TrayMenuSizePercent { get => trayMenuSizePercent; set => trayMenuSizePercent = UiSizing.Clamp(value); }
    int previewBorderThickness = 2;
    public int PreviewBorderThickness { get => previewBorderThickness; set => previewBorderThickness = Math.Clamp(value, 1, 6); }
    public bool StartWithWindows
    {
        get; set;
    }
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightCapture");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string HotkeyText => $"{((HotkeyModifiers & 2) != 0 ? "Ctrl+" : "")}{((HotkeyModifiers & 1) != 0 ? "Alt+" : "")}{((HotkeyModifiers & 4) != 0 ? "Shift+" : "")}{HotkeyKey.Name(Hotkey)}";
    public static AppSettings Load()
    {
        try
        {
            return Deserialize(File.ReadAllText(SettingsPath));
        }
        catch { return new(); }
    }
    internal static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        settings.Translation ??= new();
        settings.StrokeWidth = float.IsFinite(settings.StrokeWidth) ? Math.Clamp(settings.StrokeWidth, 1, 20) : 3;
        settings.FontSize = float.IsFinite(settings.FontSize) ? Math.Clamp(settings.FontSize, 10, 96) : 22;
        settings.HotkeyModifiers &= 7;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(MosaicBrushSize), out _))
            settings.MosaicBrushSize = Math.Max(20, (int)settings.StrokeWidth * 6);
        return settings;
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, SettingsPath, true);
    }
}

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(32, 42, 54), Blue = Color.FromArgb(43, 105, 222), Surface = Color.FromArgb(248, 250, 253);
    public static readonly Color[] Palette = [Color.Black, Color.White, Color.Gray, Color.Silver, Color.Maroon, Color.FromArgb(251, 56, 56), Color.FromArgb(247, 136, 58), Color.Yellow, Color.FromArgb(48, 132, 48), Color.FromArgb(153, 204, 0), Color.FromArgb(56, 90, 211), Color.FromArgb(56, 148, 228), Color.Purple, Color.FromArgb(243, 27, 243), Color.Teal, Color.FromArgb(22, 220, 220)];
    public static Button Button(string text, Action click, int width = 70)
    {
        var b = new ModernButton(text, click) { Width = width, Height = 32, ForeColor = Ink, Margin = new Padding(3) };
        return b;
    }
    public static void Error(IWin32Window? owner, Exception ex, string title = "轻截") => MessageBox.Show(owner, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static void DisposeMenuWhenIdle(ContextMenuStrip menu)
    {
        EventHandler? idle = null;
        idle = (_, _) => { Application.Idle -= idle; menu.Dispose(); };
        Application.Idle += idle;
    }
    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Blue);
            g.FillRoundedRectangle(fill, new RectangleF(1, 1, 30, 30), 8);
            using var pen = new Pen(Color.White, 2.1f);
            g.DrawRectangle(pen, 8, 9, 16, 14);
            g.DrawLine(pen, 13, 6, 13, 12);
            g.DrawLine(pen, 5, 18, 11, 18);
            g.DrawLine(pen, 20, 20, 26, 26);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(h).Clone();
        }
        finally { Native.DestroyIcon(h); }
    }
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
