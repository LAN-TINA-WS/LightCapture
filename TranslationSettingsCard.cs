namespace LightCapture;

internal sealed class TextEntryField : RoundedPanel
{
    internal readonly TextBox Input = new() { BorderStyle = BorderStyle.None, BackColor = Color.White };
    internal readonly ModernButton? RevealButton;
    internal TextEntryField(string name, string placeholder, bool secret = false)
    {
        Field = true; Input.AccessibleName = name; Input.PlaceholderText = placeholder; Input.UseSystemPasswordChar = secret;
        Controls.Add(Input);
        if (secret)
        {
            RevealButton = new("显示", () =>
            {
                Input.UseSystemPasswordChar = !Input.UseSystemPasswordChar;
                RevealButton!.Text = Input.UseSystemPasswordChar ? "显示" : "隐藏";
                RevealButton.AccessibleName = RevealButton.Text + name;
            }) { AccessibleName = "显示" + name };
            Controls.Add(RevealButton);
        }
    }
    internal void Configure(float scale, Font font)
    {
        UiScale = scale; Input.Font = font;
        int inset = (int)Math.Ceiling(12 * scale), reserved = RevealButton == null ? 0 : (int)Math.Ceiling(66 * scale);
        Input.Bounds = new(inset, (Height - Input.PreferredHeight) / 2, Math.Max(24, Width - inset * 2 - reserved), Input.PreferredHeight);
        if (RevealButton != null)
        {
            RevealButton.Font = font; RevealButton.UiScale = scale;
            RevealButton.Bounds = new(Width - reserved - inset / 2, (int)(5 * scale), reserved, Height - (int)(10 * scale));
        }
    }
}

