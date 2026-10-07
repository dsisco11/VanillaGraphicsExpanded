using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the owned debug stream layouts and indexed orb output policy.</summary>
public sealed partial class LumOnDebugRenderer
{
    private static readonly VertexLayoutDesc DebugLineLayout = new([
        new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 28),
        new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 12, 28)]);
    private static readonly VertexLayoutDesc DebugPointLayout = new([
        new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 1, 0, 16)]);
    private static readonly VertexLayoutDesc DebugOrbLayout = new([
        new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 1, 0, 16),
        new(2, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 2, 0, 8)]);

    private static readonly ColorBlendDesc RevealBlend = new()
    {
        Enabled = true,
        SourceRgb = BlendingFactorSrc.Zero,
        DestinationRgb = BlendingFactorDest.SrcColor,
        SourceAlpha = BlendingFactorSrc.Zero,
        DestinationAlpha = BlendingFactorDest.SrcAlpha
    };
    private static readonly ColorBlendDesc AccumulateBlend = new()
    {
        Enabled = true,
        SourceRgb = BlendingFactorSrc.One,
        DestinationRgb = BlendingFactorDest.One,
        SourceAlpha = BlendingFactorSrc.One,
        DestinationAlpha = BlendingFactorDest.One
    };
    private static readonly ColorBlendDesc[] OrbBlending =
        [RevealBlend, RevealBlend, new(), AccumulateBlend, AccumulateBlend, AccumulateBlend];
}
