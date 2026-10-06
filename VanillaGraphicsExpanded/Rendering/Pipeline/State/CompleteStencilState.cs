using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Native stencil parameter values and their independently valid fields.</summary>
internal struct CompleteStencilState
{
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteStencilKnowledge Known;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState Enables;
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
}
