using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LightCapture;

internal sealed record StitchMatch(bool Changed, bool Reliable, int Delta, int Footer, double Error);
internal sealed class Stitcher : IDisposable
{
    public Bitmap Image
    {
        get; private set;
    }
    Bitmap previous;
    public Stitcher(Bitmap first)
    {
        Image = first;
        previous = new Bitmap(first);
    }
    static byte[] Samples(Bitmap bitmap, int columns = 48)
    {
        using var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(rgb))
            g.DrawImageUnscaled(bitmap, 0, 0);
        var data = rgb.LockBits(new Rectangle(Point.Empty, rgb.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * rgb.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var result = new byte[columns * rgb.Height];
            for (int y = 0; y < rgb.Height; y++)
            for (int x = 0; x < columns; x++)
            {
                int sx = (x + 1) * rgb.Width / (columns + 1), i = y * data.Stride + sx * 4;
                result[y * columns + x] = (byte)((bytes[i] * 29 + bytes[i + 1] * 150 + bytes[i + 2] * 77) >> 8);
            }
            return result;
        }
        finally { rgb.UnlockBits(data); }
    }
    public static StitchMatch Match(Bitmap oldFrame, Bitmap next)
    {
        if (oldFrame.Size != next.Size)
            return new(true, false, 0, 0, double.MaxValue);
        const int columns = 48;
        int h = next.Height;
        if (h < 64)
            return new(true, false, 0, 0, double.MaxValue);
        var a = Samples(oldFrame);
        var b = Samples(next);
        double RowError(int yA, int yB)
        {
            double s = 0;
            for (int x = 1; x < columns - 1; x++)
                s += Math.Abs(a[yA * columns + x] - b[yB * columns + x]);
            return s / (columns - 2);
        }
        double baseline = 0;
        int samples = 0;
        for (int y = 0; y < h; y += 5)
        {
            baseline += RowError(y, y);
            samples++;
        }
        baseline /= Math.Max(1, samples);
        if (baseline < .85)
            return new(false, true, 0, 0, baseline);
        int header = 0, footer = 0;
        while (header < h / 5 && RowError(header, header) < 1.2)
            header++;
        while (footer < h / 6 && RowError(h - footer - 1, h - footer - 1) < 1.2)
            footer++;
        var scores = new List<(int Delta, double Score)>();
        for (int delta = 5; delta < h - header - footer - Math.Max(36, h / 5); delta++)
        {
            double sum = 0;
            int count = 0;
            for (int y = header + 3; y < h - footer - delta - 3; y += 4)
            {
                sum += RowError(y + delta, y);
                count++;
            }
            if (count > 5)
                scores.Add((delta, sum / count));
        }
        if (scores.Count == 0)
            return new(true, false, 0, footer, baseline);
        var best = scores.MinBy(s => s.Score);
        var nextBest = scores.Where(s => Math.Abs(s.Delta - best.Delta) > 7).Select(s => s.Score).DefaultIfEmpty(255).Min();
        bool reliable = best.Score < 11 && best.Score < baseline * .72 && (nextBest > best.Score * 1.18 + .55);
        return new(true, reliable, best.Delta, footer, best.Score);
    }
    public StitchMatch Append(Bitmap frame)
    {
        var match = Match(previous, frame);
        if (!match.Changed || !match.Reliable)
            return match;
        int newHeight = Image.Height + match.Delta;
        if (newHeight > 20000 || (long)newHeight * Image.Width > 40_000_000)
            throw new InvalidOperationException("长图已达到 20000 像素高度或 4000 万像素面积上限；已拼接的内容仍可保存。");
        var merged = new Bitmap(Image.Width, newHeight, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(merged))
        {
            g.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height - match.Footer), new Rectangle(0, 0, Image.Width, Image.Height - match.Footer), GraphicsUnit.Pixel);
            int take = match.Delta + match.Footer;
            g.DrawImage(frame, new Rectangle(0, Image.Height - match.Footer, Image.Width, take), new Rectangle(0, frame.Height - take, frame.Width, take), GraphicsUnit.Pixel);
        }
        Image.Dispose();
        Image = merged;
        previous.Dispose();
        previous = new Bitmap(frame);
        return match;
    }
    public void Dispose()
    {
        Image.Dispose();
        previous.Dispose();
    }
}

