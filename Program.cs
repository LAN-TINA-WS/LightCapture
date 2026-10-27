using System.Diagnostics;

namespace LightCapture;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--appearance-proof"))
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            return AppearanceProof.Run(args);
        }
        if (args.Contains("--toolbar-proof"))
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            return ToolbarProof.Run(args);
        }
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log(ex); };
        if (args.Contains("--self-test"))
            return SelfTests.Run(args);
        if (args.Contains("--test-scene"))
        {
            Application.Run(new TestScene());
            return 0;
        }
        using var mutex = new Mutex(true, "Local.LightCapture.Desktop.v1", out bool first);
        if (!first)
        {
            var hwnd = Native.FindWindow(null, "LightCapture.MessageWindow");
            if (hwnd != IntPtr.Zero)
                Native.PostMessage(hwnd, Native.WM_CAPTURE, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        try
        {
            using var app = new TrayContext();
            if (args.Contains("--capture"))
                app.QueueCapture(CaptureIntent.Image);
            Application.Run(app);
            return 0;
        }
        catch (Exception ex) { Report(ex); return 1; }
    }
    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.AppendAllText(Path.Combine(AppSettings.DataDirectory, "error.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
        }
        catch { }
    }
    static void Report(Exception ex)
    {
        Log(ex);
        MessageBox.Show(ex.Message, "轻截", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

internal sealed class MessageWindow : NativeWindow, IDisposable
{
    readonly Action action;
    public MessageWindow(Action action)
    {
        this.action = action;
        CreateHandle(new CreateParams { Caption = "LightCapture.MessageWindow" });
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg is Native.WM_HOTKEY or Native.WM_CAPTURE)
        {
            action();
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        Native.UnregisterHotKey(Handle, 1);
        DestroyHandle();
    }
}

internal sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly ModernTrayMenu trayMenu;
    readonly Icon icon = Ui.CreateIcon();
    readonly MessageWindow window;
    readonly AppSettings settings = AppSettings.Load();
    readonly HashSet<Form> auxiliary = [];
    CaptureForm? capture;
    bool pending, exiting, editingShortcut;
    public TrayContext()
    {
        window = new MessageWindow(() => QueueCapture(CaptureIntent.Image));
        trayMenu = new(settings, command =>
        {
            switch (command)
            {
                case TrayCommand.Capture: QueueCapture(CaptureIntent.Image); break;
                case TrayCommand.LongCapture: QueueCapture(CaptureIntent.Long); break;
                case TrayCommand.Ocr: QueueCapture(CaptureIntent.Ocr); break;
                case TrayCommand.Translate: QueueCapture(CaptureIntent.Translate); break;
                case TrayCommand.Record: QueueCapture(CaptureIntent.Record); break;
                case TrayCommand.OpenImage: OpenImage(); break;
                case TrayCommand.Settings: ShowSettings(); break;
                case TrayCommand.Help: ShowHelp(); break;
                case TrayCommand.Exit: Exit(); break;
            }
        });
        tray = new NotifyIcon { Icon = icon, Visible = true, Text = "轻截 · " + settings.HotkeyText, ContextMenuStrip = trayMenu };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) QueueCapture(CaptureIntent.Image); };
        if (!Register(settings.HotkeyModifiers, settings.Hotkey))
        {
            tray.ShowBalloonTip(5000, "截图快捷键被占用", "点击托盘图标仍可截图；右键 → 快捷键与设置，可以更换组合键。", ToolTipIcon.Info);
        }
    }
    public bool Register(uint mods, Keys key)
    {
        Native.UnregisterHotKey(window.Handle, 1);
        if (Native.RegisterHotKey(window.Handle, 1, mods | 0x4000, (uint)key))
            return true;
        Native.RegisterHotKey(window.Handle, 1, settings.HotkeyModifiers | 0x4000, (uint)settings.Hotkey);
        return false;
    }
    public void QueueCapture(CaptureIntent intent)
    {
        if (exiting || editingShortcut || capture is { IsDisposed: false } || pending)
            return;
        pending = true;
        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); pending = false; BeginCapture(intent); };
        timer.Start();
    }
    void BeginCapture(CaptureIntent intent)
    {
        if (exiting || editingShortcut || capture is { IsDisposed: false })
            return;
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            var image = ImageOutput.Snapshot(bounds);
            capture = new CaptureForm(image, bounds, settings, intent);
            capture.Produced += HandleOutput;
            capture.FormClosed += (_, _) => capture = null;
            capture.Show();
        }
        catch (Exception ex) { Program.Log(ex); tray.ShowBalloonTip(5000, "无法开始截图", ex.Message, ToolTipIcon.Warning); }
    }
    void HandleOutput(CaptureAction action, Bitmap image, Rectangle absolute)
    {
        try
        {
            if (action == CaptureAction.Pin && auxiliary.OfType<PinForm>().Count() >= 16)
                throw new InvalidOperationException("最多同时保留16张贴图，请先关闭一些贴图。");
            Form form = action switch
            {
                CaptureAction.Pin => new PinForm(image, settings, EditImage),
                CaptureAction.Ocr => new TextResultForm(image, settings, false),
                CaptureAction.Translate => new TextResultForm(image, settings, true),
                CaptureAction.Long => new LongCaptureForm(image, absolute, settings, EditImage),
                CaptureAction.Record => NewRecorder(image, absolute),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            Track(form);
        }
        catch (Exception ex) { image.Dispose(); Program.Log(ex); Ui.Error(null, ex); }
    }
    Form NewRecorder(Bitmap preview, Rectangle rect)
    {
        preview.Dispose();
        return new RecordingForm(rect, settings);
    }
    void Track(Form form)
    {
        auxiliary.Add(form);
        form.FormClosed += (_, _) => auxiliary.Remove(form);
        form.Show();
    }
    void EditImage(Bitmap bitmap) => Track(new ImageEditorForm(bitmap, settings, copy => HandleOutput(CaptureAction.Pin, copy, Rectangle.Empty)));
    void OpenImage()
    {
        using var dlg = new OpenFileDialog { Title = "打开图片编辑", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK)
            return;
        try
        {
            using var source = Image.FromFile(dlg.FileName);
            if ((long)source.Width * source.Height > 60_000_000)
                throw new InvalidOperationException("图片超过 6000 万像素，请先缩小图片。");
            EditImage(new Bitmap(source));
        }
        catch (Exception ex) { Ui.Error(null, ex); }
    }
    void ShowSettings()
    {
        var existing = auxiliary.OfType<SettingsForm>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }
        var form = new SettingsForm(settings, Register, () =>
        {
            tray.Text = "轻截 · " + settings.HotkeyText;
            foreach (var editor in auxiliary.OfType<ImageEditorForm>()) editor.RefreshAppearance();
            foreach (var result in auxiliary.OfType<TextResultForm>()) result.RefreshTranslationSettings();
        });
        form.KeyCaptureChanged += SetShortcutRecording;
        Track(form);
    }
    void SetShortcutRecording(bool recording)
    {
        if (exiting || editingShortcut == recording) return;
        editingShortcut = recording;
        Native.UnregisterHotKey(window.Handle, 1);
        if (!recording && !Native.RegisterHotKey(window.Handle, 1, settings.HotkeyModifiers | 0x4000, (uint)settings.Hotkey))
            tray.ShowBalloonTip(5000, "截图快捷键被占用", "请在偏好设置中更换快捷键；仍可点击托盘图标截图。", ToolTipIcon.Info);
    }
    void ShowHelp() => Track(new HelpForm(settings));
    void Exit()
    {
        var recording = auxiliary.OfType<RecordingForm>().FirstOrDefault(r => r.IsRecording);
        if (recording != null)
        {
            recording.Activate();
            MessageBox.Show(recording, "请先结束或取消当前录屏，再退出轻截。", "正在录屏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        exiting = true;
        capture?.Close();
        foreach (var f in auxiliary.ToArray())
            f.Close();
        tray.Visible = false;
        ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
            window.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
