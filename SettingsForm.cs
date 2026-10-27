using Microsoft.Win32;

namespace LightCapture;

internal sealed class SettingsForm : Form
{
    readonly SettingsPane pane;
    readonly AppSettings settings;
    readonly Func<uint, Keys, bool> register;
    readonly Action changed;
    internal event Action<bool>? KeyCaptureChanged;
    internal SettingsForm(AppSettings settings, Func<uint, Keys, bool> register, Action changed)
    {
        this.settings = settings; this.register = register; this.changed = changed;
        Text = "轻截 · 设置"; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None; BackColor = Ui.Surface; Icon = Ui.CreateIcon();
        MinimizeBox = false;
        float scale = Native.DpiScaleAt(Cursor.Position);
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new(Math.Min((int)(780 * scale), work.Width - 48), Math.Min((int)(810 * scale), work.Height - 64));
        MinimumSize = SizeFromClientSize(new(Math.Min((int)(560 * scale), ClientSize.Width), Math.Min((int)(400 * scale), ClientSize.Height)));
        pane = new(settings, SaveSettings, Close) { Dock = DockStyle.Fill };
        pane.KeyCapture.CaptureStateChanged += recording => KeyCaptureChanged?.Invoke(recording);
        Controls.Add(pane); pane.Configure(scale);
        AcceptButton = pane.SaveButton; CancelButton = pane.CancelButton;
        DpiChanged += (_, e) => pane.Configure(e.DeviceDpiNew / 96f);
        Shown += (_, _) => pane.Configure(DeviceDpi / 96f);
        Deactivate += (_, _) => pane.KeyCapture.CancelCapture();
    }
    void SaveSettings()
    {
        TranslationPreferences translation;
        try { translation = pane.TranslationCard.ReadPreferences(); translation.Validate(); }
        catch (Exception ex) { Ui.Error(this, ex, "翻译后端设置"); return; }
        uint modifiers = pane.Modifiers;
        if (pane.SelectedKey is not Keys selectedKey) return;
        if (modifiers == 0 && !HotkeyKey.AllowsStandalone(selectedKey))
        {
            MessageBox.Show(this, "这个按键需要搭配 Ctrl、Alt 或 Shift；F1–F24 和 Print Screen 可以单独使用。", "选择组合键"); return;
        }
        if (!register(modifiers, selectedKey))
        {
            MessageBox.Show(this, "这组快捷键已被占用，请换一个组合。原快捷键已保留。", "快捷键冲突"); return;
        }
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (pane.Startup) run.SetValue("LightCapture", "\"" + Environment.ProcessPath + "\"");
            else if (settings.StartWithWindows) run.DeleteValue("LightCapture", false);
            settings.HotkeyModifiers = modifiers; settings.Hotkey = selectedKey;
            settings.StartWithWindows = pane.Startup; settings.FfmpegPath = pane.EncoderPath;
            settings.ToolbarSizePercent = pane.ToolbarSizePercent; settings.TrayMenuSizePercent = pane.TrayMenuSizePercent;
            settings.PreviewBorderThickness = pane.PreviewBorderThickness;
            settings.OcrLanguage = pane.OcrLanguage; settings.Translation = translation; settings.Save(); changed(); Close();
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
}

internal sealed class SettingsPane : UserControl
{
    readonly UiFonts fonts = new();
    readonly Panel header = new() { BackColor = Ui.Surface }, footer = new() { BackColor = Color.White };
    internal readonly Panel ScrollBody = new() { AutoScroll = true, BackColor = Ui.Surface };
    readonly Panel content = new() { BackColor = Ui.Surface };
    readonly RoundedPanel hotkeyCard = new(), startupCard = new(), encoderCard = new(), ocrCard = new();
    internal readonly RoundedPanel SizeCard = new();
    internal readonly TranslationSettingsCard TranslationCard;
    readonly Label sizeTitle, sizeDescription, toolbarSizeLabel, traySizeLabel, toolbarPercent, trayPercent, borderThicknessLabel;
    internal readonly ChoiceField PreviewBorderField = new("预选窗口蓝框粗细");
    readonly Panel borderSample = new() { BackColor = Color.White };
    internal readonly SizeSlider ToolbarSizeSlider = new("截图工具栏大小"), TrayMenuSizeSlider = new("托盘菜单大小");
    internal readonly ModernButton ResetSizesButton;
    readonly Label title, subtitle, keyTitle, keyDescription, startupTitle, startupDescription, encoderTitle, encoderDescription, ocrTitle, ocrDescription, privacy, footerHint;
    readonly ModifierChip ctrl, alt, shift;
    readonly ModernSwitch startup = new();
    internal readonly KeyCaptureField KeyCapture = new();
    readonly ChoiceField ocr = new("本地识别语言");
    readonly RoundedPanel encoderFrame = new() { Field = true };
    readonly TextBox encoder = new() { BorderStyle = BorderStyle.None };
    readonly ModernButton browse;
    internal readonly ModernButton SaveButton, CancelButton;
    float scale = 1;
    bool arranging;
    internal uint Modifiers => (ctrl.Checked ? 2u : 0) | (alt.Checked ? 1u : 0) | (shift.Checked ? 4u : 0);
    internal Keys? SelectedKey => KeyCapture.SelectedKey;
    internal bool Startup => startup.Checked;
    internal string EncoderPath => encoder.Text.Trim();
    internal string OcrLanguage => ocr.Input.SelectedIndex == 0 ? "" : ocr.Input.Text;
    internal int ToolbarSizePercent => ToolbarSizeSlider.Value;
    internal int TrayMenuSizePercent => TrayMenuSizeSlider.Value;
    internal int PreviewBorderThickness => PreviewBorderField.Input.SelectedIndex + 1;
    internal float UiScale => scale;
    internal IEnumerable<Control> Cards => [hotkeyCard, SizeCard, startupCard, encoderCard, ocrCard, TranslationCard];