internal sealed class LongCaptureForm : Form
{
    readonly Stitcher stitcher;
    readonly Rectangle region;
    readonly AppSettings settings;
    readonly Action<Bitmap> edit;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 800 };
    readonly Label status = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8) };
    readonly PictureBox thumbnail = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(233, 237, 244) };
    readonly CheckBox automatic = new() { Text = "鼠标在选区内时自动追加", Checked = true, Dock = DockStyle.Top, Height = 28 };
    bool busy, closing;
    int frames = 1;
    protected override bool ShowWithoutActivation => true;
    public LongCaptureForm(Bitmap first, Rectangle region, AppSettings settings, Action<Bitmap> edit)
    {
        stitcher = new(first);
        this.region = region;
        this.settings = settings;
        this.edit = edit;
        Text = "轻截 · 长截图";
        ClientSize = new(360, 360);
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Microsoft YaHei UI", 9);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(4) };
        bar.Controls.Add(Ui.Button("追加一帧", () => _ = AppendAsync(), 82));
        bar.Controls.Add(Ui.Button("完成编辑", Finish, 82));
        bar.Controls.Add(Ui.Button("保存", Save, 70));
        bar.Controls.Add(Ui.Button("复制", () => _ = CopyAsync(), 70));
        bar.Controls.Add(Ui.Button("关闭", Close, 70));
        Controls.Add(thumbnail);
        Controls.Add(automatic);
        Controls.Add(status);
        Controls.Add(bar);
        status.Text = "请慢速滚动原窗口，稍停后自动拼接。\n重复、动态或不确定画面会暂停追加。";
        var work = Screen.FromRectangle(region).WorkingArea;
        int x = region.Right + 12;
        if (x + Width > work.Right)
            x = region.Left - Width - 12;
        if (x < work.Left)
            x = work.Right - Width - 12;
        Location = new(x, Math.Clamp(region.Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
        Shown += (_, _) => { Native.SetWindowDisplayAffinity(Handle, 0x11); UpdatePreview(); timer.Start(); };
        timer.Tick += (_, _) => { if (automatic.Checked && region.Contains(Cursor.Position) && Control.MouseButtons == MouseButtons.None) _ = AppendAsync(); };
        FormClosed += (_, _) => { closing = true; timer.Stop(); timer.Dispose(); thumbnail.Image?.Dispose(); stitcher.Dispose(); };
    }
    void UpdatePreview()
    {
        var old = thumbnail.Image;
        thumbnail.Image = stitcher.Image.GetThumbnailImage(Math.Max(1, (int)(stitcher.Image.Width * Math.Min(300f / stitcher.Image.Width, 200f / stitcher.Image.Height))), Math.Max(1, (int)(stitcher.Image.Height * Math.Min(300f / stitcher.Image.Width, 200f / stitcher.Image.Height))), null, IntPtr.Zero);
        old?.Dispose();
    }
    async Task AppendAsync()
    {
        if (busy || closing)
            return;
        busy = true;
        try
        {
            bool overlap = Bounds.IntersectsWith(region);
            if (overlap)
            {
                Hide();
                await Task.Delay(80);
                if (closing)
                    return;
            }
            using var frame = ImageOutput.Snapshot(region);
            if (overlap && !closing)
                Show();
            var match = await Task.Run(() => stitcher.Append(frame));
            if (closing)
                return;
            if (match.Changed && match.Reliable)
            {
                frames++;
                UpdatePreview();
                status.Text = $"已拼接 {frames} 帧 · {stitcher.Image.Width} × {stitcher.Image.Height}\n继续慢速向下滚动，或点击完成编辑。";
            }
            else if (match.Changed)
                status.Text = "本帧无法可靠匹配，未追加。\n请向上少滚一点，保证与上一次画面有重叠。";
        }
        catch (Exception ex) { if (!closing) { automatic.Checked = false; Ui.Error(this, ex); } }
        finally { if (!closing && !Visible) Show(); busy = false; }
    }
    void Finish()
    {
        if (busy)
            return;
        var copy = new Bitmap(stitcher.Image);
        Close();
        edit(copy);
    }
    void Save()
    {
        if (busy)
            return;
        try
        {
            ImageOutput.Save(this, stitcher.Image, settings);
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    async Task CopyAsync()
    {
        if (busy)
            return;
        try
        {
            await ImageOutput.CopyAsync(stitcher.Image);
            status.Text = "长图已复制。";
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
}
