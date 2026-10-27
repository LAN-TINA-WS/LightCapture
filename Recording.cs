using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace LightCapture;

internal sealed class ClockedAudio : IAsyncDisposable
{
    readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
    readonly List<WasapiCapture> captures = [];
    readonly Dictionary<bool, VolumeSampleProvider> inputs = [];
    readonly WaveFileWriter writer;
    readonly CancellationTokenSource stop = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Task pump;
    bool syntheticSource;
    public bool HasSources => captures.Count > 0 || syntheticSource;
    internal void AddSyntheticTone(double frequency)
    {
        syntheticSource = true;
        mixer.AddMixerInput(new SignalGenerator(48000, 2) { Gain = .12, Frequency = frequency, Type = SignalGeneratorType.Sin });
    }
    public ClockedAudio(string path)
    {
        writer = new(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        pump = PumpAsync();
    }
    public void SetSource(bool system, bool enabled)
    {
        if (inputs.TryGetValue(system, out var volume))
        {
            volume.Volume = enabled ? 1 : 0;
            return;
        }
        if (!enabled)
            return;
        WasapiCapture capture = system ? new WasapiLoopbackCapture() : new WasapiCapture();
        try
        {
            var buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(3), DiscardOnBufferOverflow = true, ReadFully = true };
            ISampleProvider samples = buffer.ToSampleProvider();
            if (samples.WaveFormat.SampleRate != 48000)
                samples = new WdlResamplingSampleProvider(samples, 48000);
            samples = new StereoSamples(samples);
            var gain = new VolumeSampleProvider(samples) { Volume = 1 };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            capture.StartRecording();
            mixer.AddMixerInput(gain);
            captures.Add(capture);
            inputs[system] = gain;
        }
        catch { capture.Dispose(); throw; }
    }
    async Task PumpAsync()
    {
        var data = new float[19200];
        long written = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        void WriteDue()
        {
            long due = (long)(clock.Elapsed.TotalSeconds * 48000) - written;
            while (due > 0)
            {
                int frames = (int)Math.Min(due, data.Length / 2), count = frames * 2;
                mixer.Read(data, 0, count);
                for (int i = 0; i < count; i++)
                    data[i] = Math.Clamp(data[i], -1, 1);
                writer.WriteSamples(data, 0, count);
                written += frames;
                due -= frames;
            }
        }
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
                WriteDue();
        }
        catch (OperationCanceledException) { }
        finally { WriteDue(); }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var capture in captures)
        {
            try
            {
                capture.StopRecording();
            }
            catch { }
        }
        stop.Cancel();
        try
        {
            await pump;
        }
        finally { writer.Dispose(); foreach (var capture in captures) capture.Dispose(); stop.Dispose(); }
    }
    sealed class StereoSamples(ISampleProvider input) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        float[] scratch = [];
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = input.WaveFormat.Channels, frames = count / 2, needed = frames * channels;
            if (scratch.Length < needed)
                scratch = new float[needed];
            int read = input.Read(scratch, 0, needed), actual = read / channels;
            for (int i = 0; i < actual; i++)
            {
                buffer[offset + i * 2] = scratch[i * channels];
                buffer[offset + i * 2 + 1] = scratch[i * channels + (channels == 1 ? 0 : 1)];
            }
            if (actual < frames)
                Array.Clear(buffer, offset + actual * 2, (frames - actual) * 2);
            return count;
        }
    }
}

