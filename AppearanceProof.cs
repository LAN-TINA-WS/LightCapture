using System.Drawing.Imaging;
using System.Text.Json;

namespace LightCapture;

internal static class AppearanceProof
{
    internal static int Run(string[] args)
    {
        int index = Array.IndexOf(args, "--artifacts");
        string directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "appearance-proof");
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        try
        {
            results.AddRange(ButtonRenderingProof.Generate(directory));
            results.AddRange(TranslationSettingsProof.Generate(directory));
            CheckKeyRecording();
            results.Add(new { name = "key-recording-command-keys-and-cancel", status = "passed" });
            results.AddRange(TextResultProof.Generate(directory));
            results.AddRange(MagnifierProof.Generate(directory));
            results.AddRange(MagnifierFollowingProof.Generate(directory));
            results.AddRange(CaptureSelectionProof.Generate(directory));
            results.AddRange(SizePreferencesProof.Generate(directory));
            results.AddRange(CapturePreviewProof.Generate(directory));
            foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
            {
                foreach (bool compact in new[] { false, true })
                {
                    int saved = 0, cancelled = 0;
                    using var pane = new SettingsPane(new(), () => saved++, () => cancelled++);
                    pane.Size = new((int)((compact ? 560 : 780) * scale), (int)((compact ? 430 : 810) * scale));
                    pane.Configure(scale); pane.CreateControl(); pane.PerformLayout();
                    Check(pane.ClientRectangle.Contains(pane.SaveButton.Parent!.Bounds), "Footer is outside the settings window.");
                    Check(pane.SaveButton.Parent.ClientRectangle.Contains(pane.SaveButton.Bounds), "Save button is clipped.");
                    Check(pane.CancelButton.Parent!.ClientRectangle.Contains(pane.CancelButton.Bounds), "Cancel button is clipped.");
                    foreach (var card in pane.Cards) CheckChildren(card);
                    var footerBounds = pane.SaveButton.Parent.Bounds;
                    Check(!footerBounds.IntersectsWith(pane.ScrollBody.Bounds), "Scrolling content overlaps fixed footer.");
                    int contentHeight = pane.ScrollBody.AutoScrollMinSize.Height;
                    if (compact) Check(contentHeight > pane.ScrollBody.ClientSize.Height, "Compact settings should provide vertical scrolling.");
                    pane.ScrollBody.AutoScrollPosition = new(0, contentHeight);
                    var last = pane.Cards.Last();
                    Check(last.Bottom + last.Parent!.Top <= pane.ScrollBody.ClientSize.Height, "Last settings card cannot be reached by scrolling.");
                    Check(pane.SaveButton.Parent.Bounds == footerBounds, "Scrolling moved the settings footer.");
                    if (compact && scale == 1)
                    {
                        using var scrolled = new Bitmap(pane.Width, pane.Height);
                        pane.DrawToBitmap(scrolled, pane.ClientRectangle); scrolled.Save(Path.Combine(directory, "settings-compact-scrolled.png"), ImageFormat.Png);
                    }
                    pane.SaveButton.PerformClick(); pane.CancelButton.PerformClick();
                    Check(saved == 1 && cancelled == 1, "Footer actions are not reachable.");
                    pane.ScrollBody.AutoScrollPosition = Point.Empty;
                    string name = $"settings-{(int)(scale * 100)}{(compact ? "-compact" : "")}.png";
                    using var bitmap = new Bitmap(pane.Width, pane.Height, PixelFormat.Format32bppArgb);
                    pane.DrawToBitmap(bitmap, pane.ClientRectangle); bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
                    if (!compact && scale == 1)
                    {
                        using var sizeControls = new Bitmap(pane.SizeCard.Width, pane.SizeCard.Height);
                        pane.SizeCard.DrawToBitmap(sizeControls, pane.SizeCard.ClientRectangle);
                        sizeControls.Save(Path.Combine(directory, "size-settings.png"), ImageFormat.Png);
                    }
                    results.Add(new { name, scale, compact, contentHeight, footerVisible = true, status = "passed" });
                }
                TrayCommand? invoked = null;
                using var menu = new ModernTrayMenu(new(), command => invoked = command);
                menu.Configure(scale, (int)(680 * scale)); menu.CreateControl();
                menu.Commands.Single(i => i.Text == "偏好设置").PerformClick();
                Check(invoked == TrayCommand.Settings, "Tray settings command routing failed.");
                Check(menu.Commands.All(i => i.Height >= 40 * menu.UiScale), "Tray menu rows are too small.");
                Check(menu.Width >= 300 * menu.UiScale, "Tray menu is too narrow.");
                menu.Commands.Single(i => i.Text == "偏好设置").Select();
                string menuName = $"tray-menu-{(int)(scale * 100)}.png";
                using var menuBitmap = new Bitmap(menu.Width, menu.Height, PixelFormat.Format32bppArgb);
                menu.DrawToBitmap(menuBitmap, menu.ClientRectangle); menuBitmap.Save(Path.Combine(directory, menuName), ImageFormat.Png);
                results.Add(new { name = menuName, scale, width = menu.Width, height = menu.Height, padding = menu.Padding.ToString(), itemBounds = menu.Commands[0].Bounds.ToString(), status = "passed" });
                if (scale == 2)
                {
                    menu.Configure(2, 700);
                    Check(menu.Height <= 700, "Menu exceeds short screen height at 200 percent.");
                    Check(menu.Commands.All(item => menu.ClientRectangle.Contains(item.Bounds)), "A menu command is outside the popup.");
                    using var shortMenu = new Bitmap(menu.Width, menu.Height);
                    menu.DrawToBitmap(shortMenu, menu.ClientRectangle); shortMenu.Save(Path.Combine(directory, "tray-menu-200-short.png"), ImageFormat.Png);
                    results.Add(new { name = "tray-menu-200-short.png", scale, height = menu.Height, status = "passed" });
                }
            }
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = results.Count, failed = 0, kind = "offscreen-interface-layout", results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = results.Count, failed = 1, error = ex.ToString(), results }, new JsonSerializerOptions { WriteIndented = true })); return 1;
        }
    }
    static void CheckChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Check(parent.ClientRectangle.Contains(child.Bounds), "Settings control clips: " + (child.AccessibleName ?? child.Text));
            if (child is Label label) Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "Settings label text is clipped: " + label.Text);
            if (child is not ComboBox) CheckChildren(child);
        }
    }
    static void CheckKeyRecording()
    {
        using var pane = new SettingsPane(new(), () => throw new InvalidOperationException("Recording must not save settings."), () => throw new InvalidOperationException("Recording must not close settings."));
        var field = pane.KeyCapture; field.CreateControl();
        var states = new List<bool>(); field.CaptureStateChanged += states.Add;
        bool Press(Keys key)
        {
            var message = Message.Create(field.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
            return field.PreProcessMessage(ref message);
        }
        foreach (var key in new[] { Keys.F9, Keys.D5, Keys.NumPad2, Keys.PrintScreen, Keys.OemMinus, Keys.Enter, Keys.Tab, Keys.Delete })
        {
            field.BeginCapture();
            Check(Press(Keys.ControlKey) && field.Recording, "A modifier alone must not end recording.");
            Check(Press(key) && !field.Recording && pane.SelectedKey == key, "Pressed key was not captured: " + key);
            Check(states.Last(), "Old hotkey must remain suspended until key release.");
            Check(Press(key) && !field.Recording, "Auto-repeat restarted recording or activated a dialog key.");
            field.ReleaseKey(key); Check(!states.Last(), "Old hotkey was not restored after key release.");
        }
        var before = field.SelectedKey;
        field.BeginCapture(); Check(Press(Keys.Escape) && !field.Recording && field.SelectedKey == before, "Esc must cancel recording and preserve the key.");
        Check(!states.Last(), "Esc did not restore the old hotkey.");
        field.BeginCapture(); field.CancelCapture(); Check(!states.Last(), "Focus-loss cancellation did not restore the hotkey.");
        Check(HotkeyKey.Name(Keys.D5) == "5" && HotkeyKey.Name(Keys.OemMinus) == "-", "Key display names are incorrect.");
        Check(HotkeyKey.AllowsStandalone(Keys.PrintScreen) && !HotkeyKey.AllowsStandalone(Keys.A), "Standalone shortcut validation is incorrect.");
        field.BeginCapture(); field.Dispose(); Check(!states.Last(), "Closing the field did not restore the hotkey.");
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
