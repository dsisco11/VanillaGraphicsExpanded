using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Defines complete liquid output policies independently of ambient engine framebuffer blending.</summary>
internal static class LiquidPipelineStates
{
    internal static readonly RHIColorAttachment[] SurfaceOutputs = [new(AttachmentIndex:0), new(AttachmentIndex:1), new(AttachmentIndex:2), new(AttachmentIndex:3), new(AttachmentIndex:4), new(AttachmentIndex:5)];
    // The installed engine liquid-depth framebuffer has no color image; discard the shader's dummy output.
    internal static readonly RHIColorAttachment[] DepthOutputs = [new(DiscardOutput: true)];
    private static readonly ColorBlendDesc Reveal = new()
    {
        Enabled = true,
        SourceRgb = BlendFactor.Zero,
        DestinationRgb = BlendFactor.SourceColor,
        SourceAlpha = BlendFactor.Zero,
        DestinationAlpha = BlendFactor.SourceAlpha
    };
    private static readonly ColorBlendDesc Add = new()
    {
        Enabled = true,
        SourceRgb = BlendFactor.One,
        DestinationRgb = BlendFactor.One,
        SourceAlpha = BlendFactor.One,
        DestinationAlpha = BlendFactor.One
    };
    private static readonly ColorBlendDesc Glow = new()
    {
        Enabled = true,
        SourceRgb = BlendFactor.SourceAlpha,
        DestinationRgb = BlendFactor.OneMinusSourceAlpha,
        SourceAlpha = BlendFactor.SourceAlpha,
        DestinationAlpha = BlendFactor.OneMinusSourceAlpha
    };
    internal static readonly ColorBlendDesc[] SurfaceBlending = [Reveal, Reveal, Glow, Add, Add, Add];
    internal static readonly ColorBlendDesc[] DepthBlending = [new()];
    internal static readonly ColorBlendDesc[] VolumeBlending = [Add, Add];
    internal static readonly DepthStencilDesc Depth = new() { DepthTest = true, DepthWrite = true, DepthComparison = ComparisonFunction.Less };
    internal static readonly DepthStencilDesc Surface = new() { DepthTest = true, DepthWrite = false, DepthComparison = ComparisonFunction.Less };
}
