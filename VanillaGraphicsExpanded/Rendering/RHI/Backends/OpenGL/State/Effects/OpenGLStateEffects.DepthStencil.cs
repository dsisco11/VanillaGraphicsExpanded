using OpenTK.Graphics.OpenGL;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes admitted depthstencil effects without owning cached state or suppression.</summary>
internal static partial class OpenGLStateEffects
{
    #region Internal API
    /// <summary>Issues the core DepthFunc effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void DepthFunc(DepthFunction function) => Core.GL.DepthFunc((Core.DepthFunction)function);

    /// <summary>Issues the core DepthMask effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void DepthMask(bool enabled) => Core.GL.DepthMask(enabled);

    /// <summary>Issues the core DepthRange effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void DepthRange(double near, double far) => Core.GL.DepthRangeIndexed(0, near, far);

    /// <summary>Issues the core StencilFuncSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, uint mask) => Core.GL.StencilFuncSeparate((Core.StencilFace)face, (Core.StencilFunction)function, reference, mask);

    /// <summary>Issues the core StencilMaskSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void StencilMaskSeparate(StencilFace face, uint mask) => Core.GL.StencilMaskSeparate((Core.StencilFace)face, mask);

    /// <summary>Issues the core StencilOpSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void StencilOpSeparate(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass) => Core.GL.StencilOpSeparate((Core.StencilFace)face, (Core.StencilOp)fail, (Core.StencilOp)depthFail, (Core.StencilOp)pass);

    #endregion
}
