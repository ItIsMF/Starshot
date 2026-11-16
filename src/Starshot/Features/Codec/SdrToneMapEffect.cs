using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;

namespace Starshot.Features.Codec;

/// <summary>
/// HDR scRGB → 以 SDR 白为 1 的线性 BT.709；显示白适配和 sRGB 编码由调用方处理。
/// </summary>
internal partial class SdrToneMapEffect : CanvasEffect
{
    public IGraphicsEffectSource Source { get; set; }

    public float InputMaxLuminance { get; set; }

    public float OutputWhiteLevel { get; set; }

    protected override void BuildEffectGraph(CanvasEffectGraph effectGraph)
    {
        // 样本观感标定：保留外部亮度档位，使 240 档对应原 500 档的映射。
        float mappedWhiteLevel = OutputWhiteLevel * 500f / 240f;
        var toneMap = new HdrToneMapEffect
        {
            Source = Source,
            InputMaxLuminance = InputMaxLuminance,
            OutputMaxLuminance = mappedWhiteLevel,
            DisplayMode = HdrToneMapEffectDisplayMode.Hdr,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        var normalize = new WhiteLevelAdjustmentEffect
        {
            Source = toneMap,
            InputWhiteLevel = 80,
            OutputWhiteLevel = mappedWhiteLevel,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        effectGraph.RegisterOutputNode(normalize);
    }

    protected override void ConfigureEffectGraph(CanvasEffectGraph effectGraph) { }
}
