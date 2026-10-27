using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Projects native stencil-face selectors into the existing independent knowledge fields.</summary>
internal static partial class EngineStateCalls
{
    #region Private
    /// <summary>Selects represented faces without rejecting native enum values or claiming a successful native change.</summary>
    private static StencilStateKnowledge StencilFields(StencilFace face,
        StencilStateKnowledge front, StencilStateKnowledge back) => face switch
    {
        StencilFace.Front => front,
        StencilFace.Back => back,
        StencilFace.FrontAndBack => front | back,
        _ => StencilStateKnowledge.None
    };
    #endregion
}
