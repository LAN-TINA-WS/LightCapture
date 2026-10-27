using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace LightCapture;

internal sealed class TextResultForm : Form
{
    readonly Bitmap image;
    readonly AppSettings settings;
    readonly bool translateOnShow;
    readonly TextResultPane pane;
    TextBox original => pane.Original;
    TextBox translated => pane.Translated;
    PictureBox preview => pane.Preview;
    Label status => pane.Status;
    ComboBox source => pane.Source.Input;
    ComboBox target => pane.Target.Input;
    CancellationTokenSource operation = new();
    IReadOnlyList<RecognizedLine> lines = [];
    int selectedLine = -1, generation;
    bool translating;
    static readonly (string Tag, string Name)[] Languages = [("en", "英语"), ("zh-CN", "简体中文"), ("ja", "日语"), ("ko", "韩语"), ("fr", "法语"), ("de", "德语"), ("es", "西班牙语")];
    public TextResultForm(Bitmap image, AppSettings settings, bool translateOnShow)
    {
        this.image = image;
        this.settings = settings;
        this.translateOnShow = translateOnShow;
        Text = "轻截 · 识图与翻译";
        AutoScaleMode = AutoScaleMode.None; BackColor = Ui.Surface;
        float scale = Native.DpiScaleAt(Cursor.Position);
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new(Math.Min((int)(1260 * scale), work.Width - 48), Math.Min((int)(860 * scale), work.Height - 64));
        MinimumSize = SizeFromClientSize(new(Math.Min((int)(720 * scale), ClientSize.Width), Math.Min((int)(500 * scale), ClientSize.Height)));
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Ui.CreateIcon();
        pane = new(image, Languages.Select(l => l.Name)) { Dock = DockStyle.Fill };
        pane.RecognizeRequested += () => _ = RecognizeAsync(); pane.TranslateRequested += () => _ = TranslateAsync();
        pane.CopyOriginalRequested += () => Copy(original.Text); pane.CopyTranslatedRequested += () => Copy(translated.Text);
        pane.ExportTextRequested += () => Export(false); pane.ExportWordRequested += () => Export(true);
        pane.CancelRequested += () => operation.Cancel(); Controls.Add(pane); pane.Configure(scale);
        RefreshTranslationSettings(); Activated += (_, _) => RefreshTranslationSettings();
        DpiChanged += (_, e) => pane.Configure(e.DeviceDpiNew / 96f);
        original.MouseUp += (_, _) => { selectedLine = original.Text.Take(original.SelectionStart).Count(c => c == '\n'); preview.Invalidate(); };
        preview.Paint += PaintPreview;
        Shown += async (_, _) => { pane.Configure(DeviceDpi / 96f); if (await RecognizeAsync() && translateOnShow) await TranslateAsync(); };
        FormClosed += (_, _) => { generation++; operation.Cancel(); operation.Dispose(); preview.Image = null; image.Dispose(); };
    }
    CancellationToken BeginOperation()
    {
        operation.Cancel();
        operation.Dispose();
        operation = new();
        generation++;
        return operation.Token;
    }
    async Task<bool> RecognizeAsync()
    {
        var token = BeginOperation();
        int id = generation;
        pane.SetBusy(true);
        status.Text = "正在本机识别，图片不上传…";
        try
        {
            var result = await TextServices.RecognizeAsync(image, settings.OcrLanguage, token);
            if (IsDisposed || id != generation)
                return false;
            lines = result.Lines;
            selectedLine = -1;
            original.Text = result.Text;
            translated.Clear();
            source.SelectedIndex = System.Text.RegularExpressions.Regex.IsMatch(result.Text, "[\u4e00-\u9fff]") ? 1 : 0;
            target.SelectedIndex = source.SelectedIndex == 1 ? 0 : 1;
            status.Text = string.IsNullOrWhiteSpace(result.Text) ? "没有识别到文字，可以更换语言或选取更清晰的区域。" : $"本机识别完成 · {result.Language} · {result.Lines.Count} 行";
            return !string.IsNullOrWhiteSpace(result.Text);
        }
        catch (OperationCanceledException) { if (!IsDisposed && id == generation) status.Text = "已取消识别。"; return false; }
        catch (Exception ex) { if (!IsDisposed && id == generation) { status.Text = "识别未完成，图片仍保留。"; Ui.Error(this, ex); } return false; }
        finally { if (!IsDisposed && id == generation) pane.SetBusy(false); }
    }
    async Task TranslateAsync()
    {
        string text = original.Text;
        var preferences = settings.Translation.Clone();
        var token = BeginOperation();
        int id = generation;
        translating = true; pane.SetTranslationProvider(preferences.DisplayName);
        pane.SetBusy(true);
        status.Text = $"正在通过 {preferences.DisplayName} 翻译文字…";
        try
        {
            string result = await TranslationService.TranslateAsync(text, Languages[source.SelectedIndex].Tag, Languages[target.SelectedIndex].Tag, preferences, token);
            if (IsDisposed || id != generation)
                return;
            translated.Text = result;
            status.Text = $"翻译完成 · {preferences.DisplayName} · 原文和译文均可编辑、复制或导出";
        }
        catch (OperationCanceledException) { if (!IsDisposed && id == generation) status.Text = "已取消翻译。"; }
        catch (Exception ex) { if (!IsDisposed && id == generation) { status.Text = "翻译未完成，原文已保留。"; Ui.Error(this, ex); } }
        finally { if (!IsDisposed && id == generation) { translating = false; pane.SetBusy(false); RefreshTranslationSettings(); } }
    }
    internal void RefreshTranslationSettings()
    {
        if (!translating) pane.SetTranslationProvider(settings.Translation.DisplayName);
    }
    void PaintPreview(object? sender, PaintEventArgs e)
    {
        if (selectedLine < 0 || selectedLine >= lines.Count)
            return;
        float scale = Math.Min(preview.ClientSize.Width / (float)image.Width, preview.ClientSize.Height / (float)image.Height), x = (preview.ClientSize.Width - image.Width * scale) / 2, y = (preview.ClientSize.Height - image.Height * scale) / 2;
        var r = lines[selectedLine].Bounds;
        using var brush = new SolidBrush(Color.FromArgb(60, 43, 105, 222));
        e.Graphics.FillRectangle(brush, x + r.X * scale, y + r.Y * scale, r.Width * scale, r.Height * scale);
    }
    void Copy(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        try
        {
            Clipboard.SetText(text);
            status.Text = "文字已复制。";
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    void Export(bool docx)
    {
        using var dialog = new SaveFileDialog { Filter = docx ? "Word 文档|*.docx" : "文字文件|*.txt", FileName = "轻截识图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string text = original.Text + (string.IsNullOrWhiteSpace(translated.Text) ? "" : Environment.NewLine + Environment.NewLine + "译文" + Environment.NewLine + translated.Text);
        try
        {
            if (docx)
                WriteDocx(dialog.FileName, text);
            else
                File.WriteAllText(dialog.FileName, text, new UTF8Encoding(true));
            status.Text = "文字已导出。";
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    internal static void WriteDocx(string path, string text)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Write(string name, string value)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(value);
        }
        Write("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Write("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(w + "body", text.Replace("\r", "").Split('\n').Select(line => new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line)))));
        Write("word/document.xml", new XDocument(new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w), body)).ToString());
    }
}
