using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Projects native stencil-face selectors into the existing independent knowledge fields.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Selects represented faces without rejecting native enum values or claiming a successful native change.</summary>
    private static StencilStateKnowledge NativeStencilFields(StencilFace face,
        StencilStateKnowledge front, StencilStateKnowledge back) => face switch
    {
        StencilFace.Front => front,
        StencilFace.Back => back,
        StencilFace.FrontAndBack => front | back,
        _ => StencilStateKnowledge.None
    };
    #endregion
}