internal sealed class RecordingSession
{
    public string DirectoryPath
    {
        get;
    }
    public string Encoder
    {
        get;
    }
    readonly Process video;
    readonly Task<string> errors;
    readonly ClockedAudio audio;
    bool stopped;
    public Stopwatch Clock { get; } = Stopwatch.StartNew();
    public static string FindEncoder(AppSettings settings)
    {
        string[] candidates = [settings.FfmpegPath, Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"), Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")];
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)) ?? throw new InvalidOperationException("找不到录屏编码器。请在设置中选择 ffmpeg.exe，或将它放在程序的 tools 文件夹内。");
    }
    public static Process StartEncoder(string executable, IEnumerable<string> args, bool input = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardInput = input };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动录屏编码器。");
    }
    public RecordingSession(Rectangle bounds, AppSettings settings, bool sound, bool microphone, bool synthetic = false, bool syntheticAudio = false)
    {
        Encoder = FindEncoder(settings);
        DirectoryPath = Path.Combine(AppSettings.DataDirectory, "recordings", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(DirectoryPath);
        audio = new(Path.Combine(DirectoryPath, "audio.wav"));
        try
        {
            audio.SetSource(true, sound);
            audio.SetSource(false, microphone);
            if (synthetic && syntheticAudio)
            {
                audio.AddSyntheticTone(440);
                audio.AddSyntheticTone(660);
            }
            string[] input = synthetic ? ["-re", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=20"] : ["-f", "gdigrab", "-framerate", "20", "-draw_mouse", "1", "-offset_x", bounds.X.ToString(), "-offset_y", bounds.Y.ToString(), "-video_size", $"{bounds.Width}x{bounds.Height}", "-i", "desktop"];
            video = StartEncoder(Encoder, new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(input).Concat(["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "22", "-pix_fmt", "yuv420p", "-an", Path.Combine(DirectoryPath, "video.mp4")]), true);
            errors = video.StandardError.ReadToEndAsync();
        }
        catch { audio.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }
    public void SetSound(bool enabled) => audio.SetSource(true, enabled);
    public void SetMicrophone(bool enabled) => audio.SetSource(false, enabled);
    public async Task<string> StopAsync()
    {
        if (stopped)
            throw new InvalidOperationException("录屏已经结束。");
        stopped = true;
        Clock.Stop();
        try
        {
            if (!video.HasExited)
            {
                await video.StandardInput.WriteLineAsync("q");
                await video.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await video.WaitForExitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) { if (!video.HasExited) video.Kill(true); throw new InvalidOperationException("编码器未及时结束，录制缓存已保留，可在设置目录下的 recordings 文件夹中找回。"); }
        finally { await audio.DisposeAsync(); }
        string log = await errors;
        if (video.ExitCode != 0)
        {
            video.Dispose();
            throw new InvalidOperationException("录屏编码失败：" + log[..Math.Min(700, log.Length)]);
        }
        video.Dispose();
        string source = Path.Combine(DirectoryPath, "video.mp4"), result = Path.Combine(DirectoryPath, "轻截录屏.mp4");
        if (!audio.HasSources)
        {
            File.Copy(source, result, true);
            return result;
        }
        using var mux = StartEncoder(Encoder, ["-hide_banner", "-loglevel", "error", "-y", "-i", source, "-i", Path.Combine(DirectoryPath, "audio.wav"), "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "160k", "-af", "apad", "-shortest", "-movflags", "+faststart", result]);
        string muxError = await mux.StandardError.ReadToEndAsync();
        await mux.WaitForExitAsync();
        if (mux.ExitCode != 0)
            throw new InvalidOperationException("录制内容已保留，但音视频合成失败：" + muxError[..Math.Min(500, muxError.Length)]);
        return result;
    }
}

internal sealed class RecordingForm : Form
{
    readonly Rectangle region;
    readonly AppSettings settings;
    readonly Label status = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12), Font = new Font("Microsoft YaHei UI", 11) };
    readonly CheckBox sound = new() { Text = "系统声音", AutoSize = true }, microphone = new() { Text = "麦克风", AutoSize = true };
    readonly Button start = new(), stop = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    RecordingSession? session;
    int countdown;
    bool busy;
    public bool IsRecording => session != null || countdown > 0 || busy;
    public RecordingForm(Rectangle region, AppSettings settings)
    {
        this.region = region;
        this.settings = settings;
        Text = "轻截 · 录屏";
        ClientSize = new(390, 170);
        Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        status.Text = $"录制区域 {region.Width} × {region.Height}\n选择音源后，点击开始录制。";
        var sources = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(12, 2, 4, 0) };
        sources.Controls.AddRange([sound, microphone]);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8) };
        start = Ui.Button("开始录制", () => { try { RecordingSession.FindEncoder(settings); countdown = 3; start.Enabled = false; status.Text = "3 秒后开始录制…"; timer.Start(); } catch (Exception ex) { Ui.Error(this, ex); } }, 110);
        stop = Ui.Button("结束录制", () => _ = StopAsync(), 110);
        stop.Enabled = false;
        buttons.Controls.Add(start);
        buttons.Controls.Add(stop);
        buttons.Controls.Add(Ui.Button("关闭", () => { if (countdown > 0) { countdown = 0; timer.Stop(); start.Enabled = true; status.Text = "已取消倒计时。"; } else Close(); }, 90));
        Controls.Add(sources);
        Controls.Add(status);
        Controls.Add(buttons);
        var work = Screen.FromRectangle(region).WorkingArea;
        Location = new(Math.Clamp(region.Right + 8, work.Left, Math.Max(work.Left, work.Right - Width)), Math.Clamp(region.Bottom + 8, work.Top, Math.Max(work.Top, work.Bottom - Height)));
        Shown += (_, _) => Native.SetWindowDisplayAffinity(Handle, 0x11);
        timer.Tick += async (_, _) => { if (countdown > 0) { countdown--; if (countdown > 0) { status.Text = $"{countdown} 秒后开始录制…"; return; } await StartAsync(); } else if (session != null) status.Text = "● 正在录制  " + session.Clock.Elapsed.ToString(@"hh\:mm\:ss") + "\n系统声音和麦克风可独立切换。"; };
        sound.CheckedChanged += (_, _) => { if (session != null) try { session.SetSound(sound.Checked); } catch (Exception ex) { sound.Checked = false; Ui.Error(this, ex); } };
        microphone.CheckedChanged += (_, _) => { if (session != null) try { session.SetMicrophone(microphone.Checked); } catch (Exception ex) { microphone.Checked = false; Ui.Error(this, ex); } };
        FormClosing += (_, e) => { if (IsRecording) { e.Cancel = true; status.Text = "请先结束录制，内容将保留供预览和保存。"; } };
        FormClosed += (_, _) => timer.Dispose();
    }
    async Task StartAsync()
    {
        busy = true;
        try
        {
            bool s = sound.Checked, m = microphone.Checked;
            session = await Task.Run(() => new RecordingSession(region, settings, s, m));
            stop.Enabled = true;
            status.Text = "● 正在录制  00:00:00";
        }
        catch (Exception ex) { timer.Stop(); start.Enabled = true; Ui.Error(this, ex); }
        finally { busy = false; }
    }
    async Task StopAsync()
    {
        if (session == null || busy)
            return;
        busy = true;
        timer.Stop();
        stop.Enabled = false;
        sound.Enabled = microphone.Enabled = false;
        status.Text = "正在结束并整理录制内容…";
        try
        {
            string encoder = session.Encoder;
            string path = await session.StopAsync();
            session = null;
            busy = false;
            new VideoResultForm(path, encoder).Show();
            Close();
        }
        catch (Exception ex) { session = null; busy = false; start.Enabled = true; status.Text = "录制缓存已保留。"; Ui.Error(this, ex); }
    }
}

internal sealed class VideoResultForm : Form
{
    public VideoResultForm(string path, string encoder)
    {
        Text = "轻截 · 录屏完成";
        ClientSize = new(480, 210);
        Font = new Font("Microsoft YaHei UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        var label = new Label { Dock = DockStyle.Top, Height = 92, Padding = new Padding(20), Text = "录屏已完成，可以播放预览或另存为。\n未另存的录制内容也会保留在本机缓存中。" };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(12) };
        bar.Controls.Add(Ui.Button("播放预览", () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }), 100));
        bar.Controls.Add(Ui.Button("保存 MP4", () => { using var d = new SaveFileDialog { Filter = "MP4 视频|*.mp4", FileName = "轻截录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") }; if (d.ShowDialog(this) == DialogResult.OK) try { File.Copy(path, d.FileName, true); label.Text = "视频已保存。"; } catch (Exception ex) { Ui.Error(this, ex); } }, 100));
        var gif = Ui.Button("导出 GIF", () => { }, 100);
        gif.Click += async (_, _) => { using var d = new SaveFileDialog { Filter = "GIF 动图|*.gif", FileName = "轻截动图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") }; if (d.ShowDialog(this) != DialogResult.OK) return; gif.Enabled = false; label.Text = "正在生成动图…"; try { using var p = RecordingSession.StartEncoder(encoder, ["-hide_banner", "-loglevel", "error", "-y", "-i", path, "-filter_complex", "fps=12,scale=w='min(960,iw)':h=-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-loop", "0", d.FileName]); string error = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); if (p.ExitCode != 0) throw new InvalidOperationException(error); label.Text = "GIF 已导出（不含声音）。"; } catch (Exception ex) { Ui.Error(this, ex); } finally { if (!IsDisposed) gif.Enabled = true; } };
        bar.Controls.Add(gif);
        bar.Controls.Add(Ui.Button("查看缓存", () => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetDirectoryName(path)! }, UseShellExecute = true }), 100));
        Controls.Add(bar);
        Controls.Add(label);
    }
}
