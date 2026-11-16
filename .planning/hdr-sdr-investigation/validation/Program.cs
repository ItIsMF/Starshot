using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Starshot.Features.Codec;
using Starward.Codec.ICC;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

internal static class Probe
{
    [STAThread]
    static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        using var device = CanvasDevice.GetSharedDevice();
        Console.WriteLine($"Win2D device: {device.MaximumBitmapSizeInPixels}");
        var loader = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Codec.ImageLoader")!;
        var task = (Task)loader.GetMethod("LoadImageAsync")!.Invoke(null, [args[0], CancellationToken.None])!;
        await task;
        using var info = (IDisposable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        var bitmap = (CanvasBitmap)info.GetType().GetProperty("CanvasBitmap")!.GetValue(info)!;
        Console.WriteLine($"Loaded: {bitmap.SizeInPixels.Width}x{bitmap.SizeInPixels.Height}, {bitmap.Format}");
        var outputDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outputDir);
        var capture = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Screenshot.ScreenCaptureService")!;
        var levels = ((float MaxCLL, float MaxFALL))capture.GetMethod("GetContentLightLevels")!.Invoke(null, [bitmap])!;
        Console.WriteLine($"Measured: MaxCLL={levels.MaxCLL}, MaxFALL={levels.MaxFALL}");
        foreach (var (white, referenceWhite) in new[] { (120f, 250f), (240f, 500f), (480f, 1000f) })
        {
            using var batch = Convert(bitmap, white, null);
            using var viewer = Convert(bitmap, white, levels.MaxCLL);
            Assert(batch.GetPixelBytes().SequenceEqual(viewer.GetPixelBytes()), "batch and cached viewer pixels differ");
            await CheckPng(batch, Path.Combine(outputDir, $"calibrated-{white}.png"));
            using var reference = File.OpenRead(Path.Combine(args[2], $"fixed-{referenceWhite}.png"));
            byte[] referencePixels = await ReadPng(reference);
            Assert(batch.GetPixelBytes().SequenceEqual(referencePixels), $"new {white} differs from pre-calibration {referenceWhite}");
            Console.WriteLine($"Calibration {white} -> {referenceWhite}: identical pixels");
            foreach (float displayWhite in new[] { 80f, 240f, 480f })
                CheckDisplayRoundtrip(bitmap, levels.MaxCLL, white, displayWhite, batch.GetPixelBytes());
        }
        CheckRamp(80f, 240f, [0f, 4.8f, 14.4f, 40f, 80f]);
        CheckRamp(1000f, 240f, [0f, 14.4f, 80f, 120f, 240f, 400f, 600f, 800f, 1000f]);
        CheckRamp(240f, 240f, [0f, 14.4f, 80f, 120f, 240f], capturedSdr: true);
        Console.WriteLine("PASS: proportional calibration, cached/uncached equality, PNG roundtrip, display normalization, HDR ramps, unmodified SDR capture brightness");
        await CodecChecks.Run(bitmap, outputDir);
    }

    static CanvasRenderTarget Convert(CanvasBitmap bitmap, float white, float? peak)
    {
        var capture = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Screenshot.ScreenCaptureService")!;
        return (CanvasRenderTarget)capture.GetMethod("TonemapToSdr")!.Invoke(null, [bitmap, white, peak])!;
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void CheckDisplayRoundtrip(CanvasBitmap bitmap, float peak, float white, float displayWhite, byte[] expected)
    {
        var type = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Codec.SdrToneMapEffect")!;
        using var linear = (IDisposable)Activator.CreateInstance(type)!;
        type.GetProperty("Source")!.SetValue(linear, bitmap);
        type.GetProperty("InputMaxLuminance")!.SetValue(linear, peak);
        type.GetProperty("OutputWhiteLevel")!.SetValue(linear, white);
        using var display = new WhiteLevelAdjustmentEffect
        {
            Source = (Windows.Graphics.Effects.IGraphicsEffectSource)linear,
            InputWhiteLevel = displayWhite, OutputWhiteLevel = 80,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var normalized = new WhiteLevelAdjustmentEffect
        {
            Source = display, InputWhiteLevel = 80, OutputWhiteLevel = displayWhite,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var gamma = new SrgbGammaEffect
        {
            Source = normalized, GammaMode = SrgbGammaMode.OETF,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), bitmap.SizeInPixels.Width,
            bitmap.SizeInPixels.Height, 96, DirectXPixelFormat.R8G8B8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = target.CreateDrawingSession()) ds.DrawImage(gamma);
        byte[] actual = target.GetPixelBytes();
        int maximum = actual.Zip(expected, (a,b) => Math.Abs(a-b)).Max();
        Assert(maximum <= 1, $"display white {displayWhite}: normalization mismatch {maximum}");
        Console.WriteLine($"Display white {displayWhite}, target {white}: max byte delta {maximum}");
    }

    static async Task CheckPng(CanvasBitmap input, string path)
    {
        var saver = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Codec.ImageSaver")!;
        var save = saver.GetMethods().Single(m => m.Name == "SaveAsPngAsync" && m.GetParameters()[0].ParameterType == typeof(CanvasBitmap));
        using var stream = new MemoryStream();
        await (Task)save.Invoke(null, [input, stream, ColorPrimaries.BT709, null, false, null, null])!;
        File.WriteAllBytes(path, stream.ToArray());
        stream.Position = 0;
        byte[] decoded = await ReadPng(stream);
        Assert(input.GetPixelBytes().SequenceEqual(decoded), "PNG encoder changed SDR pixels");
    }

    static async Task<byte[]> ReadPng(Stream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return data.DetachPixelData();
    }

    static void CheckRamp(float peak, float white, float[] nits, bool capturedSdr = false)
    {
        var pixels = new Half[nits.Length * 4];
        for (int i = 0; i < nits.Length; i++)
        {
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = (Half)(nits[i] / 80f);
            pixels[i * 4 + 3] = (Half)1;
        }
        using var input = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(),
            MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray(), nits.Length, 1, DirectXPixelFormat.R16G16B16A16Float);
        var capture = typeof(Starshot.Program).Assembly.GetType("Starshot.Features.Screenshot.ScreenCaptureService")!;
        using var rendered = capturedSdr
            ? (CanvasRenderTarget)capture.GetMethod("NormalizeSdrCapture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [input, white])!
            : Convert(input, white, peak);
        var bytes = rendered.GetPixelBytes();
        Console.WriteLine($"Ramp peak={peak}, white={white}, capturedSDR={capturedSdr}: " +
            string.Join(", ", nits.Select((n, i) => $"{n}nits={bytes[i * 4]}")));
        for (int i = 0; i < nits.Length; i++)
        {
            Assert(bytes[i * 4] == bytes[i * 4 + 1] && bytes[i * 4] == bytes[i * 4 + 2], "neutral ramp became colored");
            if (i > 0) Assert(bytes[i * 4] > bytes[(i - 1) * 4], "highlight steps merged or reversed");
            if (peak <= white)
            {
                double v = nits[i] / (capturedSdr ? white : white * 500.0 / 240.0);
                double encoded = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
                Assert(Math.Abs(bytes[i * 4] - Math.Round(encoded * 255)) <= 1, "ramp differs from scalar SDR normalization");
            }
        }
    }
}
