using System.Drawing.Imaging;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LightCapture;

internal sealed record RecognizedLine(string Text, RectangleF Bounds);
internal sealed record RecognizedText(string Text, IReadOnlyList<RecognizedLine> Lines, string Language);
internal static class TextServices
{
    public static async Task<RecognizedText> RecognizeAsync(Bitmap image, string language, CancellationToken token)
    {
        OcrEngine? engine = string.IsNullOrEmpty(language) ? OcrEngine.TryCreateFromUserProfileLanguages() : OcrEngine.TryCreateFromLanguage(new Language(language));
        engine ??= OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(e => e != null);
        if (engine == null)
            throw new InvalidOperationException("这台电脑没有可用的 Windows OCR 语言包。请在 Windows 语言设置中添加所需语言的文字识别功能。");
        double scale = Math.Min(1, Math.Min(OcrEngine.MaxImageDimension / (double)image.Width, OcrEngine.MaxImageDimension / (double)image.Height));
        using var resized = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(Point.Empty, resized.Size));
        }
        using var png = new MemoryStream();
        resized.Save(png, ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png.ToArray());
            await writer.StoreAsync().AsTask(token);
            await writer.FlushAsync().AsTask(token);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token);
        var result = await engine.RecognizeAsync(software).AsTask(token);
        token.ThrowIfCancellationRequested();
        var lines = result.Lines.Select(line =>
        {
            var rects = line.Words.Select(w => new RectangleF((float)(w.BoundingRect.X / scale), (float)(w.BoundingRect.Y / scale), (float)(w.BoundingRect.Width / scale), (float)(w.BoundingRect.Height / scale))).ToArray();
            RectangleF r = rects.Length == 0 ? RectangleF.Empty : rects.Aggregate(RectangleF.Union);
            return new RecognizedLine(line.Text, r);
        }).ToArray();
        return new(string.Join(Environment.NewLine, lines.Select(l => l.Text)), lines, engine.RecognizerLanguage.LanguageTag);
    }
    public static IEnumerable<string> TranslationChunks(string text, int maxBytes = 450)
    {
        var current = new StringBuilder();
        int count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int size = rune.Utf8SequenceLength;
            if (count + size > maxBytes && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
                count = 0;
            }
            current.Append(rune.ToString());
            count += size;
            if (rune.Value == '\n' && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
                count = 0;
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }
    public static Task<string> TranslateAsync(string text, string source, string target, CancellationToken token)
        => TranslationService.TranslateAsync(text, source, target, new(), token);
}
