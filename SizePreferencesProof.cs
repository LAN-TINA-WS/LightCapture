using System.Drawing.Imaging;
using System.Text.Json;

namespace LightCapture;

internal static class SizePreferencesProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        var olderSettings = JsonSerializer.Deserialize<AppSettings>("{\"HotkeyModifiers\":3,\"Hotkey\":65}")!;
        Check(olderSettings.ToolbarSizePercent == 75 && olderSettings.TrayMenuSizePercent == 75, "Old settings must use the smaller defaults.");
        Check(olderSettings.PreviewBorderThickness == 2, "Old settings must use the more visible preview border.");
        var invalid = JsonSerializer.Deserialize<AppSettings>("{\"ToolbarSizePercent\":0,\"TrayMenuSizePercent\":9999}")!;
        Check(invalid.ToolbarSizePercent == 50 && invalid.TrayMenuSizePercent == 125, "Invalid UI sizes were not bounded.");
        using (var pane = new SettingsPane(olderSettings, () => { }, () => { }))
        {
            pane.ToolbarSizeSlider.AccessibilityObject.Value = "50%";
            pane.TrayMenuSizeSlider.AccessibilityObject.Value = "125%";
            pane.PreviewBorderField.Input.SelectedIndex = 5;
            Check(pane.ToolbarSizePercent == 50 && pane.TrayMenuSizePercent == 125, "UI sizes cannot be adjusted independently.");
            Check(olderSettings.ToolbarSizePercent == 75 && olderSettings.TrayMenuSizePercent == 75, "Editing must not change settings before save.");
            var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { ToolbarSizePercent = pane.ToolbarSizePercent, TrayMenuSizePercent = pane.TrayMenuSizePercent, PreviewBorderThickness = pane.PreviewBorderThickness }))!;
            Check(roundTrip.ToolbarSizePercent == 50 && roundTrip.TrayMenuSizePercent == 125, "Size preferences did not survive serialization.");
            Check(roundTrip.PreviewBorderThickness == 6 && olderSettings.PreviewBorderThickness == 2, "Preview border must persist only after applying settings.");
            using (var independentToolbar = new CaptureToolbar(roundTrip))
            using (var independentMenu = new ModernTrayMenu(roundTrip, _ => { }))
            {
                independentToolbar.Configure(2, 1850); independentMenu.Configure(2, 2500);
                Check(Math.Abs(independentToolbar.UiScale - 1) < .001f && Math.Abs(independentMenu.UiScale - 2.5f) < .001f,
                    "Toolbar and menu sizes must use their own independent settings.");
            }
            pane.ResetSizesButton.PerformClick();
            Check(pane.ToolbarSizePercent == 75 && pane.TrayMenuSizePercent == 75, "Reset must restore the smaller defaults.");
            Check(pane.PreviewBorderThickness == 2, "Reset must restore the default preview border.");
        }
        foreach (float dpi in new[] { 1, 1.5f, 2 })
        foreach (int percent in new[] { 50, 75, 100, 125 })
        {
            var settings = new AppSettings { ToolbarSizePercent = percent, TrayMenuSizePercent = percent };
            float expected = dpi * percent / 100f;
            foreach (bool editor in new[] { false, true })
            {
                using var toolbar = new CaptureToolbar(settings, editor);
                toolbar.Configure(dpi, 1850); toolbar.CreateControl();
                Check(Math.Abs(toolbar.UiScale - expected) < .001f, "Toolbar percentage was applied twice or ignored.");
                foreach (var tool in new[] { DrawingTool.Brush, DrawingTool.Mosaic, DrawingTool.Text, DrawingTool.Select })
                {
                    toolbar.SetSelectedTool(tool); CheckToolbarBounds(toolbar);
                }
                settings.ToolbarSizePercent = percent == 75 ? 100 : 75;
                toolbar.Configure(dpi, 1850);
                Check(Math.Abs(toolbar.UiScale - dpi * settings.ToolbarSizePercent / 100f) < .001f, "Saving a new size did not refresh an existing toolbar.");
                settings.ToolbarSizePercent = percent;
            }
            using var menu = new ModernTrayMenu(settings, _ => { }); menu.Configure(dpi, 2500); menu.CreateControl();
            Check(Math.Abs(menu.UiScale - expected) < .001f, "Menu percentage was applied twice or ignored.");
            Check(menu.Commands.All(item => menu.ClientRectangle.Contains(item.Bounds)), "Resizing clipped a menu command.");
            using var snapshot = new Bitmap(menu.Width, menu.Height, PixelFormat.Format24bppRgb);
            menu.DrawToBitmap(snapshot, menu.ClientRectangle);
            if (dpi == 1) snapshot.Save(Path.Combine(directory, $"menu-size-{percent}.png"), ImageFormat.Png);
            menu.Configure(dpi, 700);
            Check(menu.Height <= 700 && menu.Commands.All(item => menu.ClientRectangle.Contains(item.Bounds)), "Menu cannot fit a short screen.");
            yield return new { name = $"interface-size-{percent}-dpi-{dpi}", dpi, percent, status = "passed" };
        }
    }
    static void CheckToolbarBounds(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Check(parent.ClientRectangle.Contains(child.Bounds), "Resizing clips toolbar content: " + (child.AccessibleName ?? child.Name));
            if (child is not NumericUpDown) CheckToolbarBounds(child);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
