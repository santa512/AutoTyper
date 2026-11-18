using System.IO;
using System.Runtime.InteropServices;
using RapidOcrNet;
using SkiaSharp;

namespace AutoTyper;

internal sealed record OcrText(string Text, float Confidence, bool NeedsReview = false);

internal static class EnglishOcr
{
    private static readonly object Sync = new();
    // One CPU model set is reused across selections; recognition is serialized.
    private static RapidOcr? _engine;

    internal static OcrText? Recognize(CapturedScreen capture, CancellationToken cancellationToken = default)
    {
        Validate(capture);
        cancellationToken.ThrowIfCancellationRequested();
        lock (Sync)
        {
            EnsureEngine();
            using SKBitmap? image = Prepare(capture, out bool lowContrast);
            if (image is null) return null;
            OcrResult result = _engine!.Detect(image, RapidOcrOptions.Default with
            {
                TextScore = 0.5f,
                ImgResize = 2048,
                MaxSideLen = 2048
            }, cancellationToken);
            var blocks = result.TextBlocks.Where(block => !string.IsNullOrWhiteSpace(block.Text)).ToArray();
            if (blocks.Length == 0) return null;
            string text = string.Join(Environment.NewLine, blocks.Select(block => block.Text.Trim())).Trim();
            if (!text.Any(char.IsLetterOrDigit)) return null;
            float[] scores = blocks.SelectMany(block => block.CharScores ?? []).ToArray();
            float confidence = scores.Length == 0 ? 0 : scores.Average();
            return new OcrText(text, confidence, lowContrast || scores.Any(score => score < 0.75f));
        }
    }

    private static SKBitmap? Prepare(CapturedScreen capture, out bool lowContrast)
    {
        int width = capture.Width, height = capture.Height;
        byte[] gray = new byte[width * height];
        int[] histogram = new int[256], borders = new int[256];
        int borderCount = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x, offset = i * 4;
            byte value = (byte)((29 * capture.Pixels[offset] + 150 * capture.Pixels[offset + 1]
                + 77 * capture.Pixels[offset + 2]) >> 8);
            gray[i] = value; histogram[value]++;
            if (x < 2 || x >= width - 2 || y < 2 || y >= height - 2)
            { borders[value]++; borderCount++; }
        }
        int low = Percentile(histogram, Math.Max(1, gray.Length / 1000));
        int high = Percentile(histogram, Math.Max(1, gray.Length - gray.Length / 1000));
        lowContrast = high - low < 96;
        if (high - low < 5) return null;
        bool invert = Percentile(borders, Math.Max(1, borderCount / 2)) < (low + high) / 2;
        int[] rows = new int[height];
        byte[] pixels = new byte[capture.Pixels.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            int value = Math.Clamp((gray[i] - low) * 255 / (high - low), 0, 255);
            byte normalized = (byte)(invert ? 255 - value : value);
            int offset = i * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = normalized;
            pixels[offset + 3] = 255; // GDI's undefined alpha must not erase the image.
            if (normalized < 160) rows[i / width]++;
        }
        var lineHeights = new List<int>();
        int run = 0;
        foreach (int count in rows.Append(0))
        {
            if (count >= Math.Max(1, width / 500)) run++;
            else { if (run >= 3 && run <= 150) lineHeights.Add(run); run = 0; }
        }
        lineHeights.Sort();
        int letterHeight = lineHeights.Count == 0 ? 28 : lineHeights[lineHeights.Count / 2];
        float scale = Math.Clamp(36f / letterHeight, 1, 4);
        scale = Math.Min(scale, (float)Math.Sqrt(24_000_000d / ((long)width * height)));
        using var original = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(pixels, 0, original.GetPixels(), pixels.Length);
        return original.Resize(new SKImageInfo(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKCubicResampler.CatmullRom))
            ?? throw new InvalidOperationException("Could not prepare the OCR image.");
    }

    private static int Percentile(int[] histogram, int target)
    {
        int total = 0;
        for (int i = 0; i < histogram.Length; i++) if ((total += histogram[i]) >= target) return i;
        return 255;
    }

    private static void EnsureEngine()
    {
        if (_engine is not null) return;
        // All content is extracted beside these assemblies by the single-file publish profile.
        string directory = Path.GetDirectoryName(typeof(EnglishOcr).Assembly.Location)
            is { Length: > 0 } path ? path : AppContext.BaseDirectory;
        foreach (string name in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" })
            NativeLibrary.Load(Path.Combine(directory, "x64", name), typeof(EnglishOcr).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
        string models = Path.Combine(directory, "Assets", "OCR");
        var engine = new RapidOcr();
        try
        {
            engine.InitModels(
                Path.Combine(models, "ch_PP-OCRv5_mobile_det.onnx"),
                Path.Combine(models, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                Path.Combine(models, "en_PP-OCRv5_rec_mobile.onnx"),
                Path.Combine(models, "ppocrv5_en_dict.txt"),
                numThread: Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
            _engine = engine;
        }
        catch { engine.Dispose(); throw; }
    }

    private static void Validate(CapturedScreen capture)
    {
        if (capture.Width <= 0 || capture.Height <= 0 || (long)capture.Width * capture.Height > 40_000_000
            || (long)capture.Width * capture.Height * 4 != capture.Pixels.Length)
            throw new ArgumentException("Select a smaller text region.");
    }
}
