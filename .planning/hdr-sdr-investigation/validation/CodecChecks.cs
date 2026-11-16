using System.Reflection;
using System.Runtime.InteropServices;
using System.IO.Hashing;
using System.Buffers.Binary;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Starward.Codec.ICC;
using Windows.Graphics.DirectX;

internal static class CodecChecks
{
    private static readonly Assembly App = typeof(Starshot.Program).Assembly;

    public static async Task Run(CanvasBitmap source, string outputDir)
    {
        // 缩小真实样本以检验编解码与动态范围，避免把编码耗时测试混入功能检查。
        using var hdr = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 320, 180, 96,
            DirectXPixelFormat.R16G16B16A16Float, CanvasAlphaMode.Premultiplied);
        using (var scale = new ScaleEffect { Source = source, Scale = new(320f / source.SizeInPixels.Width, 180f / source.SizeInPixels.Height) })
        using (var ds = hdr.CreateDrawingSession()) ds.DrawImage(scale);
        foreach (string format in new[] { "png", "avif", "jxl", "jxr" })
        {
            string path = Path.Combine(outputDir, $"hdr-roundtrip.{format}");
            await Save(hdr, path, format, true);
            using var decoded = await Load(path);
            var bitmap = GetBitmap(decoded);
            if (!(bool)decoded.GetType().GetProperty("HDR")!.GetValue(decoded)!)
                throw new Exception($"{format}: HDR flag lost");
            float error = HdrError(hdr, bitmap);
            if (error > 0.02f) throw new Exception($"{format}: HDR normalized RMSE {error} exceeds 2%");
            Console.WriteLine($"HDR {format} roundtrip: {bitmap.Format}, normalized RMSE {error:P4}");
            using var mapped = ToneMap(bitmap);
            using var expected = ToneMap(hdr);
            double sdrError = mapped.GetPixelBytes().Zip(expected.GetPixelBytes(), (a, b) => Math.Abs(a - b)).Average();
            if (sdrError > 2) throw new Exception($"{format}: SDR mean byte error {sdrError}");
            Console.WriteLine($"HDR {format} -> SDR: mean byte error {sdrError:F4}");
        }
        var bytes = new byte[256 * 32 * 4];
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 256; x++)
        {
            int i = (y * 256 + x) * 4;
            bytes[i] = (byte)x;
            bytes[i + 1] = (byte)(255 - x);
            bytes[i + 2] = (byte)(y * 8);
            bytes[i + 3] = 255;
        }
        using var sdr = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), bytes, 256, 32,
            DirectXPixelFormat.R8G8B8A8UIntNormalized);
        foreach (string format in new[] { "png", "jpg", "avif", "jxl" })
        {
            string path = Path.Combine(outputDir, $"sdr-roundtrip.{format}");
            await Save(sdr, path, format, false);
            using var decoded = await Load(path);
            var bitmap = GetBitmap(decoded);
            if ((bool)decoded.GetType().GetProperty("HDR")!.GetValue(decoded)!)
                throw new Exception($"{format}: SDR incorrectly detected as HDR");
            using var rgba = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 256, 32, 96,
                DirectXPixelFormat.R8G8B8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
            using (var ds = rgba.CreateDrawingSession()) ds.DrawImage(bitmap);
            double error = bytes.Zip(rgba.GetPixelBytes(), (a, b) => Math.Abs(a - b)).Average();
            double tolerance = format is "png" or "jxl" ? 0 : 2;
            if (error > tolerance) throw new Exception($"{format}: SDR mean byte error {error}");
            Console.WriteLine($"SDR {format} roundtrip: mean byte error {error:F4}");
        }
        await CheckSdrBt2020Png(outputDir);
        CheckRegionPreview(hdr, sdr);
        Console.WriteLine("PASS: HDR PNG/AVIF/JXL/JXR roundtrips and SDR mapping; SDR codecs, PNG classification, region preview");
    }

    private static CanvasBitmap GetBitmap(IDisposable info) =>
        (CanvasBitmap)info.GetType().GetProperty("CanvasBitmap")!.GetValue(info)!;

    private static async Task<IDisposable> Load(string path)
    {
        var loader = App.GetType("Starshot.Features.Codec.ImageLoader")!;
        var task = (Task)loader.GetMethod("LoadImageAsync")!.Invoke(null, [path, CancellationToken.None])!;
        await task;
        return (IDisposable)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task Save(CanvasBitmap bitmap, string path, string format, bool hdr)
    {
        var primaries = hdr ? ColorPrimaries.BT2020 : ColorPrimaries.BT709;
        using var stream = File.Create(path);
        if (format == "jxr")
        {
            await bitmap.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.JpegXR, 1);
            return;
        }
        (string Name, object[] Args) call = format switch
        {
            "png" => ("SaveAsPngAsync", [bitmap, stream, primaries, null, true, hdr ? 1000f : null, hdr ? 228f : null]),
            "jpg" => ("SaveAsJpegAsync", [bitmap, stream, 100]),
            "avif" => ("SaveAsAvifAsync", [bitmap, stream, primaries, 100, null, true, hdr ? 1000f : null, hdr ? 228f : null]),
            "jxl" => ("SaveAsJxlAsync", [bitmap, stream, primaries, 0f, null, true]),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        var method = App.GetType("Starshot.Features.Codec.ImageSaver")!.GetMethods()
            .Single(m => m.Name == call.Name && m.GetParameters()[0].ParameterType == typeof(CanvasBitmap));
        await (Task)method.Invoke(null, call.Args)!;
    }

    private static CanvasRenderTarget ToneMap(CanvasBitmap bitmap) =>
        (CanvasRenderTarget)App.GetType("Starshot.Features.Screenshot.ScreenCaptureService")!
            .GetMethod("TonemapToSdr")!.Invoke(null, [bitmap, 240f, null])!;

    private static void CheckRegionPreview(CanvasBitmap hdr, CanvasBitmap sdr)
    {
        var preview = App.GetType("Starshot.Features.Screenshot.RegionCaptureWindow")!
            .GetMethod("CreateDisplayBitmap", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var rendered = (CanvasBitmap)preview.Invoke(null, [hdr, 240f, 240f])!;
        using var expected = ToneMap(hdr);
        if (!rendered.GetPixelBytes().SequenceEqual(expected.GetPixelBytes()))
            throw new Exception("Region HDR preview differs from shared SDR mapping");
        if (!ReferenceEquals(sdr, preview.Invoke(null, [sdr, 80f, 240f])))
            throw new Exception("Region SDR preview must reuse original bytes");
        var white = new Half[] { (Half)3f, (Half)3f, (Half)3f, (Half)1f };
        using var captured = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(),
            MemoryMarshal.AsBytes(white.AsSpan()).ToArray(), 1, 1, DirectXPixelFormat.R16G16B16A16Float);
        using var normalized = (CanvasBitmap)preview.Invoke(null, [captured, 240f, 120f])!;
        if (normalized.GetPixelBytes().Any(b => b != 255))
            throw new Exception("Region SDR desktop white was dimmed by aesthetic calibration");
        Console.WriteLine("Region HDR preview/clipboard source matches SDR mapping; SDR white remains 255");
    }

    private static async Task CheckSdrBt2020Png(string outputDir)
    {
        // Independently tag an ordinary SDR PNG as BT.2020/sRGB. Primaries alone do not imply PQ.
        byte[] original = File.ReadAllBytes(Path.Combine(outputDir, "sdr-roundtrip.png"));
        byte[] chunk = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 4);
        "cICP"u8.CopyTo(chunk.AsSpan(4));
        new byte[] { 9, 13, 0, 1 }.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), Crc32.HashToUInt32(chunk.AsSpan(4, 8)));
        string path = Path.Combine(outputDir, "sdr-bt2020.png");
        using (var output = File.Create(path))
        {
            output.Write(original, 0, 33); // signature + IHDR
            output.Write(chunk);
            output.Write(original, 33, original.Length - 33);
        }
        using var decoded = await Load(path);
        if ((bool)decoded.GetType().GetProperty("HDR")!.GetValue(decoded)!)
            throw new Exception("BT.2020 SDR PNG was treated as PQ HDR");
        Console.WriteLine("BT.2020/sRGB PNG remains SDR");
    }

    private static float HdrError(CanvasBitmap expected, CanvasBitmap actual)
    {
        if (actual.SizeInPixels != expected.SizeInPixels || actual.Format != DirectXPixelFormat.R16G16B16A16Float)
            throw new Exception("HDR size or floating point storage lost");
        var a = MemoryMarshal.Cast<byte, Half>(expected.GetPixelBytes());
        var b = MemoryMarshal.Cast<byte, Half>(actual.GetPixelBytes());
        double sum = 0;
        float peak = 0, decodedPeak = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (i % 4 == 3) continue;
            float x = (float)a[i], y = (float)b[i];
            if (!float.IsFinite(y)) throw new Exception("Non-finite HDR output");
            peak = Math.Max(peak, x);
            decodedPeak = Math.Max(decodedPeak, y);
            sum += (x - y) * (x - y);
        }
        if (decodedPeak < peak * 0.9f) throw new Exception("HDR highlights clipped on save/load");
        return (float)Math.Sqrt(sum / (a.Length * 0.75)) / peak;
    }
}
