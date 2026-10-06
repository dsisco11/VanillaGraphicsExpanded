using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>One face's stencil operations; references are declared draw dynamics.</summary>
internal sealed record StencilFaceDesc
{
    public StencilFunction Comparison { get; init; } = StencilFunction.Always;
    public uint ReadMask { get; init; } = uint.MaxValue;
    public uint WriteMask { get; init; } = uint.MaxValue;
    public StencilOp Fail { get; init; } = StencilOp.Keep;
    public StencilOp DepthFail { get; init; } = StencilOp.Keep;
    public StencilOp Pass { get; init; } = StencilOp.Keep;
}
