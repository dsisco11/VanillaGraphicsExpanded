using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable depth and stencil intent, independent of mutable cache knowledge.</summary>
internal sealed record DepthStencilDesc
{
    public bool DepthTest { get; init; }
    public DepthFunction DepthComparison { get; init; } = DepthFunction.Less;
    public bool DepthWrite { get; init; }
    public bool StencilTest { get; init; }
    public StencilFaceDesc Front { get; init; } = new();
    public StencilFaceDesc Back { get; init; } = new();
    // Depth range is deliberately fixed rather than an undeclared dynamic value.
    public double DepthRangeNear => 0;
    public double DepthRangeFar => 1;
}
