using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete stencil values independent of cache knowledge and native operations.</summary>
internal struct StencilState
{
    /// <summary>Observed FrontStencilFunction value, valid only when corresponding knowledge is established.</summary>
    public (StencilFunction Function, int Reference, uint Mask) FrontStencilFunction;
    /// <summary>Observed BackStencilFunction value, valid only when corresponding knowledge is established.</summary>
    public (StencilFunction Function, int Reference, uint Mask) BackStencilFunction;
    /// <summary>Declared or observed uint FrontStencilMask value.</summary>
    public uint FrontStencilMask;
    /// <summary>Declared or observed uint BackStencilMask value.</summary>
    public uint BackStencilMask;
    /// <summary>Observed FrontStencilOperation value, valid only when corresponding knowledge is established.</summary>
    public (StencilOp Fail, StencilOp DepthFail, StencilOp Pass) FrontStencilOperation;
    /// <summary>Observed BackStencilOperation value, valid only when corresponding knowledge is established.</summary>
    public (StencilOp Fail, StencilOp DepthFail, StencilOp Pass) BackStencilOperation;
    /// <summary>Cached native TestEnabled enable value.</summary>
    public bool TestEnabled;
}