    internal SettingsPane(AppSettings settings, Action save, Action cancel)
    {
        AutoScaleMode = AutoScaleMode.None; BackColor = Ui.Surface; DoubleBuffered = true;
        title = Label(header, "轻截设置"); subtitle = Label(header, "快捷键、界面与翻译服务", true);
        keyTitle = Label(hotkeyCard, "截图快捷键"); keyDescription = Label(hotkeyCard, "点击右侧按键框，再按下需要的按键；Esc 取消录入。", true);
        ctrl = new("Ctrl", (settings.HotkeyModifiers & 2) != 0); alt = new("Alt", (settings.HotkeyModifiers & 1) != 0); shift = new("Shift", (settings.HotkeyModifiers & 4) != 0);
        hotkeyCard.Controls.AddRange([ctrl, alt, shift, KeyCapture]);
        KeyCapture.SelectedKey = settings.Hotkey;
        sizeTitle = Label(SizeCard, "界面外观"); sizeDescription = Label(SizeCard, "大小默认 75%；预选蓝框默认 2 档，保存后生效。", true);
        toolbarSizeLabel = Label(SizeCard, "截图工具栏"); traySizeLabel = Label(SizeCard, "托盘菜单");
        toolbarPercent = Label(SizeCard, ""); trayPercent = Label(SizeCard, "");
        toolbarPercent.ForeColor = trayPercent.ForeColor = Ui.Blue;
        toolbarPercent.TextAlign = trayPercent.TextAlign = ContentAlignment.MiddleRight;
        ToolbarSizeSlider.Value = settings.ToolbarSizePercent; TrayMenuSizeSlider.Value = settings.TrayMenuSizePercent;
        void UpdateSizes() { toolbarPercent.Text = ToolbarSizePercent + "%"; trayPercent.Text = TrayMenuSizePercent + "%"; }
        ToolbarSizeSlider.ValueChanged += UpdateSizes; TrayMenuSizeSlider.ValueChanged += UpdateSizes; UpdateSizes();
        borderThicknessLabel = Label(SizeCard, "预选蓝框粗细");
        PreviewBorderField.Input.Items.AddRange(Enumerable.Range(1, 6).Select(n => (object)$"{n} 档").ToArray());
        PreviewBorderField.Input.SelectedIndex = settings.PreviewBorderThickness - 1;
        PreviewBorderField.Input.SelectedIndexChanged += (_, _) => borderSample.Invalidate();
        borderSample.Paint += (_, e) => CapturePreview.DrawOutline(e.Graphics, Rectangle.Inflate(borderSample.ClientRectangle, -(int)(6 * scale), -(int)(6 * scale)), PreviewBorderThickness, scale);
        ResetSizesButton = new("恢复默认", () => { ToolbarSizeSlider.Value = UiSizing.Default; TrayMenuSizeSlider.Value = UiSizing.Default; PreviewBorderField.Input.SelectedIndex = 1; });
        SizeCard.Controls.AddRange([ToolbarSizeSlider, TrayMenuSizeSlider, PreviewBorderField, borderSample, ResetSizesButton]);
        startupTitle = Label(startupCard, "登录时启动"); startupDescription = Label(startupCard, "登录 Windows 后，自动在托盘中待命。", true);
        startup.Checked = settings.StartWithWindows; startupCard.Controls.Add(startup);
        encoderTitle = Label(encoderCard, "录屏编码器"); encoderDescription = Label(encoderCard, "留空即可使用随程序附带的 FFmpeg。", true);
        encoder.Text = settings.FfmpegPath; encoder.AccessibleName = "FFmpeg 编码器路径"; encoderFrame.Controls.Add(encoder);
        browse = new("浏览…", () =>
        {
            using var dialog = new OpenFileDialog { Title = "选择 FFmpeg 编码器", Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe" };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) encoder.Text = dialog.FileName;
        });
        encoderCard.Controls.AddRange([encoderFrame, browse]);
        ocrTitle = Label(ocrCard, "本地识别语言"); ocrDescription = Label(ocrCard, "使用 Windows 已安装的 OCR 语言包。", true);
        ocr.Input.Items.Add("自动选择本机语言");
        try { foreach (var language in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages) ocr.Input.Items.Add(language.LanguageTag); } catch { }
        ocr.Input.SelectedIndex = 0;
        if (ocr.Input.Items.Contains(settings.OcrLanguage)) ocr.Input.SelectedItem = settings.OcrLanguage;
        ocrCard.Controls.Add(ocr);
        TranslationCard = new(settings.Translation);
        TranslationCard.PreferredHeightChanged += Arrange;
        privacy = Label(content, "识图在本机处理。使用翻译功能时，仅向所选后端发送文字，图片不会发送给翻译服务。\n托盘图标的位置由 Windows 通知区域设置管理。", true);
        footerHint = Label(footer, "保存后立即生效", true);
        SaveButton = new("保存设置", save) { Primary = true, AccessibleName = "保存设置" };
        CancelButton = new("取消", cancel) { DialogResult = DialogResult.Cancel };
        footer.Controls.AddRange([CancelButton, SaveButton]);
        content.Controls.AddRange([hotkeyCard, SizeCard, startupCard, encoderCard, ocrCard, TranslationCard]);
        ScrollBody.Controls.Add(content); Controls.AddRange([header, ScrollBody, footer]);
        footer.Paint += (_, e) => { using var pen = new Pen(UiShape.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        Resize += (_, _) => Arrange(); Configure(1);
    }
    static Label Label(Control parent, string text, bool muted = false)
    {
        var label = new Label { Text = text, ForeColor = muted ? UiShape.Muted : Ui.Ink, BackColor = parent.BackColor, UseMnemonic = false };
        parent.Controls.Add(label); return label;
    }
    internal void Configure(float value)
    {
        scale = Math.Clamp(value, 1, 4);
        title.Font = fonts.Get(26 * scale, FontStyle.Bold); subtitle.Font = fonts.Get(14 * scale);
        foreach (var label in new[] { keyTitle, sizeTitle, startupTitle, encoderTitle, ocrTitle }) label.Font = fonts.Get(16 * scale, FontStyle.Bold);
        foreach (var label in new[] { keyDescription, sizeDescription, startupDescription, encoderDescription, ocrDescription, privacy, footerHint }) label.Font = fonts.Get(13 * scale);
        toolbarSizeLabel.Font = traySizeLabel.Font = borderThicknessLabel.Font = fonts.Get(14 * scale);
        toolbarPercent.Font = trayPercent.Font = fonts.Get(14 * scale, FontStyle.Bold);
        ToolbarSizeSlider.UiScale = TrayMenuSizeSlider.UiScale = scale;
        ResetSizesButton.Font = fonts.Get(13 * scale); ResetSizesButton.UiScale = scale;
        foreach (var chip in new[] { ctrl, alt, shift }) { chip.Font = fonts.Get(15 * scale); chip.UiScale = scale; }
        foreach (var button in new[] { SaveButton, CancelButton, browse }) { button.Font = fonts.Get(15 * scale); button.UiScale = scale; }
        foreach (var card in new[] { hotkeyCard, SizeCard, startupCard, encoderCard, ocrCard, encoderFrame }) card.UiScale = scale;
        startup.UiScale = scale; encoder.Font = fonts.Get(15 * scale); Arrange(); Invalidate(true);
        KeyCapture.UiScale = scale; KeyCapture.Font = fonts.Get(15 * scale);
    }
    int S(float value) => (int)Math.Ceiling(value * scale);
    int PlaceLabel(Label label, int x, int y, int width)
    {
        int height = label.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height;
        label.Bounds = new(x, y, Math.Max(1, width), height); return label.Bottom;
    }
    void Arrange()
    {
        if (arranging || Width < S(300) || Height < S(200)) return;
        arranging = true; SuspendLayout();
        try
        {
            header.Bounds = new(0, 0, Width, S(100));
            PlaceLabel(title, S(32), S(22), Width - S(64)); PlaceLabel(subtitle, S(32), S(62), Width - S(64));
            footer.Bounds = new(0, Height - S(80), Width, S(80));
            SaveButton.Bounds = new(Width - S(172), S(18), S(140), S(44));
            CancelButton.Bounds = new(SaveButton.Left - S(108), S(18), S(96), S(44));
            footerHint.Visible = CancelButton.Left > S(230);
            PlaceLabel(footerHint, S(32), (footer.Height - footerHint.Font.Height) / 2, Math.Max(1, CancelButton.Left - S(48)));
            ScrollBody.Bounds = new(0, header.Bottom, Width, Math.Max(1, footer.Top - header.Bottom));
            int contentWidth = Math.Max(S(300), ScrollBody.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int padding = S(32), cardWidth = contentWidth - padding * 2, inner = S(22), usable = cardWidth - inner * 2;
            int y = S(4);
            int HotkeyLayout()
            {
                int at = PlaceLabel(keyTitle, inner, S(20), usable) + S(5);
                at = PlaceLabel(keyDescription, inner, at, usable) + S(14);
                int x = inner;
                foreach (var chip in new[] { ctrl, alt, shift }) { chip.Bounds = new(x, at, S(80), S(44)); x += S(90); }
                KeyCapture.Bounds = new(x + S(4), at, S(132), S(44));
                return at + S(64);
            }
            hotkeyCard.Bounds = new(padding, y, cardWidth, HotkeyLayout()); y = hotkeyCard.Bottom + S(12);
            int sizeAt = PlaceLabel(sizeTitle, inner, S(20), usable - S(108)) + S(5);
            ResetSizesButton.Bounds = new(cardWidth - inner - S(96), S(14), S(96), S(34));
            sizeAt = PlaceLabel(sizeDescription, inner, sizeAt, usable) + S(12);
            void SizeRow(Label label, SizeSlider slider, Label percent)
            {
                PlaceLabel(label, inner, sizeAt, S(114));
                label.Top += (S(44) - label.Height) / 2;
                slider.Bounds = new(inner + S(126), sizeAt, usable - S(190), S(44));
                percent.Bounds = new(cardWidth - inner - S(52), sizeAt, S(52), S(44));
                sizeAt += S(48);
            }
            SizeRow(toolbarSizeLabel, ToolbarSizeSlider, toolbarPercent); SizeRow(traySizeLabel, TrayMenuSizeSlider, trayPercent);
            PlaceLabel(borderThicknessLabel, inner, sizeAt, S(114)); borderThicknessLabel.Top += (S(44) - borderThicknessLabel.Height) / 2;
            borderSample.Bounds = new(inner + S(126), sizeAt, usable - S(242), S(44));
            PreviewBorderField.Bounds = new(cardWidth - inner - S(104), sizeAt, S(104), S(44));
            PreviewBorderField.Arrange(scale, fonts.Get(15 * scale));
            sizeAt += S(48);
            SizeCard.Bounds = new(padding, y, cardWidth, sizeAt + S(14)); y = SizeCard.Bottom + S(12);
            int startAt = PlaceLabel(startupTitle, inner, S(20), usable - S(78)) + S(5);
            int startBottom = PlaceLabel(startupDescription, inner, startAt, usable - S(78));
            startupCard.Bounds = new(padding, y, cardWidth, Math.Max(S(90), startBottom + S(20)));
            startup.Bounds = new(cardWidth - inner - S(50), (startupCard.Height - S(28)) / 2, S(50), S(28)); y = startupCard.Bottom + S(12);
            int encAt = PlaceLabel(encoderTitle, inner, S(20), usable) + S(5);
            encAt = PlaceLabel(encoderDescription, inner, encAt, usable) + S(14);
            encoderFrame.Bounds = new(inner, encAt, usable - S(100), S(44));
            encoder.Bounds = new(S(12), (encoderFrame.Height - encoder.PreferredHeight) / 2, encoderFrame.Width - S(24), encoder.PreferredHeight);
            browse.Bounds = new(encoderFrame.Right + S(10), encAt, S(90), S(44));
            encoderCard.Bounds = new(padding, y, cardWidth, encAt + S(64)); y = encoderCard.Bottom + S(12);
            int ocrAt = PlaceLabel(ocrTitle, inner, S(20), usable) + S(5);
            ocrAt = PlaceLabel(ocrDescription, inner, ocrAt, usable) + S(14);
            ocr.Bounds = new(inner, ocrAt, usable, S(44)); ocr.Arrange(scale, fonts.Get(15 * scale));
            ocrCard.Bounds = new(padding, y, cardWidth, ocrAt + S(64)); y = ocrCard.Bottom + S(18);
            TranslationCard.Configure(scale, cardWidth); TranslationCard.Location = new(padding, y); y = TranslationCard.Bottom + S(18);
            int bottom = PlaceLabel(privacy, padding + S(4), y, cardWidth - S(8)) + S(24);
            content.Size = new(contentWidth, bottom);
            ScrollBody.AutoScrollMinSize = new(0, bottom);
        }
        finally { ResumeLayout(false); arranging = false; }
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) fonts.Dispose(); }
}

internal sealed class HelpForm : Form
{
    readonly UiFonts fonts = new();
    public HelpForm(AppSettings settings)
    {
        Text = "轻截 · 使用说明"; AutoScaleMode = AutoScaleMode.None;
        float scale = Native.DpiScaleAt(Cursor.Position); var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new(Math.Min((int)(720 * scale), work.Width - 48), Math.Min((int)(720 * scale), work.Height - 64));
        StartPosition = FormStartPosition.CenterScreen; BackColor = Ui.Surface; Icon = Ui.CreateIcon();
        var body = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Ui.Surface, ForeColor = Ui.Ink,
            Font = fonts.Get(15 * scale), Dock = DockStyle.Fill,
            Text = $"轻截 {Application.ProductVersion}\r\n\r\n启动后常驻右下角系统托盘。\r\n{settings.HotkeyText}：截图；左键托盘图标也可以截图。\r\n右键托盘图标：模式、图片编辑、设置与退出。\r\n\r\n截图与标注\r\n拖动选择区域，单击吸附窗口；拖动边角调整选区。\r\n右键直接取消本次截图，后台继续常驻。\r\n选择工具：拖动对象移动；拖动对象右下角缩放；Delete 删除。\r\n文字：支持中文与换行；Ctrl+Enter 提交；Esc 取消本次输入。\r\n自由选区：逐点选择，双击闭合；右键退出截图。\r\nC 复制当前色号；方向键微调，Shift+方向键移动10像素。\r\n\r\nCtrl+Enter：完成并复制图片\r\nCtrl+S：另存为 PNG、JPEG 或 BMP\r\nCtrl+Z / Ctrl+Y：撤销 / 重做\r\nCtrl+T：钉在桌面\r\nEsc：退出截图\r\n\r\n贴图：拖动图片移动；滚轮缩放；Ctrl+滚轮调整透明度。\r\n长图：滚动原窗口，暂停后自动拼接，也可点“追加”。\r\n识图：使用 Windows 本机 OCR 语言包。\r\n翻译：在偏好设置中选择 MyMemory、OpenAI 兼容接口或 DeepL；填写参数后可测试连接，仅发送文字。\r\n录屏：区域 MP4，系统声音与麦克风可分别开启。\r\n\r\n设置和错误日志：\r\n{AppSettings.DataDirectory}" };
        Padding = new Padding((int)(28 * scale)); Controls.Add(body);
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) fonts.Dispose(); }
}
