using System.Drawing.Imaging;

namespace LightCapture;

internal static class CapturePreviewProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        using var source = new Bitmap(180, 140, PixelFormat.Format24bppRgb);
        var viewport = new Rectangle(Point.Empty, source.Size);
        var window = new Rectangle(20, 15, 140, 105);
        using (var graphics = Graphics.FromImage(source)) { graphics.Clear(Color.FromArgb(125, 145, 165)); graphics.FillRectangle(Brushes.White, window); }
        foreach (string state in new[] { "hover", "switch-window", "selection", "no-candidate", "polygon-pending", "polygon-closed", "whole-screen" })
        {
            Rectangle selection = Rectangle.Empty, hover = window;
            IReadOnlyList<PointF>? polygon = null;
            bool pending = state == "polygon-pending";
            Point bright = new(80, 60), dim = new(4, 4);
            bool hasBright = true, hasDim = true;
            switch (state)
            {
                case "switch-window": hover = new(80, 30, 70, 70); bright = new(100, 50); dim = new(35, 50); break;
                case "selection": selection = new(40, 35, 40, 35); bright = new(50, 50); dim = new(100, 60); break;
                case "no-candidate": hover = Rectangle.Empty; hasBright = false; dim = new(80, 60); break;
                case "polygon-pending": hasBright = false; dim = new(80, 60); break;
                case "polygon-closed":
                    selection = new(40, 30, 80, 80); polygon = [new(40, 30), new(120, 30), new(40, 110)]; bright = new(50, 40); dim = new(100, 90); break;
                case "whole-screen": hover = viewport; hasDim = false; break;
            }
            using var frame = new Bitmap(source);
            using (var graphics = Graphics.FromImage(frame)) CapturePreview.DrawMask(graphics, viewport, CapturePreview.ActiveRectangle(selection, hover, pending), polygon);
            if (hasBright) Check(frame.GetPixel(bright.X, bright.Y).ToArgb() == source.GetPixel(bright.X, bright.Y).ToArgb(), "Candidate or selection was darkened: " + state);
            if (hasDim) Check(frame.GetPixel(dim.X, dim.Y).R < source.GetPixel(dim.X, dim.Y).R, "Outside region was not darkened: " + state);
            yield return new { name = "capture-preview-" + state, status = "passed" };
        }
        foreach (int thickness in Enumerable.Range(1, 6))
        {
            foreach (float dpi in new[] { 1, 1.5f, 2, 3 })
            {
                using var frame = new Bitmap(160, 120);
                var rect = new Rectangle(20, 20, 100, 80);
                using (var graphics = Graphics.FromImage(frame)) { graphics.Clear(Color.White); CapturePreview.DrawOutline(graphics, rect, thickness, dpi); }
                int pixels = (int)Math.Ceiling(thickness * dpi);
                Check(frame.GetPixel(rect.Left + pixels - 1, 60).ToArgb() == CapturePreview.BorderColor.ToArgb(), "Preview border is thinner than configured.");
                Check(frame.GetPixel(rect.Left + pixels, 60).ToArgb() == Color.White.ToArgb(), "Preview border is thicker than configured.");
                Check(frame.GetPixel(70, rect.Top + pixels - 1).ToArgb() == CapturePreview.BorderColor.ToArgb(), "Top preview border has incorrect width.");
                Check(frame.GetPixel(rect.Left - 1, 60).ToArgb() == Color.White.ToArgb(), "Preview border extends outside the candidate.");
            }
            yield return new { name = "preview-border-width-" + thickness, thickness, dpiCases = 4, status = "passed" };
        }
        SavePreview(directory);
    }
    static void SavePreview(string directory)
    {
        using var scene = new Bitmap(940, 600, PixelFormat.Format24bppRgb);
        var window = new Rectangle(80, 52, 780, 482);
        using var fonts = new UiFonts();
        using (var graphics = Graphics.FromImage(scene))
        {
            graphics.Clear(Color.FromArgb(117, 137, 163)); graphics.FillRectangle(Brushes.White, window);
            using var header = new SolidBrush(Ui.Surface); graphics.FillRectangle(header, window.X, window.Y, window.Width, 48);
            graphics.FillRectangle(header, window.X, window.Y + 48, 150, window.Height - 48);
            graphics.DrawString("文件资源管理器", fonts.Get(17, FontStyle.Bold), Brushes.Black, window.X + 18, window.Y + 14);
            graphics.DrawString("文档\n\n图片\n\n下载\n\n此电脑", fonts.Get(15), Brushes.SlateGray, window.X + 20, window.Y + 80);
            graphics.DrawString("名称                                       类型", fonts.Get(14), Brushes.SlateGray, window.X + 178, window.Y + 72);
            graphics.DrawString("Documents\n\nPictures\n\nDownloads\n\nLightCapture.exe", fonts.Get(16), Brushes.Black, window.X + 180, window.Y + 118);
            graphics.FillRectangle(Brushes.DimGray, 0, 558, scene.Width, 42);
        }
        using var frame = new Bitmap(scene);
        using (var graphics = Graphics.FromImage(frame))
        {
            CapturePreview.DrawMask(graphics, new(Point.Empty, frame.Size), window);
            CapturePreview.DrawOutline(graphics, window, 2, 1.5f);
            using var magnifier = new CaptureMagnifier();
            var lens = magnifier.Measure(graphics, 1);
            magnifier.Draw(graphics, scene, new(604, 282), new(625, 304), lens, window.Size);
        }
        frame.Save(Path.Combine(directory, "window-preselection.png"), ImageFormat.Png);
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
