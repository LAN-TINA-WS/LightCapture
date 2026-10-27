using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LightCapture;

internal static class SelfTests
{
    public static int Run(string[] args)
    {
        var results = new List<object>();
        int failed = 0;
        int artifactsIndex=Array.IndexOf(args,"--artifacts");
        string dir = artifactsIndex>=0&&artifactsIndex+1<args.Length?args[artifactsIndex+1]:Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(dir);
        void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
        void Test(string name, Action run)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                run();
                results.Add(new
                {
                    name,
                    status = "passed",
                    ms = timer.ElapsedMilliseconds
                });
            }
            catch (Exception ex) { failed++; results.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        Test("逆向拖动与负坐标矩形", () => Check(Geometry.Between(new(-20, 60), new(30, -10)) == new Rectangle(-20, -10, 50, 70), "矩形归一化失败"));
        TranslationTests.Register(Test);
        Test("选区移动限制在桌面内", () => { Check(Geometry.MoveInside(new(20, 20, 30, 30), new(-100, 100), new(0, 0, 100, 100)) == new Rectangle(0, 70, 30, 30), "移动边界错误"); });
        Test("像素裁剪不受显示缩放影响", () => { using var b = Pattern(420, 300); using var doc = new CaptureDocument(new Bitmap(b)); using var crop = doc.Render(new(31, 27, 120, 70)); Check(crop.Size == new Size(120, 70), "裁剪尺寸不正确"); Check(crop.GetPixel(22, 18).ToArgb() == b.GetPixel(53, 45).ToArgb(), "裁剪像素偏移"); });
        Test("标注撤销重做保持对象坐标", () => { using var doc = new CaptureDocument(Pattern(200, 120)); doc.Checkpoint(); doc.Annotations.Add(new() { Tool = DrawingTool.Arrow, Points = [new(10, 20), new(100, 80)], ColorArgb = Color.Red.ToArgb() }); doc.Checkpoint(); doc.Annotations[0].Move(7, -3); doc.Undo(); Check(doc.Annotations[0].Points[0] == new PointF(10, 20), "撤销未恢复坐标"); doc.Redo(); Check(doc.Annotations[0].Points[0] == new PointF(17, 17), "重做未恢复移动"); doc.Undo(); doc.Undo(); Check(doc.Annotations.Count == 0, "撤销新增失败"); });
        Test("非矩形裁剪保留PNG透明度", () => { using var doc = new CaptureDocument(Pattern(100, 100)); using var b = doc.Render(new(0, 0, 100, 100), [new(0, 0), new(99, 0), new(0, 99)]); Check(b.GetPixel(95, 95).A == 0, "非选区应透明"); Check(b.GetPixel(10, 10).A == 255, "选区内容丢失"); });
        Test("矩形标注进入最终图像", () => { using var doc = new CaptureDocument(new Bitmap(160, 100)); doc.Annotations.Add(new() { Tool = DrawingTool.Rectangle, Points = [new(20, 20), new(120, 80)], Width = 4, ColorArgb = Color.Red.ToArgb() }); using var b = doc.Render(new(0, 0, 160, 100)); Check(b.GetPixel(40, 20).R > 200, "输出未包含标注"); });
        Test("马赛克局部生效且不改底图", () => { using var baseImage = Pattern(180, 160); using var doc = new CaptureDocument(new Bitmap(baseImage)); doc.Annotations.Add(new() { Tool = DrawingTool.Mosaic, Points = [new(80, 80), new(90, 80)], Width = 5 }); using var result = doc.Render(new(0, 0, 180, 160)); Check(result.GetPixel(10, 10).ToArgb() == baseImage.GetPixel(10, 10).ToArgb(), "选区外被修改"); Check(result.GetPixel(81, 81).ToArgb() != baseImage.GetPixel(81, 81).ToArgb(), "马赛克未生效"); Check(doc.Background.GetPixel(81, 81).ToArgb() == baseImage.GetPixel(81, 81).ToArgb(), "破坏了原始底图"); });
        Test("马赛克扩大笔刷不改变颗粒", () =>
        {
            using var background = Pattern(256, 256); using var doc = new CaptureDocument(new Bitmap(background));
            var mark = new Annotation { Tool = DrawingTool.Mosaic, Points = [new(128, 128)], MosaicBrushSize = 20, MosaicBlockSize = 10 };
            doc.Annotations.Add(mark); using var small = doc.Render(new(Point.Empty, background.Size));
            mark.MosaicBrushSize = 100; using var large = doc.Render(new(Point.Empty, background.Size));
            for (int y = 125; y < 132; y++) for (int x = 125; x < 132; x++) Check(small.GetPixel(x, y).ToArgb() == large.GetPixel(x, y).ToArgb(), "扩大笔刷改变了重叠区的码块");
            Check(small.GetPixel(161, 128).ToArgb() == background.GetPixel(161, 128).ToArgb(), "小笔刷覆盖过宽");
            Check(large.GetPixel(161, 128).ToArgb() == background.GetPixel(165, 125).ToArgb(), "大笔刷未扩大覆盖范围或颗粒网格发生偏移");
        });
        Test("马赛克颗粒大小独立于涂抹范围", () =>
        {
            using var background = Pattern(256, 256); using var doc = new CaptureDocument(new Bitmap(background));
            var mark = new Annotation { Tool = DrawingTool.Mosaic, Points = [new(128, 128)], MosaicBrushSize = 100, MosaicBlockSize = 8 };
            doc.Annotations.Add(mark); using var fine = doc.Render(new(Point.Empty, background.Size));
            mark.MosaicBlockSize = 20; using var coarse = doc.Render(new(Point.Empty, background.Size));
            Check(fine.GetPixel(113, 114).ToArgb() == background.GetPixel(116, 116).ToArgb(), "细颗粒边长或采样错误");
            Check(coarse.GetPixel(113, 114).ToArgb() == background.GetPixel(110, 110).ToArgb(), "粗颗粒边长或采样错误");
            Check(fine.GetPixel(190, 128).ToArgb() == background.GetPixel(190, 128).ToArgb() && coarse.GetPixel(190, 128).ToArgb() == background.GetPixel(190, 128).ToArgb(), "改变颗粒扩大了覆盖范围");
        });
        Test("马赛克两项参数随撤销重做恢复", () =>
        {
            using var doc = new CaptureDocument(Pattern(160, 160));
            doc.Annotations.Add(new() { Tool = DrawingTool.Mosaic, Points = [new(80, 80)], MosaicBrushSize = 20, MosaicBlockSize = 10 });
            doc.Checkpoint(); doc.Annotations[0].MosaicBrushSize = 80; doc.Annotations[0].MosaicBlockSize = 18;
            doc.Undo(); Check(doc.Annotations[0].MosaicBrushSize == 20 && doc.Annotations[0].MosaicBlockSize == 10, "撤销丢失马赛克参数");
            doc.Redo(); Check(doc.Annotations[0].MosaicBrushSize == 80 && doc.Annotations[0].MosaicBlockSize == 18, "重做丢失马赛克参数");
        });
        Test("快速拖动画笔仍产生连续马赛克", () =>
        {
            using var background = Pattern(320, 160); using var doc = new CaptureDocument(new Bitmap(background));
            doc.Annotations.Add(new() { Tool = DrawingTool.Mosaic, Points = [new(40, 80), new(280, 80)], MosaicBrushSize = 12, MosaicBlockSize = 6 });
            using var result = doc.Render(new(Point.Empty, background.Size));
            foreach (int x in new[] { 40, 160, 280 }) Check(result.GetPixel(x, 80).ToArgb() == background.GetPixel(x / 6 * 6 + 3, 81).ToArgb(), "马赛克笔画出现断点或端点空洞");
            Check(result.GetPixel(160, 60).ToArgb() == background.GetPixel(160, 60).ToArgb(), "连续笔画超出笔刷宽度");
        });
        Test("马赛克独立参数保存与旧设置迁移", () =>
        {
            var migrated = AppSettings.Deserialize("{\"StrokeWidth\":10}");
            Check(migrated.MosaicBrushSize == 60 && migrated.MosaicBlockSize == 10 && migrated.StrokeWidth == 10, "旧设置未保留覆盖范围或仍绑定颗粒大小");
            var saved = AppSettings.Deserialize(JsonSerializer.Serialize(new AppSettings { StrokeWidth = 3, MosaicBrushSize = 90, MosaicBlockSize = 6 }));
            Check(saved.MosaicBrushSize == 90 && saved.MosaicBlockSize == 6 && saved.StrokeWidth == 3, "独立参数未正确保存");
        });
        Test("编号在撤销后连续", () => { using var doc = new CaptureDocument(new Bitmap(40, 40)); doc.Checkpoint(); doc.Annotations.Add(new() { Tool = DrawingTool.Marker, Number = 1, Points = [new(10, 10)] }); Check(doc.NextNumber == 2, "编号失败"); doc.Undo(); Check(doc.NextNumber == 1, "撤销后编号错误"); });
        Test("PNG JPEG BMP可重新打开", () => { using var b = Pattern(122, 84); foreach (string ext in new[] { "png", "jpg", "bmp" }) { string p = Path.Combine(dir, "output." + ext); ImageOutput.SaveFile(b, p); using var restored = Image.FromFile(p); Check(restored.Size == b.Size, "格式输出尺寸错误：" + ext); } });
        Test("透明像素输出JPEG时铺白底", () => { using var b = new Bitmap(100, 100, PixelFormat.Format32bppArgb); string path = Path.Combine(dir, "alpha.jpg"); ImageOutput.SaveFile(b, path); using var saved = new Bitmap(path); Check(saved.GetPixel(50, 50).R > 245, "透明区域未铺白底"); });
        Test("长图识别80像素滚动并正确合成", () => { using var world = Pattern(420, 1200); using var first = View(world, 0, 300); using var next = View(world, 80, 300); var match = Stitcher.Match(first, next); Check(match.Reliable && match.Delta == 80, $"滚动匹配失败 {match}"); using var stitch = new Stitcher(new Bitmap(first)); stitch.Append(next); Check(stitch.Image.Height == 380, "拼接高度错误"); Check(stitch.Image.GetPixel(50, 350).ToArgb() == world.GetPixel(50, 350).ToArgb(), "拼缝像素不一致"); });
        Test("长图忽略完全相同的画面", () => { using var frame = Pattern(160, 240); var match = Stitcher.Match(frame, frame); Check(!match.Changed, "静止画面不应该追加"); });
        Test("长图不接受无信息白色重复区", () => { using var a = new Bitmap(160, 240); using var b = new Bitmap(160, 240); using (var g = Graphics.FromImage(a)) g.Clear(Color.White); using (var g = Graphics.FromImage(b)) g.Clear(Color.White); Check(!Stitcher.Match(a, b).Changed, "白色画面不能伪造滚动"); });
        Test("长图正确处理固定页头和页尾", () => { using var world = Pattern(320, 1300); using var first = View(world, 0, 320); using var next = View(world, 75, 320); foreach (var frame in new[] { first, next }) using (var g = Graphics.FromImage(frame)) { g.FillRectangle(Brushes.White, 0, 0, 320, 24); g.FillRectangle(Brushes.Gray, 0, 300, 320, 20); } using var stitch = new Stitcher(new Bitmap(first)); var result = stitch.Append(next); Check(result.Reliable && result.Delta == 75, $"固定头尾匹配失败 {result}"); Check(stitch.Image.Height == 395, "固定头尾下高度错误"); Check(stitch.Image.GetPixel(50, 390).ToArgb() == Color.Gray.ToArgb(), "页尾应只保留一次"); });
        Test("撤销历史受控且新编辑清除重做", () => { using var doc = new CaptureDocument(new Bitmap(20, 20)); for (int i = 0; i < 130; i++) { doc.Checkpoint(); doc.Annotations.Clear(); doc.Annotations.Add(new() { Tool = DrawingTool.Marker, Number = i, Points = [new(5, 5)] }); } int count = 0; while (doc.Undo()) count++; Check(count == 100, "历史上限未生效"); doc.Checkpoint(); Check(!doc.Redo(), "新操作后重做应清空"); });
        Test("UTF8翻译分段不截断中文和emoji", () => { string text = string.Concat(Enumerable.Repeat("中文标点 😀 abc\r\n", 100)); var chunks = TextServices.TranslationChunks(text).ToArray(); Check(string.Concat(chunks) == text, "分段破坏原文"); Check(chunks.All(c => Encoding.UTF8.GetByteCount(c) <= 450), "超出API字节限制"); });
        Test("Word导出可解析且正确转义", () => { string path = Path.Combine(dir, "text.docx"); if (File.Exists(path)) File.Delete(path); TextResultForm.WriteDocx(path, "中文 <&>\nsecond line"); using var zip = ZipFile.OpenRead(path); using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()); string xml = reader.ReadToEnd(); Check(xml.Contains("&lt;&amp;&gt;"), "XML转义失败"); System.Xml.Linq.XDocument.Parse(xml); Check(zip.GetEntry("[Content_Types].xml") != null, "缺少文档内容类型"); });
        Test("Windows本机OCR识别合成英文", () =>
        {
            using var b = new Bitmap(900, 180);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                using var f = new Font("Segoe UI", 42, FontStyle.Regular, GraphicsUnit.Pixel);
                g.DrawString("LightCapture 2026", f, Brushes.Black, 30, 50);
            }
            var result = TextServices.RecognizeAsync(b, "en-US", CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Text.Contains("2026"), "OCR未识别测试文本：" + result.Text);
            File.WriteAllText(Path.Combine(dir, "ocr.txt"), result.Text);
        });
        Test("Windows本机OCR识别合成中文", () =>
        {
            using var b=new Bitmap(1000,180);using(var g=Graphics.FromImage(b)){g.Clear(Color.White);using var f=new Font("Microsoft YaHei UI",42,FontStyle.Regular,GraphicsUnit.Pixel);g.DrawString("中文截图测试 2026",f,Brushes.Black,30,50);}
            var result=TextServices.RecognizeAsync(b,"zh-CN",CancellationToken.None).GetAwaiter().GetResult();string text=string.Concat(result.Text.Where(c=>!char.IsWhiteSpace(c)));Check(text.Contains("中文")&&text.Contains("截图")&&text.Contains("2026"),"中文识别结果不符："+result.Text);File.WriteAllText(Path.Combine(dir,"ocr-chinese.txt"),result.Text);
        });
        Test("剪贴板Bitmap及PNG数据可读取",()=>
        {
            using var b=Pattern(120,80);ImageOutput.CopyAsync(b).GetAwaiter().GetResult();using var restored=Clipboard.GetImage();Check(restored!=null&&restored.Size==b.Size,"剪贴板图片尺寸错误");bool hasPng=Clipboard.TryGetData<MemoryStream>("PNG",out var png);Check(hasPng&&png!=null&&png.ToArray().Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"剪贴板PNG格式头错误");
        });
        Test("FFmpeg编码与解码合成视频", () => { string exe = RecordingSession.FindEncoder(new()), path = Path.Combine(dir, "encoder.mp4"); using (var p = RecordingSession.StartEncoder(exe, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=20", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", path])) { string err = p.StandardError.ReadToEnd(); p.WaitForExit(); Check(p.ExitCode == 0, err); } using var decode = RecordingSession.StartEncoder(exe, ["-hide_banner", "-loglevel", "error", "-i", path, "-f", "null", "-"]); string error = decode.StandardError.ReadToEnd(); decode.WaitForExit(); Check(decode.ExitCode == 0, error); });
        Test("录屏会话正常结束并保留有效视频", () => { var recording = new RecordingSession(new Rectangle(0, 0, 320, 180), new(), false, false, true); Thread.Sleep(1300); string file = recording.StopAsync().GetAwaiter().GetResult(); Check(File.Exists(file) && new FileInfo(file).Length > 1000, "会话没有生成有效视频"); using var decode = RecordingSession.StartEncoder(recording.Encoder, ["-hide_banner", "-loglevel", "error", "-i", file, "-f", "null", "-"]); string error = decode.StandardError.ReadToEnd(); decode.WaitForExit(); Check(decode.ExitCode == 0, error); File.WriteAllText(Path.Combine(dir, "recording-session-path.txt"), file); });
        Test("静音音轨保持采样时钟连续", () => { string file = Path.Combine(dir, "silence.wav"); var audio = new ClockedAudio(file); Thread.Sleep(260); audio.DisposeAsync().AsTask().GetAwaiter().GetResult(); using var reader = new NAudio.Wave.WaveFileReader(file); Check(reader.TotalTime.TotalMilliseconds >= 200 && reader.TotalTime.TotalMilliseconds < 2000, "静音时间轴不连续"); });
        Test("合成双音源能混音并封装为AAC",()=>{var recording=new RecordingSession(new Rectangle(0,0,320,180),new(),false,false,true,true);Thread.Sleep(1300);string file=recording.StopAsync().GetAwaiter().GetResult();using var decode=RecordingSession.StartEncoder(recording.Encoder,["-hide_banner","-loglevel","error","-i",file,"-map","0:a:0","-f","null","-"]);string error=decode.StandardError.ReadToEnd();decode.WaitForExit();Check(decode.ExitCode==0,error);using var reader=new NAudio.Wave.WaveFileReader(Path.Combine(recording.DirectoryPath,"audio.wav"));var bytes=new byte[4096];reader.ReadExactly(bytes);Check(bytes.Any(b=>b!=0),"混音输出不应为全静音");File.WriteAllText(Path.Combine(dir,"mixed-recording-path.txt"),file);});
        Test("合成视频可导出GIF", () => { string path = Path.Combine(dir, "preview.gif"); using var p = RecordingSession.StartEncoder(RecordingSession.FindEncoder(new()), ["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(dir, "encoder.mp4"), "-filter_complex", "fps=12,scale=w='min(960,iw)':h=-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-loop", "0", path]); string error = p.StandardError.ReadToEnd(); p.WaitForExit(); Check(p.ExitCode == 0, error); using var gif = Image.FromFile(path); Check(gif.Width == 320 && gif.Height == 180, "GIF尺寸错误"); });
        if (args.Contains("--network-test"))
            Test("MyMemory在线翻译合成文本", () => { string translated = TextServices.TranslateAsync("Hello world.", "en", "zh-CN", CancellationToken.None).GetAwaiter().GetResult(); Check(!string.IsNullOrWhiteSpace(translated) && translated != "Hello world.", "翻译未返回有效结果"); File.WriteAllText(Path.Combine(dir, "translation.txt"), translated); });
        string report = Path.Combine(dir, "results.json");
        int index = Array.IndexOf(args, "--report");
        if (index >= 0 && index + 1 < args.Length)
            report = args[index + 1];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            passed = results.Count - failed,
            failed,
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }
    internal static Bitmap Pattern(int width, int height)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        var random = new Random(9127);
        for (int y = 0; y < height; y++)
        {
            int seed = random.Next(32, 220);
            for (int x = 0; x < width; x++)
                image.SetPixel(x, y, Color.FromArgb((seed + x * 3) % 256, (seed * 2 + x * 7) % 256, (seed + y + x) % 256));
        }
        return image;
    }
    internal static Bitmap View(Bitmap world, int y, int height)
    {
        var b = new Bitmap(world.Width, height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(b);
        g.DrawImage(world, new Rectangle(0, 0, b.Width, b.Height), new Rectangle(0, y, b.Width, b.Height), GraphicsUnit.Pixel);
        return b;
    }
}

internal sealed class TestScene : Form
{
    public TestScene()
    {
        Text = "LightCapture TestScene";
        StartPosition = FormStartPosition.Manual;
        Location = new(100, 80);
        Size = new(1120, 780);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 11);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var page = new TabPage("截图与文字");
        var scroll = new TabPage("滚动测试");
        tabs.TabPages.AddRange([page, scroll]);
        Controls.Add(tabs);
        var canvas = new TestCanvas { Dock = DockStyle.Fill };
        page.Controls.Add(canvas);
        var launch = Ui.Button("开始截图测试", () => { var hwnd = Native.FindWindow(null, "LightCapture.MessageWindow"); if (hwnd != IntPtr.Zero) Native.PostMessage(hwnd, Native.WM_CAPTURE, IntPtr.Zero, IntPtr.Zero); }, 150);
        launch.Dock = DockStyle.Bottom;
        launch.Height = 40;
        page.Controls.Add(launch);
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = new LongTestCanvas { Size = new(1000, 6200) };
        panel.Controls.Add(content);
        scroll.Controls.Add(panel);
    }
    sealed class TestCanvas : Control
    {
        public TestCanvas()
        {
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            using var title = new Font("Segoe UI", 32, FontStyle.Bold);
            g.DrawString("LightCapture 2026", title, Brushes.DarkSlateBlue, 42, 32);
            using var text = new Font("Microsoft YaHei UI", 19);
            g.DrawString("轻截测试画布 · 只包含合成内容", text, Brushes.DimGray, 45, 100);
            Color[] colors = [Color.FromArgb(251, 56, 56), Color.FromArgb(43, 105, 222), Color.FromArgb(48, 132, 48), Color.FromArgb(247, 136, 58)];
            for (int i = 0; i < 4; i++)
            {
                using var b = new SolidBrush(colors[i]);
                g.FillRectangle(b, 45 + i * 235, 180, 200, 145);
                g.DrawString(ColorTranslator.ToHtml(colors[i]), text, Brushes.Black, 45 + i * 235, 338);
            }
            g.DrawString("中文输入测试：你好，世界！\nRectangle · Arrow · Text · Mosaic\n快捷键截图 / 撤销 / 复制 / 保存", text, Brushes.Black, new PointF(48, 405));
            using var pen = new Pen(Color.LightGray);
            for (int x = 0; x < Width; x += 20)
                g.DrawLine(pen, x, 580, x, Height);
        }
    }
    sealed class LongTestCanvas : Control
    {
        public LongTestCanvas()
        {
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            using var font = new Font("Microsoft YaHei UI", 16);
            for (int i = 0; i < 75; i++)
            {
                int y = i * 80;
                using var b = new SolidBrush(i % 2 == 0 ? Color.FromArgb(230, 239, 253) : Color.FromArgb(244, 247, 251));
                g.FillRectangle(b, 0, y, Width, 78);
                g.DrawString($"条目 {i + 1:00}  LightCapture 长图测试  {(i * 7117) % 9999:0000}", font, Brushes.DarkSlateGray, 30, y + 20);
                g.FillRectangle(Brushes.CornflowerBlue, 750 + (i * 13) % 110, y + 12, 45 + (i % 4) * 10, 48);
            }
        }
    }
}
