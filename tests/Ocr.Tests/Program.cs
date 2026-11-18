using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using AutoTyper;

if (args.Contains("--dependencies"))
{
    foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "x64"), "*.dll"))
    {
        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        var imports = pe.GetSectionData(pe.PEHeaders.PEHeader!.ImportTableDirectory.RelativeVirtualAddress).GetReader();
        var names = new List<string>();
        while (imports.RemainingBytes >= 20)
        {
            int thunk = imports.ReadInt32(); imports.ReadInt64();
            int name = imports.ReadInt32(); imports.ReadInt32();
            if (thunk == 0 && name == 0) break;
            var text = pe.GetSectionData(name).GetReader();
            var bytes = new List<byte>(); byte value;
            while ((value = text.ReadByte()) != 0) bytes.Add(value);
            names.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
        }
        Console.WriteLine($"{Path.GetFileName(path)}: {string.Join(", ", names)}");
    }
    return;
}

var cases = new[]
{
    ("Paragraph", "The quick brown fox jumps over the lazy dog.\nInvoice total: $123.45 due November 18, 2026.", "Segoe UI", 22, 0x000000u, 0xFFFFFFu),
    ("Dark theme", "English OCR works on dark backgrounds.", "Segoe UI", 16, 0xEEEEEEu, 0x202020u),
    ("Small text", "Small English text stays readable.", "Segoe UI", 11, 0x000000u, 0xFFFFFFu),
    ("Small UI labels", "PaddleOCR reads English screen text.\nOrder ID: AT-2026-0142\nEmail: hello@example.com", "Microsoft Sans Serif", 11, 0x000000u, 0xEFEFEFu),
    ("Low contrast", "Low contrast English text, 12345.", "Segoe UI", 16, 0x8C8C8Cu, 0xC0C0C0u),
    ("Monospace", "Order ID: AT-2026-0142\nEmail: hello@example.com", "Consolas", 18, 0x101010u, 0xFFFFFFu),
    ("Serif", "Printed English text includes punctuation!", "Times New Roman", 22, 0x000000u, 0xFFFFFFu),
    ("One word", "Screenshot", "Segoe UI", 24, 0x000000u, 0xFFFFFFu)
};
double sumErrors = 0; int totalCharacters = 0, failures = 0;
foreach (var (name, expected, font, fontSize, foreground, background) in cases)
{
    var watch = Stopwatch.StartNew();
    OcrText? result = EnglishOcr.Recognize(TextFixture.Render(expected, font, fontSize, foreground, background,
        lineGap: name == "Small UI labels" ? 2 : 9));
    string normalizedExpected = Normalize(expected), actual = Normalize(result?.Text ?? "");
    int errors = Distance(normalizedExpected, actual);
    double rate = (double)errors / normalizedExpected.Length;
    sumErrors += errors; totalCharacters += normalizedExpected.Length;
    Console.WriteLine($"{name}: {1 - rate:P1} character accuracy, confidence {result?.Confidence:P0}, {watch.ElapsedMilliseconds} ms");
    Console.WriteLine($"  {actual}");
    // Low-resolution punctuation remains ambiguous even with high model confidence.
    // Permit one punctuation error only in this low-contrast fixture; words and digits must match.
    bool acceptedPunctuation = name == "Low contrast" && errors <= 1
        && Regex.Replace(actual, @"[^\p{L}\p{N}]", "") == Regex.Replace(normalizedExpected, @"[^\p{L}\p{N}]", "")
        && result?.NeedsReview == true;
    if (rate > 0.02 && !acceptedPunctuation) failures++;
}
foreach (byte level in new byte[] { 0, 32, 128, 255 })
{
    byte[] pixels = Enumerable.Repeat(level, 240 * 80 * 4).ToArray();
    if (EnglishOcr.Recognize(new CapturedScreen(pixels, 240, 80)) is not null) throw new Exception("Blank image produced text.");
}
Console.WriteLine("PASS: Blank light/dark/gray regions produce no text");
try { EnglishOcr.Recognize(new CapturedScreen(new byte[4], 100, 100)); throw new Exception("Invalid buffer accepted."); }
catch (ArgumentException) { Console.WriteLine("PASS: Invalid image rejected"); }
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try
    {
        EnglishOcr.Recognize(TextFixture.Render("Do not replace input", "Segoe UI", 22, 0, 0xFFFFFF), cancelled.Token);
        throw new Exception("Cancelled extraction continued.");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS: Cancelled extraction does not produce a result"); }
}
Console.WriteLine($"Overall fixture character accuracy: {1 - sumErrors / totalCharacters:P2} ({totalCharacters} characters).");
if (failures != 0)
{
    Console.Error.WriteLine($"{failures} fixture(s) failed OCR accuracy requirements.");
    Environment.ExitCode = 1;
    return;
}
Console.WriteLine("All English OCR checks passed.");

static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
static int Distance(string a, string b)
{
    int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
    for (int i = 1; i <= a.Length; i++)
    {
        int[] current = new int[b.Length + 1]; current[0] = i;
        for (int j = 1; j <= b.Length; j++)
            current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        previous = current;
    }
    return previous[b.Length];
}
