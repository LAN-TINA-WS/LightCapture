namespace LightCapture;

internal sealed class TextResultPane : UserControl
{
    readonly UiFonts fonts = new();
    readonly Panel header = new() { BackColor = Ui.Surface }, footer = new() { BackColor = Color.White };
    internal readonly Panel ResultsViewport = new() { AutoScroll = true, BackColor = Ui.Surface };
    readonly Panel results = new() { BackColor = Ui.Surface };
    readonly RoundedPanel languageCard = new(), imageCard = new(), originalCard = new(), translatedCard = new();
    readonly Label title, subtitle, serviceTitle, serviceNote, arrow, imageTitle, imageHint, originalTitle, originalHint, translatedTitle, translatedHint;
    internal readonly Label Status;
    internal readonly TextBox Original = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, WordWrap = true, BorderStyle = BorderStyle.None, BackColor = Color.White, AccessibleName = "识别原文，可编辑" };
    internal readonly TextBox Translated = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, WordWrap = true, BorderStyle = BorderStyle.None, BackColor = Color.White, AccessibleName = "翻译结果，可编辑" };
    internal readonly PictureBox Preview = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Surface, AccessibleName = "原图对照" };
    internal readonly ChoiceField Source = new("原文语言"), Target = new("目标语言");
    internal readonly ModernButton RecognizeButton, TranslateButton, CancelRequestButton, CopyOriginalButton, CopyTranslatedButton, ExportTextButton, ExportWordButton;
    internal event Action? RecognizeRequested, TranslateRequested, CancelRequested, CopyOriginalRequested, CopyTranslatedRequested, ExportTextRequested, ExportWordRequested;
    float scale = 1;
    bool arranging;
    internal float UiScale => scale;
    internal IEnumerable<Control> Cards => [imageCard, originalCard, translatedCard];
    internal TextResultPane(Bitmap image, IEnumerable<string> languages)
    {
        AutoScaleMode = AutoScaleMode.None; BackColor = Ui.Surface; DoubleBuffered = true;
        title = AddLabel(header, "识图与翻译"); subtitle = AddLabel(header, "本机识别 · 原图对照 · 文字可编辑", true);
        RecognizeButton = new("重新识别", () => RecognizeRequested?.Invoke());
        ExportTextButton = new("导出 TXT", () => ExportTextRequested?.Invoke()); ExportWordButton = new("导出 Word", () => ExportWordRequested?.Invoke());
        header.Controls.AddRange([RecognizeButton, ExportTextButton, ExportWordButton]);
        serviceTitle = AddLabel(languageCard, "文字翻译"); serviceNote = AddLabel(languageCard, "MyMemory · 仅发送文字", true);
        arrow = AddLabel(languageCard, "→", true); arrow.TextAlign = ContentAlignment.MiddleCenter;
        Source.Input.Items.AddRange(languages.Cast<object>().ToArray()); Target.Input.Items.AddRange(Source.Input.Items.Cast<object>().ToArray());
        Source.Input.SelectedIndex = 0; Target.Input.SelectedIndex = 1;
        TranslateButton = new("翻译文字", () => TranslateRequested?.Invoke()) { Primary = true };
        CancelRequestButton = new("取消请求", () => CancelRequested?.Invoke()) { Enabled = false };
        languageCard.Controls.AddRange([Source, Target, TranslateButton, CancelRequestButton]);
        imageTitle = AddLabel(imageCard, "原图对照"); imageHint = AddLabel(imageCard, $"{image.Width} × {image.Height} 像素", true);
        Preview.Image = image; imageCard.Controls.Add(Preview);
        originalTitle = AddLabel(originalCard, "原文"); originalHint = AddLabel(originalCard, "识别结果，可直接编辑", true);
        translatedTitle = AddLabel(translatedCard, "译文"); translatedHint = AddLabel(translatedCard, "翻译结果，可直接编辑", true);
        CopyOriginalButton = new("复制原文", () => CopyOriginalRequested?.Invoke()); CopyTranslatedButton = new("复制译文", () => CopyTranslatedRequested?.Invoke());
        originalCard.Controls.AddRange([Original, CopyOriginalButton]); translatedCard.Controls.AddRange([Translated, CopyTranslatedButton]);
        Original.PlaceholderText = "识别后的文字会显示在这里，也可以直接输入或粘贴。";
        Translated.PlaceholderText = "选择语言后点击“翻译文字”，译文会显示在这里。";
        results.Controls.AddRange([imageCard, originalCard, translatedCard]); ResultsViewport.Controls.Add(results);
        Status = AddLabel(footer, "准备本机识别…", true); Status.AccessibleRole = AccessibleRole.StatusBar;
        Controls.AddRange([header, languageCard, ResultsViewport, footer]);
        footer.Paint += (_, e) => { using var p = new Pen(UiShape.Border); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        Status.TextChanged += (_, _) => Arrange(); Resize += (_, _) => Arrange(); Configure(1);
    }
    static Label AddLabel(Control parent, string text, bool muted = false)
    {
        var label = new Label { Text = text, BackColor = parent.BackColor, ForeColor = muted ? UiShape.Muted : Ui.Ink, UseMnemonic = false };
        parent.Controls.Add(label); return label;
    }
    internal void Configure(float value)
    {
        scale = Math.Clamp(value, 1, 4);
        title.Font = fonts.Get(26 * scale, FontStyle.Bold); subtitle.Font = fonts.Get(14 * scale);
        foreach (var label in new[] { serviceTitle, imageTitle, originalTitle, translatedTitle }) label.Font = fonts.Get(16 * scale, FontStyle.Bold);
        foreach (var label in new[] { serviceNote, imageHint, originalHint, translatedHint, Status }) label.Font = fonts.Get(13 * scale);
        arrow.Font = fonts.Get(18 * scale);
        foreach (var button in new[] { RecognizeButton, ExportTextButton, ExportWordButton, TranslateButton, CancelRequestButton }) { button.Font = fonts.Get(15 * scale); button.UiScale = scale; }
        foreach (var button in new[] { CopyOriginalButton, CopyTranslatedButton }) { button.Font = fonts.Get(13 * scale); button.UiScale = scale; }
        foreach (var card in new[] { languageCard, imageCard, originalCard, translatedCard }) card.UiScale = scale;
        Original.Font = Translated.Font = fonts.Get(16 * scale);
        Arrange(); Invalidate(true);
    }
    internal void SetBusy(bool busy)
    {
        RecognizeButton.Enabled = TranslateButton.Enabled = Source.Enabled = Target.Enabled = !busy;
        CancelRequestButton.Enabled = busy;
    }
    internal void SetTranslationProvider(string name)
    {
        serviceNote.Text = name + " · 仅发送文字"; Arrange();
    }
    int S(float value) => (int)Math.Ceiling(value * scale);
    int PlaceLabel(Label label, int x, int y, int width)
    {
        int height = label.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height;
        label.Bounds = new(x, y, Math.Max(1, width), height); return label.Bottom;
    }
    void Arrange()
    {
        if (arranging || Width < S(500) || Height < S(340)) return;
        arranging = true; SuspendLayout();
        try
        {
            int pad = S(28), gap = S(16), width = Width - pad * 2;
            subtitle.Text = Width >= S(1140) ? "本机识别 · 原图对照 · 文字可编辑" : "本机识别 · 文字优先展示，原图可向下滚动查看";
            bool wideHeader = Width >= S(920), wideLanguage = Width >= S(930);
            int headerHeight = S(wideHeader ? 100 : 148);
            header.Bounds = new(0, 0, Width, headerHeight);
            int actionsWidth = S(104 + 12 + 104 + 12 + 124);
            int actionX = wideHeader ? Width - pad - actionsWidth : pad, actionY = S(wideHeader ? 28 : 88);
            PlaceLabel(title, pad, S(20), wideHeader ? actionX - pad - gap : width);
            PlaceLabel(subtitle, pad, S(61), wideHeader ? actionX - pad - gap : width);
            foreach (var (button, logicalWidth) in new[] { (RecognizeButton, 104), (ExportTextButton, 104), (ExportWordButton, 124) })
            {
                button.Bounds = new(actionX, actionY, S(logicalWidth), S(42)); actionX = button.Right + S(12);
            }
            languageCard.Bounds = new(pad, header.Bottom, width, S(wideLanguage ? 88 : 130));
            int inside = S(20), rowY = S(wideLanguage ? 22 : 65), rowX = S(wideLanguage ? 204 : 20);
            PlaceLabel(serviceTitle, inside, S(wideLanguage ? 21 : 17), S(160));
            if (wideLanguage) PlaceLabel(serviceNote, inside, S(48), S(174));
            else PlaceLabel(serviceNote, S(128), S(20), width - S(148));
            Source.Bounds = new(rowX, rowY, S(152), S(44));
            arrow.Bounds = new(Source.Right + S(2), rowY, S(28), S(44));
            Target.Bounds = new(arrow.Right + S(2), rowY, S(152), S(44));
            TranslateButton.Bounds = new(Target.Right + S(16), rowY, S(124), S(44));
            CancelRequestButton.Bounds = new(TranslateButton.Right + S(12), rowY, S(100), S(44));
            Source.Arrange(scale, fonts.Get(15 * scale)); Target.Arrange(scale, fonts.Get(15 * scale));
            int statusHeight = Status.GetPreferredSize(new Size(width, 0)).Height;
            int footerHeight = Math.Max(S(52), statusHeight + S(24));
            footer.Bounds = new(0, Height - footerHeight, Width, footerHeight);
            Status.Bounds = new(pad, (footerHeight - statusHeight) / 2, width, statusHeight);
            int bodyTop = languageCard.Bottom + gap;
            ResultsViewport.Bounds = new(0, bodyTop, Width, Math.Max(1, footer.Top - bodyTop));
            int contentWidth = Math.Max(S(500), ResultsViewport.Width - SystemInformation.VerticalScrollBarWidth);
            int available = contentWidth - pad * 2;
            if (Width >= S(1140))
            {
                int height = Math.Max(S(360), ResultsViewport.ClientSize.Height - gap);
                int imageWidth = (int)((available - gap * 2) * .29f), textWidth = (available - gap * 2 - imageWidth) / 2;
                imageCard.Bounds = new(pad, 0, imageWidth, height);
                originalCard.Bounds = new(imageCard.Right + gap, 0, textWidth, height);
                translatedCard.Bounds = new(originalCard.Right + gap, 0, available - imageWidth - textWidth - gap * 2, height);
            }
            else if (Width >= S(720))
            {
                int height = Math.Max(S(320), ResultsViewport.ClientSize.Height - gap);
                int textWidth = (available - gap) / 2;
                originalCard.Bounds = new(pad, 0, textWidth, height);
                translatedCard.Bounds = new(originalCard.Right + gap, originalCard.Top, available - gap - textWidth, height);
                imageCard.Bounds = new(pad, originalCard.Bottom + gap, available, S(220));
            }
            else
            {
                originalCard.Bounds = new(pad, 0, available, S(280));
                translatedCard.Bounds = new(pad, originalCard.Bottom + gap, available, S(280));
                imageCard.Bounds = new(pad, translatedCard.Bottom + gap, available, S(200));
            }
            LayoutImageCard(); LayoutTextCard(originalCard, originalTitle, originalHint, Original, CopyOriginalButton);
            LayoutTextCard(translatedCard, translatedTitle, translatedHint, Translated, CopyTranslatedButton);
            int contentHeight = Math.Max(imageCard.Bottom, Math.Max(originalCard.Bottom, translatedCard.Bottom)) + gap;
            results.Size = new(contentWidth, contentHeight); ResultsViewport.AutoScrollMinSize = new(0, contentHeight);
        }
        finally { ResumeLayout(false); arranging = false; }
    }
    void LayoutImageCard()
    {
        PlaceLabel(imageTitle, S(20), S(18), imageCard.Width - S(40));
        PlaceLabel(imageHint, S(20), S(48), imageCard.Width - S(40));
        Preview.Bounds = new(S(16), S(79), imageCard.Width - S(32), Math.Max(1, imageCard.Height - S(95)));
    }
    void LayoutTextCard(RoundedPanel card, Label heading, Label hint, TextBox input, ModernButton copy)
    {
        PlaceLabel(heading, S(20), S(18), card.Width - S(132));
        copy.Bounds = new(card.Width - S(108), S(14), S(88), S(34));
        PlaceLabel(hint, S(20), S(48), card.Width - S(40));
        input.Bounds = new(S(20), S(83), card.Width - S(40), Math.Max(1, card.Height - S(103)));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Preview.Image = null;
        base.Dispose(disposing); if (disposing) fonts.Dispose();
    }
}
