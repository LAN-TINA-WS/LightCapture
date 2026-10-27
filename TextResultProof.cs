using System.Drawing.Imaging;

namespace LightCapture;

internal static class TextResultProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        using var image = new Bitmap(1200, 640);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
            using var title = new Font("Microsoft YaHei UI", 38, FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new Font("Microsoft YaHei UI", 27, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString("屏幕截图 · 中文识别", title, Brushes.Black, 60, 80);
            graphics.DrawString("点击右侧按键框，再按下想用的键。\n\n原文与译文可以直接编辑、复制或导出。", text, Brushes.DimGray, new RectangleF(60, 190, 1080, 360));
        }
        foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
        foreach (var (width, height, suffix) in new[] { (1260, 860, "wide"), (980, 620, "medium"), (720, 500, "compact") })
        {
            using var pane = new TextResultPane(image, ["英语", "简体中文", "日语", "韩语", "法语", "德语", "西班牙语"]);
            pane.Size = new((int)(width * scale), (int)(height * scale)); pane.Configure(scale); pane.CreateControl();
            pane.Original.Text = "可以呀，点击右侧按键框，再按下想用的键。\r\nCtrl、Alt、Shift 仍然可以分别点选。\r\n原文和译文都可以直接编辑、复制或导出。";
            pane.Translated.Text = "Click the key field on the right, then press the key you want to use.\r\nCtrl, Alt and Shift can still be selected separately.\r\nBoth the original text and the translation can be edited, copied or exported.";
            pane.Source.Input.SelectedIndex = 1; pane.Target.Input.SelectedIndex = 0;
            foreach (string providerName in new[] { "MyMemory", "DeepL", "OpenAI 兼容" })
            {
                pane.SetTranslationProvider(providerName);
                foreach (Control child in pane.Controls) if (child != pane.ResultsViewport) CheckChildren(child);
            }
            pane.Status.Text = "翻译完成 · 原文和译文均可编辑、复制或导出；识图在本机处理，在线翻译仅发送文字。";
            Check(pane.Original.Width >= 240 * scale && pane.Translated.Width >= 240 * scale, "Text columns are too narrow.");
            Check(pane.Original.WordWrap && pane.Translated.WordWrap, "Text must wrap inside its editor.");
            foreach (Control child in pane.Controls)
            {
                Check(pane.ClientRectangle.Contains(child.Bounds), "Translation section clips outside the window.");
                if (child != pane.ResultsViewport) CheckChildren(child);
            }
            foreach (var card in pane.Cards) CheckChildren(card);
            Check(!pane.ResultsViewport.Bounds.IntersectsWith(pane.Status.Parent!.Bounds), "Translation content overlaps the status bar.");
            Check(!pane.ResultsViewport.HorizontalScroll.Visible, "Translation layout requires horizontal scrolling.");
            var footerBounds = pane.Status.Parent.Bounds;
            pane.ResultsViewport.AutoScrollPosition = new(0, 100000);
            var lastCard = pane.Cards.MaxBy(card => card.Bottom)!;
            Check(lastCard.Bottom + lastCard.Parent!.Top <= pane.ResultsViewport.ClientSize.Height, "Translation card cannot be reached by scrolling.");
            Check(pane.Status.Parent.Bounds == footerBounds, "Status bar moved during scrolling.");
            if (scale == 1 && suffix == "compact")
            {
                using var scrolled = new Bitmap(pane.Width, pane.Height);
                pane.DrawToBitmap(scrolled, pane.ClientRectangle); scrolled.Save(Path.Combine(directory, "translation-compact-scrolled.png"), ImageFormat.Png);
            }
            pane.ResultsViewport.AutoScrollPosition = Point.Empty;
            int actions = 0;
            pane.RecognizeRequested += () => actions++; pane.TranslateRequested += () => actions++;
            pane.CopyOriginalRequested += () => actions++; pane.CopyTranslatedRequested += () => actions++;
            pane.ExportTextRequested += () => actions++; pane.ExportWordRequested += () => actions++; pane.CancelRequested += () => actions++;
            string readyStatus = pane.Status.Text;
            pane.SetBusy(true); pane.Status.Text = "正在通过 OpenAI 兼容接口翻译文字…";
            Check(!pane.RecognizeButton.Enabled && !pane.TranslateButton.Enabled && pane.CancelRequestButton.Enabled, "Request buttons do not reflect the busy state.");
            if (scale == 1.5f && suffix == "wide")
            {
                using var busy = new Bitmap(pane.Width, pane.Height);
                pane.DrawToBitmap(busy, pane.ClientRectangle);
                busy.Save(Path.Combine(directory, "translation-150-busy.png"), ImageFormat.Png);
            }
            pane.CancelRequestButton.PerformClick(); pane.SetBusy(false); pane.Status.Text = readyStatus;
            foreach (var button in new[] { pane.RecognizeButton, pane.TranslateButton, pane.CopyOriginalButton, pane.CopyTranslatedButton, pane.ExportTextButton, pane.ExportWordButton }) button.PerformClick();
            Check(actions == 7 && !pane.CancelRequestButton.Enabled, "Translation actions were not routed correctly.");
            string name = $"translation-{(int)(scale * 100)}-{suffix}.png";
            using var bitmap = new Bitmap(pane.Width, pane.Height, PixelFormat.Format32bppArgb);
            pane.DrawToBitmap(bitmap, pane.ClientRectangle); bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
            pane.Configure(1); pane.Configure(scale);
            Check(pane.Original.Text.Contains("点击右侧") && pane.Translated.Text.Contains("Click the key"), "DPI relayout lost edited text.");
            yield return new { name, scale, layout = suffix, textColumnWidth = pane.Original.Width, status = "passed" };
        }
    }
    static void CheckChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Check(parent.ClientRectangle.Contains(child.Bounds), "Translation control clips: " + (child.AccessibleName ?? child.Text));
            if (child is Label label) Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "Translation label text is clipped.");
            if (child is ModernButton button)
                Check(button.Width >= TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 8, "Translation button caption is clipped.");
            if (child is not ComboBox) CheckChildren(child);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
