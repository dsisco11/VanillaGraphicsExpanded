using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>One face's stencil operations; references are declared draw dynamics.</summary>
public sealed record StencilFaceDesc
{
    public ComparisonFunction Comparison { get; init; } = ComparisonFunction.Always;
    public uint ReadMask { get; init; } = uint.MaxValue;
    public uint WriteMask { get; init; } = uint.MaxValue;
    public StencilOperation Fail { get; init; } = StencilOperation.Keep;
    public StencilOperation DepthFail { get; init; } = StencilOperation.Keep;
    public StencilOperation Pass { get; init; } = StencilOperation.Keep;
}