internal sealed class TranslationSettingsCard : RoundedPanel
{
    readonly UiFonts fonts = new();
    readonly Label title, description, providerLabel, explanation, addressLabel, modelLabel, keyLabel, planLabel, secretNote, testNote, testStatus;
    internal readonly ChoiceField ProviderField = new("翻译后端"), DeepLPlanField = new("DeepL API 账户类型");
    internal readonly TextEntryField AddressField = new("OpenAI 兼容接口地址", "https://api.openai.com/v1");
    internal readonly TextEntryField ModelField = new("翻译模型名称", "填写服务商提供的模型名称");
    internal readonly TextEntryField OpenAiKeyField = new("OpenAI 兼容接口密钥", "本地无鉴权服务可留空", true);
    internal readonly TextEntryField DeepLKeyField = new("DeepL API 密钥", "填写 DeepL API 密钥", true);
    internal readonly ModernButton TestButton;
    internal event Action? PreferredHeightChanged;
    CancellationTokenSource? testOperation;
    int testGeneration;
    float scale = 1;
    internal TranslationBackend SelectedBackend => (TranslationBackend)Math.Max(0, ProviderField.Input.SelectedIndex);
    internal TranslationSettingsCard(TranslationPreferences settings)
    {
        title = AddLabel("翻译后端"); description = AddLabel("选择文字翻译服务；截图识别仍在本机进行。", true);
        providerLabel = AddLabel("使用的服务"); explanation = AddLabel("", true);
        addressLabel = AddLabel("API 地址（基础地址或完整 /chat/completions 地址）");
        modelLabel = AddLabel("模型名称"); keyLabel = AddLabel("API 密钥"); planLabel = AddLabel("DeepL API 账户类型");
        secretNote = AddLabel("密钥仅在当前 Windows 账户下加密保存。", true);
        testNote = AddLabel("发送固定测试句 Hello world.，可能产生服务用量。", true);
        testStatus = AddLabel("尚未测试当前配置。", true);
        TestButton = new("测试连接", () => _ = TestAsync());
        ProviderField.Input.Items.AddRange(["MyMemory（默认）", "OpenAI 兼容接口", "DeepL"]);
        DeepLPlanField.Input.Items.AddRange(["DeepL API Free", "DeepL API Pro"]);
        ProviderField.Input.SelectedIndex = (int)settings.Backend; DeepLPlanField.Input.SelectedIndex = settings.DeepLFreeApi ? 0 : 1;
        AddressField.Input.Text = settings.OpenAiBaseUrl; ModelField.Input.Text = settings.OpenAiModel;
        OpenAiKeyField.Input.Text = settings.OpenAiApiKey; DeepLKeyField.Input.Text = settings.DeepLApiKey;
        Controls.AddRange([ProviderField, DeepLPlanField, AddressField, ModelField, OpenAiKeyField, DeepLKeyField, TestButton]);
        ProviderField.Input.SelectedIndexChanged += (_, _) => { InvalidateTest(); Configure(scale, Width); PreferredHeightChanged?.Invoke(); };
        DeepLPlanField.Input.SelectedIndexChanged += (_, _) => InvalidateTest();
        foreach (var field in new[] { AddressField, ModelField, OpenAiKeyField, DeepLKeyField }) field.Input.TextChanged += (_, _) => InvalidateTest();
        Configure(1, 700);
    }
    Label AddLabel(string text, bool muted = false)
    {
        var label = new Label { Text = text, BackColor = Color.White, ForeColor = muted ? UiShape.Muted : Ui.Ink, UseMnemonic = false };
        Controls.Add(label); return label;
    }
    internal TranslationPreferences ReadPreferences() => new()
    {
        Provider = SelectedBackend switch { TranslationBackend.OpenAiCompatible => "openai", TranslationBackend.DeepL => "deepl", _ => "mymemory" },
        OpenAiBaseUrl = AddressField.Input.Text.Trim(), OpenAiModel = ModelField.Input.Text.Trim(),
        OpenAiApiKey = OpenAiKeyField.Input.Text, DeepLApiKey = DeepLKeyField.Input.Text, DeepLFreeApi = DeepLPlanField.Input.SelectedIndex == 0
    };
    int S(float value) => (int)Math.Ceiling(value * scale);
    int PlaceLabel(Label label, int y, int width)
    {
        label.Bounds = new(S(22), y, width, label.GetPreferredSize(new(Math.Max(1, width), 0)).Height); return label.Bottom;
    }
    internal void Configure(float value, int width)
    {
        scale = value; UiScale = value; Width = width;
        title.Font = fonts.Get(16 * scale, FontStyle.Bold);
        foreach (var label in new[] { description, explanation, secretNote, testNote, testStatus }) label.Font = fonts.Get(13 * scale);
        foreach (var label in new[] { providerLabel, addressLabel, modelLabel, keyLabel, planLabel }) label.Font = fonts.Get(14 * scale);
        TestButton.Font = fonts.Get(14 * scale); TestButton.UiScale = scale;
        int usable = Math.Max(S(180), Width - S(44));
        int y = PlaceLabel(title, S(20), usable) + S(5);
        y = PlaceLabel(description, y, usable) + S(16);
        void Row(Label label, Control field)
        {
            y = PlaceLabel(label, y, usable) + S(7);
            field.Bounds = new(S(22), y, usable, S(44));
            if (field is ChoiceField choice) choice.Arrange(scale, fonts.Get(15 * scale));
            if (field is TextEntryField entry) entry.Configure(scale, fonts.Get(15 * scale));
            y = field.Bottom + S(14);
        }
        Row(providerLabel, ProviderField);
        bool openAi = SelectedBackend == TranslationBackend.OpenAiCompatible, deepL = SelectedBackend == TranslationBackend.DeepL;
        explanation.Text = openAi ? "使用 Chat Completions 格式；模型名称以服务商提供的为准。"
            : deepL ? "请选择与你的 API 账户对应的 Free 或 Pro。" : "无需填写密钥，继续使用 MyMemory 公共翻译接口。";
        y = PlaceLabel(explanation, y - S(4), usable) + S(16);
        foreach (var control in new Control[] { addressLabel, AddressField, modelLabel, ModelField }) control.Visible = openAi;
        planLabel.Visible = DeepLPlanField.Visible = deepL;
        keyLabel.Visible = secretNote.Visible = openAi || deepL;
        OpenAiKeyField.Visible = openAi; DeepLKeyField.Visible = deepL;
        if (openAi) { Row(addressLabel, AddressField); Row(modelLabel, ModelField); Row(keyLabel, OpenAiKeyField); }
        if (deepL) { Row(planLabel, DeepLPlanField); Row(keyLabel, DeepLKeyField); }
        if (openAi || deepL) y = PlaceLabel(secretNote, y - S(2), usable) + S(14);
        y = PlaceLabel(testNote, y, usable) + S(12);
        TestButton.Bounds = new(S(22), y, S(112), S(40)); y = TestButton.Bottom + S(10);
        y = PlaceLabel(testStatus, y, usable) + S(20);
        Height = y;
    }
    void InvalidateTest()
    {
        testGeneration++; testOperation?.Cancel(); TestButton.Text = "测试连接";
        testStatus.Text = "尚未测试当前配置。"; testStatus.ForeColor = UiShape.Muted;
    }
    async Task TestAsync()
    {
        if (testOperation != null && !testOperation.IsCancellationRequested) { testOperation.Cancel(); return; }
        var operation = new CancellationTokenSource(); testOperation = operation;
        int id = ++testGeneration;
        TestButton.Text = "取消测试"; testStatus.Text = "正在发送固定测试句…"; Reflow();
        try
        {
            var preferences = ReadPreferences(); preferences.Validate();
            string text = await TranslationService.TranslateAsync("Hello world.", "en", "zh-CN", preferences, operation.Token);
            if (IsDisposed || id != testGeneration) return;
            testStatus.Text = "连接成功 · " + preferences.DisplayName + " 已返回译文。"; testStatus.ForeColor = Ui.Blue;
        }
        catch (OperationCanceledException) { if (!IsDisposed && id == testGeneration) testStatus.Text = "测试已取消。"; }
        catch (Exception ex) { if (!IsDisposed && id == testGeneration) { testStatus.Text = ex.Message; testStatus.ForeColor = Color.FromArgb(173, 57, 57); } }
        finally
        {
            if (ReferenceEquals(testOperation, operation)) testOperation = null;
            operation.Dispose();
            if (!IsDisposed && id == testGeneration) { TestButton.Text = "测试连接"; Reflow(); }
        }
    }
    void Reflow() { Configure(scale, Width); PreferredHeightChanged?.Invoke(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { testGeneration++; testOperation?.Cancel(); }
        base.Dispose(disposing); if (disposing) fonts.Dispose();
    }
}
