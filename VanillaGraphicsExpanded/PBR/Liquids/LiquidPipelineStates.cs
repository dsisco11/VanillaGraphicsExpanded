using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Defines complete liquid output policies independently of ambient engine framebuffer blending.</summary>
internal static class LiquidPipelineStates
{
    internal static readonly RenderPassColor[] SurfaceOutputs = [new(0), new(1), new(2), new(3), new(4), new(5)];
    // The installed engine liquid-depth framebuffer has no color image; discard the shader's dummy output.
    internal static readonly RenderPassColor[] DepthOutputs = [new(-1, DiscardOutput: true)];
    internal static readonly RenderPassColor[] VolumeOutputs =
        [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)),
         new(1, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))];
    private static readonly ColorBlendDesc Reveal = new()
    {
        Enabled = true,
        SourceRgb = BlendingFactorSrc.Zero,
        DestinationRgb = BlendingFactorDest.SrcColor,
        SourceAlpha = BlendingFactorSrc.Zero,
        DestinationAlpha = BlendingFactorDest.SrcAlpha
    };
    private static readonly ColorBlendDesc Add = new()
    {
        Enabled = true,
        SourceRgb = BlendingFactorSrc.One,
        DestinationRgb = BlendingFactorDest.One,
        SourceAlpha = BlendingFactorSrc.One,
        DestinationAlpha = BlendingFactorDest.One
    };
    private static readonly ColorBlendDesc Glow = new()
    {
        Enabled = true,
        SourceRgb = BlendingFactorSrc.SrcAlpha,
        DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha,
        SourceAlpha = BlendingFactorSrc.SrcAlpha,
        DestinationAlpha = BlendingFactorDest.OneMinusSrcAlpha
    };
    internal static readonly ColorBlendDesc[] SurfaceBlending = [Reveal, Reveal, Glow, Add, Add, Add];
    internal static readonly ColorBlendDesc[] DepthBlending = [new()];
    internal static readonly ColorBlendDesc[] VolumeBlending = [Add, Add];
    internal static readonly DepthStencilDesc Depth = new() { DepthTest = true, DepthWrite = true, DepthComparison = DepthFunction.Less };
    internal static readonly DepthStencilDesc Surface = new() { DepthTest = true, DepthWrite = false, DepthComparison = DepthFunction.Less };
}
