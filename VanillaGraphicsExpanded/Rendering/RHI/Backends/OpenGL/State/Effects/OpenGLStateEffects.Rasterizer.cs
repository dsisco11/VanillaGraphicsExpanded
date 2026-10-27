using OpenTK.Graphics.OpenGL;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes admitted rasterizer effects without owning cached state or suppression.</summary>
internal static partial class OpenGLStateEffects
{
    #region Internal API
    /// <summary>Issues the core Enable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Enable(EnableCap capability) => Core.GL.Enable((Core.EnableCap)capability);

    /// <summary>Issues the core Disable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Disable(EnableCap capability) => Core.GL.Disable((Core.EnableCap)capability);

    /// <summary>Issues the core Enable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Enable(IndexedEnableCap capability, int index) => Core.GL.Enable((Core.IndexedEnableCap)capability, index);

    /// <summary>Issues the core Disable effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Disable(IndexedEnableCap capability, int index) => Core.GL.Disable((Core.IndexedEnableCap)capability, index);

    /// <summary>Issues the core CullFace effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void CullFace(CullFaceMode mode) => Core.GL.CullFace((Core.CullFaceMode)mode);

    /// <summary>Issues the core FrontFace effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void FrontFace(FrontFaceDirection direction) => Core.GL.FrontFace((Core.FrontFaceDirection)direction);

    /// <summary>Issues the core PolygonMode effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PolygonMode(MaterialFace face, PolygonMode mode) => Core.GL.PolygonMode((Core.MaterialFace)face, (Core.PolygonMode)mode);

    /// <summary>Issues the core PolygonOffset effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PolygonOffset(float factor, float units) => Core.GL.PolygonOffset(factor, units);

    /// <summary>Issues the core LineWidth effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void LineWidth(float width) => Core.GL.LineWidth(width);

    /// <summary>Issues the core PointSize effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PointSize(float size) => Core.GL.PointSize(size);

    /// <summary>Issues the core ClipControl effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ClipControl(ClipOrigin origin, ClipDepthMode depth) => Core.GL.ClipControl((Core.ClipOrigin)origin, (Core.ClipDepthMode)depth);

    /// <summary>Issues the core PointParameter effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PointParameter(PointParameterName parameter, int value) => Core.GL.PointParameter((Core.PointParameterName)parameter, value);

    /// <summary>Issues the core Viewport effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Viewport(int x, int y, int width, int height) => Core.GL.Viewport(x, y, width, height);

    /// <summary>Issues the core Scissor effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void Scissor(int x, int y, int width, int height) => Core.GL.Scissor(x, y, width, height);

    /// <summary>Issues the core ProvokingVertex effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ProvokingVertex(ProvokingVertexMode mode) => Core.GL.ProvokingVertex((Core.ProvokingVertexMode)mode);

    /// <summary>Issues the core PatchParameter effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PatchParameter(PatchParameterInt parameter, int value) => Core.GL.PatchParameter((Core.PatchParameterInt)parameter, value);

    /// <summary>Issues the core PrimitiveRestartIndex effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void PrimitiveRestartIndex(uint index) => Core.GL.PrimitiveRestartIndex(index);

    #endregion
}
