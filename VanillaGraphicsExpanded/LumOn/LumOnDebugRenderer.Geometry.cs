using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the owned debug stream layouts and indexed orb output policy.</summary>
public sealed partial class LumOnDebugRenderer
{
    private static readonly RHIVertexLayout DebugLineLayout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 28),
        new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 12, 28)]);
    private static readonly RHIVertexLayout DebugPointLayout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 16)]);
    private static readonly RHIVertexLayout DebugOrbLayout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 16),
        new(2, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 2, 0, 8)]);

    private static readonly ColorBlendDesc RevealBlend = new()
    {
        Enabled = true,
        SourceRgb = BlendFactor.Zero,
        DestinationRgb = BlendFactor.SourceColor,
        SourceAlpha = BlendFactor.Zero,
        DestinationAlpha = BlendFactor.SourceAlpha
    };
    private static readonly ColorBlendDesc AccumulateBlend = new()
    {
        Enabled = true,
        SourceRgb = BlendFactor.One,
        DestinationRgb = BlendFactor.One,
        SourceAlpha = BlendFactor.One,
        DestinationAlpha = BlendFactor.One
    };
    private static readonly ColorBlendDesc[] OrbBlending =
        [RevealBlend, RevealBlend, new(), AccumulateBlend, AccumulateBlend, AccumulateBlend];
}
