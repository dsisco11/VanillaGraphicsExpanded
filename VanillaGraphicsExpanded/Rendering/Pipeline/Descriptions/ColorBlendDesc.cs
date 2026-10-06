using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Blend and write policy for a fragment draw-output slot, not an attachment index.</summary>
internal sealed record ColorBlendDesc
{
    public bool Enabled { get; init; }
    public BlendEquationMode RgbEquation { get; init; } = BlendEquationMode.FuncAdd;
    public BlendEquationMode AlphaEquation { get; init; } = BlendEquationMode.FuncAdd;
    public BlendingFactorSrc SourceRgb { get; init; } = BlendingFactorSrc.One;
    public BlendingFactorDest DestinationRgb { get; init; } = BlendingFactorDest.Zero;
    public BlendingFactorSrc SourceAlpha { get; init; } = BlendingFactorSrc.One;
    public BlendingFactorDest DestinationAlpha { get; init; } = BlendingFactorDest.Zero;
    public bool WriteRed { get; init; } = true;
    public bool WriteGreen { get; init; } = true;
    public bool WriteBlue { get; init; } = true;
    public bool WriteAlpha { get; init; } = true;
}
