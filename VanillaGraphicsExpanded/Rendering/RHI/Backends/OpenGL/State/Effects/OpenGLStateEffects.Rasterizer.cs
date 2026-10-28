using OpenTK.Graphics.OpenGL;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes admitted rasterizer effects without owning cached state or suppression.</summary>
internal static partial class OpenGLStateEffects
{
    #region Internal API
    /// <summary>Issues the core Enable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Enable(EnableCap capability)
    {
        if (capability == EnableCap.ScissorTest) OpenGLDriverCalls.Enable((Core.IndexedEnableCap)Core.EnableCap.ScissorTest, 0);
        else OpenGLDriverCalls.Enable((Core.EnableCap)capability);
    }

    /// <summary>Issues the core Disable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Disable(EnableCap capability)
    {
        if (capability == EnableCap.ScissorTest) OpenGLDriverCalls.Disable((Core.IndexedEnableCap)Core.EnableCap.ScissorTest, 0);
        else OpenGLDriverCalls.Disable((Core.EnableCap)capability);
    }

    /// <summary>Issues the core Enable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Enable(IndexedEnableCap capability, int index) => OpenGLDriverCalls.Enable((Core.IndexedEnableCap)capability, index);

    /// <summary>Issues the core Disable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Disable(IndexedEnableCap capability, int index) => OpenGLDriverCalls.Disable((Core.IndexedEnableCap)capability, index);

    /// <summary>Issues the core CullFace effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void CullFace(CullFaceMode mode) => OpenGLDriverCalls.CullFace((Core.CullFaceMode)mode);

    /// <summary>Issues the core FrontFace effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void FrontFace(FrontFaceDirection direction) => OpenGLDriverCalls.FrontFace((Core.FrontFaceDirection)direction);

    /// <summary>Issues the core PolygonMode effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PolygonMode(MaterialFace face, PolygonMode mode) => OpenGLDriverCalls.PolygonMode((Core.MaterialFace)face, (Core.PolygonMode)mode);

    /// <summary>Issues the core PolygonOffset effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PolygonOffset(float factor, float units) => OpenGLDriverCalls.PolygonOffset(factor, units);

    /// <summary>Issues the core LineWidth effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void LineWidth(float width) => OpenGLDriverCalls.LineWidth(width);

    /// <summary>Issues the core PointSize effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PointSize(float size) => OpenGLDriverCalls.PointSize(size);

    /// <summary>Issues the core ClipControl effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ClipControl(ClipOrigin origin, ClipDepthMode depth) => OpenGLDriverCalls.ClipControl((Core.ClipOrigin)origin, (Core.ClipDepthMode)depth);

    /// <summary>Issues the core PointParameter effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PointParameter(PointParameterName parameter, int value) => OpenGLDriverCalls.PointParameter((Core.PointParameterName)parameter, value);

    /// <summary>Issues the core Viewport effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Viewport(int x, int y, int width, int height) => Core.GL.ViewportIndexed(0, x, y, width, height);

    /// <summary>Issues the core Scissor effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Scissor(int x, int y, int width, int height) => Core.GL.ScissorIndexed(0, x, y, width, height);

    /// <summary>Issues the core ProvokingVertex effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ProvokingVertex(ProvokingVertexMode mode) => OpenGLDriverCalls.ProvokingVertex((Core.ProvokingVertexMode)mode);

    /// <summary>Issues the core PatchParameter effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PatchParameter(PatchParameterInt parameter, int value) => OpenGLDriverCalls.PatchParameter((Core.PatchParameterInt)parameter, value);

    /// <summary>Issues the core PrimitiveRestartIndex effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PrimitiveRestartIndex(uint index) => OpenGLDriverCalls.PrimitiveRestartIndex(index);

    #endregion
}
