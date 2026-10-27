using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Blend and write policy for a fragment draw-output slot, not an attachment index.</summary>
public sealed record ColorBlendDesc
{
    public bool Enabled { get; init; }
    public BlendOperation RgbEquation { get; init; } = BlendOperation.Add;
    public BlendOperation AlphaEquation { get; init; } = BlendOperation.Add;
    public BlendFactor SourceRgb { get; init; } = BlendFactor.One;
    public BlendFactor DestinationRgb { get; init; } = BlendFactor.Zero;
    public BlendFactor SourceAlpha { get; init; } = BlendFactor.One;
    public BlendFactor DestinationAlpha { get; init; } = BlendFactor.Zero;
    public bool WriteRed { get; init; } = true;
    public bool WriteGreen { get; init; } = true;
    public bool WriteBlue { get; init; } = true;
    public bool WriteAlpha { get; init; } = true;
}
