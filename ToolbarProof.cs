using System.Drawing.Imaging;
using System.Text.Json;

namespace LightCapture;

// Renders our own controls offscreen. This does not capture or automate the desktop.
internal static class ToolbarProof
{
    internal static object[] Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        foreach (bool editor in new[] { false, true })
        foreach (var (scale, maxWidth, suffix) in new (float, int, string)[] {
            (1, 1366 - 24, "100"), (1.25f, 1920 - 30, "125"),
            (1.5f, 1920 - 36, "150"), (2, 1920 - 48, "200"),
            (1.5f, 960 - 36, "150-narrow"), (2, 640, "200-narrow") })
        {
            var settings = new AppSettings { FontSize = 22, StrokeWidth = 3 };
            using var toolbar = new CaptureToolbar(settings, editor);
            toolbar.Configure(scale, maxWidth);
            float effectiveScale = toolbar.UiScale;
            toolbar.CreateControl();
            int collapsedHeight = toolbar.Height;
            Assert(!toolbar.PaletteVisible && !toolbar.BrushModesVisible, "Selection mode must hide brush options.");
            DrawingTool? clickedTool = null;
            ToolbarCommand? clickedCommand = null;
            toolbar.ToolSelected += value => clickedTool = value;
            toolbar.CommandSelected += value => clickedCommand = value;
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Rectangle).PerformClick();
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Save).PerformClick();
            Assert(clickedTool == DrawingTool.Rectangle && clickedCommand == ToolbarCommand.Save, "Icon action routing failed.");
            Assert(!toolbar.PaletteVisible && toolbar.Height > collapsedHeight, "Shape mode must hide the palette and retain stroke options.");
            CheckBounds(toolbar);
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Brush).PerformClick();
            Assert(clickedTool == DrawingTool.Brush && toolbar.PaletteVisible && toolbar.BrushModesVisible, "Brush mode must reveal the palette and mode choices.");
            var numberInputs = toolbar.NumberInputs.ToArray();
            numberInputs[0].Value = 7;
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Text).PerformClick();
            Assert(!toolbar.PaletteVisible && !toolbar.BrushModesVisible, "Text mode must hide the brush palette.");
            numberInputs[1].Value = 96;
            Assert(settings.StrokeWidth == 7 && settings.FontSize == 96, "Toolbar parameter changes were not applied.");
            numberInputs[0].Value = 3; numberInputs[1].Value = 22;
            Assert(toolbar.Width <= maxWidth, "Toolbar exceeds available screen width.");
            CheckBounds(toolbar);
            foreach (var button in toolbar.IconButtons)
            {
                Assert(button.Height >= 44 * effectiveScale && button.Width >= 40 * effectiveScale, "Small icon hit area: " + button.AccessibleName);
                Assert(!string.IsNullOrWhiteSpace(button.AccessibleName), "Missing accessible tool name.");
            }
            foreach (var input in toolbar.NumberInputs)
            {
                int textWidth = TextRenderer.MeasureText(input.Maximum.ToString(), input.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                Assert(input.Width >= textWidth + 38 * effectiveScale, "Number input clips two digits.");
                Assert(input.Height >= input.Font.Height + 4, "Number input clips text height.");
            }
            string prefix = editor ? "editor-" : "toolbar-";
            void Snapshot(string mode)
            {
                CheckBounds(toolbar);
                using var frame = new Bitmap(toolbar.Width, toolbar.Height);
                toolbar.DrawToBitmap(frame, toolbar.ClientRectangle);
                frame.Save(Path.Combine(directory, prefix + mode + "-" + suffix + ".png"), ImageFormat.Png);
            }
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Brush).PerformClick();
            Snapshot("brush");
            toolbar.BrushModeButtons.Single(b => b.Glyph == ToolbarGlyph.Mosaic).PerformClick();
            Assert(clickedTool == DrawingTool.Mosaic && toolbar.BrushModesVisible && !toolbar.PaletteVisible, "Mosaic mode must select the mosaic engine without showing colors.");
            Assert(toolbar.MosaicBrushInput.Visible && toolbar.MosaicBlockInput.Visible && !numberInputs[0].Visible, "Mosaic must show its independent parameters instead of ordinary stroke width.");
            toolbar.MosaicBrushInput.Value = 80;
            Assert(settings.MosaicBrushSize == 80 && settings.MosaicBlockSize == 10 && settings.StrokeWidth == 3, "Brush size changed mosaic granularity or ordinary brush width.");
            toolbar.MosaicBlockInput.Value = 6;
            Assert(settings.MosaicBrushSize == 80 && settings.MosaicBlockSize == 6, "Granularity changed brush size.");
            toolbar.MosaicBrushInput.Value = 60; toolbar.MosaicBlockInput.Value = 10;
            Snapshot("mosaic");
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Select).PerformClick();
            Assert(toolbar.Height == collapsedHeight && !toolbar.PaletteVisible, "Selecting must collapse the brush panel.");
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Brush).PerformClick();
            Assert(clickedTool == DrawingTool.Mosaic, "Brush mode choice should survive switching tools.");
            toolbar.BrushModeButtons.Single(b => b.Glyph == ToolbarGlyph.Brush).PerformClick();
            Assert(clickedTool == DrawingTool.Brush && toolbar.PaletteVisible, "Normal brush must restore color controls.");
            Assert(!toolbar.MosaicBrushInput.Visible && !toolbar.MosaicBlockInput.Visible, "Ordinary brush should hide mosaic-only parameters.");
            // A hidden capture toolbar must still lay out correctly after changing DPI.
            toolbar.Hide(); toolbar.Configure(1, 1342); toolbar.Configure(scale, maxWidth); toolbar.Show();
            Assert(toolbar.PaletteVisible && toolbar.Height > collapsedHeight, "Hidden DPI relayout lost brush options.");
            CheckBounds(toolbar);
            toolbar.IconButtons.Single(b => b.Glyph == ToolbarGlyph.Select).PerformClick();
            string name = (editor ? "editor-" : "toolbar-") + suffix + ".png";
            using var bitmap = new Bitmap(toolbar.Width, toolbar.Height, PixelFormat.Format32bppArgb);
            toolbar.DrawToBitmap(bitmap, toolbar.ClientRectangle);
            bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
            // Reconfiguration must also shrink a previously enlarged toolbar cleanly.
            toolbar.Configure(1, 1342);
            CheckBounds(toolbar);
            results.Add(new { name, scale, maxWidth, width = bitmap.Width, height = bitmap.Height, modesChecked = new[] { "select", "shape", "text", "brush", "mosaic" }, status = "passed" });
        }
        return results.ToArray();
    }

    static void CheckBounds(Control parent)
    {
        var children = parent.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        foreach (var child in children)
        {
            Assert(parent.ClientRectangle.Contains(child.Bounds), "Control clips outside parent: " + (child.AccessibleName ?? child.Name));
            CheckBounds(child);
        }
        // NumericUpDown's native edit/spinner overlap is owned by WinForms.
        if (parent is NumericUpDown) return;
        for (int i = 0; i < children.Length; i++)
        for (int j = i + 1; j < children.Length; j++)
            Assert(!children[i].Bounds.IntersectsWith(children[j].Bounds), "Toolbar controls overlap.");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static int Run(string[] args)
    {
        int index = Array.IndexOf(args, "--artifacts");
        string directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "toolbar-proof");
        Directory.CreateDirectory(directory);
        try
        {
            var results = Generate(directory);
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = results.Length, failed = 0, kind = "offscreen-control-layout", results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = 0, failed = 1, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
}
