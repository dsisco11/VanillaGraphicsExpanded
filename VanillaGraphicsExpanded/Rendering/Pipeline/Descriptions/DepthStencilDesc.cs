using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable depth and stencil intent, independent of mutable cache knowledge.</summary>
public sealed record DepthStencilDesc
{
    public bool DepthTest { get; init; }
    public ComparisonFunction DepthComparison { get; init; } = ComparisonFunction.Less;
    public bool DepthWrite { get; init; }
    public bool StencilTest { get; init; }
    public StencilFaceDesc Front { get; init; } = new();
    public StencilFaceDesc Back { get; init; } = new();
    // Depth range is deliberately fixed rather than an undeclared dynamic value.
    public double DepthRangeNear => 0;
    public double DepthRangeFar => 1;
}
