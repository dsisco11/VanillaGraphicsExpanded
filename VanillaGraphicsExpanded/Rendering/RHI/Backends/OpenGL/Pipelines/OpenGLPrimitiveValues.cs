using System;
using Native = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates supported primitive assembly at native execution and discovery boundaries.</summary>
internal static class OpenGLPrimitiveValues
{
    #region Internal API
    /// <summary>Selects the core primitive enum without relying on token-compatible semantic values.</summary>
    internal static OpenTK.Graphics.OpenGL4.PrimitiveType ToCore(PrimitiveTopology value) => value switch
    {
        PrimitiveTopology.Points => OpenTK.Graphics.OpenGL4.PrimitiveType.Points,
        PrimitiveTopology.Lines => OpenTK.Graphics.OpenGL4.PrimitiveType.Lines,
        PrimitiveTopology.LineStrip => OpenTK.Graphics.OpenGL4.PrimitiveType.LineStrip,
        PrimitiveTopology.LineLoop => OpenTK.Graphics.OpenGL4.PrimitiveType.LineLoop,
        PrimitiveTopology.Triangles => OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles,
        PrimitiveTopology.TriangleStrip => OpenTK.Graphics.OpenGL4.PrimitiveType.TriangleStrip,
        PrimitiveTopology.TriangleFan => OpenTK.Graphics.OpenGL4.PrimitiveType.TriangleFan,
        PrimitiveTopology.LinesAdjacency => OpenTK.Graphics.OpenGL4.PrimitiveType.LinesAdjacency,
        PrimitiveTopology.LineStripAdjacency => OpenTK.Graphics.OpenGL4.PrimitiveType.LineStripAdjacency,
        PrimitiveTopology.TrianglesAdjacency => OpenTK.Graphics.OpenGL4.PrimitiveType.TrianglesAdjacency,
        PrimitiveTopology.TriangleStripAdjacency => OpenTK.Graphics.OpenGL4.PrimitiveType.TriangleStripAdjacency,
        PrimitiveTopology.Patches => OpenTK.Graphics.OpenGL4.PrimitiveType.Patches,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Selects the native primitive mode without assuming numeric equivalence.</summary>
    internal static Native.PrimitiveType ToNative(PrimitiveTopology value) => value switch
    {
        PrimitiveTopology.Points => Native.PrimitiveType.Points,
        PrimitiveTopology.Lines => Native.PrimitiveType.Lines,
        PrimitiveTopology.LineStrip => Native.PrimitiveType.LineStrip,
        PrimitiveTopology.LineLoop => Native.PrimitiveType.LineLoop,
        PrimitiveTopology.Triangles => Native.PrimitiveType.Triangles,
        PrimitiveTopology.TriangleStrip => Native.PrimitiveType.TriangleStrip,
        PrimitiveTopology.TriangleFan => Native.PrimitiveType.TriangleFan,
        PrimitiveTopology.LinesAdjacency => Native.PrimitiveType.LinesAdjacency,
        PrimitiveTopology.LineStripAdjacency => Native.PrimitiveType.LineStripAdjacency,
        PrimitiveTopology.TrianglesAdjacency => Native.PrimitiveType.TrianglesAdjacency,
        PrimitiveTopology.TriangleStripAdjacency => Native.PrimitiveType.TriangleStripAdjacency,
        PrimitiveTopology.Patches => Native.PrimitiveType.Patches,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Projects an explicitly acquired native primitive mode into the supported semantic domain.</summary>
    internal static PrimitiveTopology FromNative(Native.PrimitiveType value) => value switch
    {
        Native.PrimitiveType.Points => PrimitiveTopology.Points,
        Native.PrimitiveType.Lines => PrimitiveTopology.Lines,
        Native.PrimitiveType.LineStrip => PrimitiveTopology.LineStrip,
        Native.PrimitiveType.LineLoop => PrimitiveTopology.LineLoop,
        Native.PrimitiveType.Triangles => PrimitiveTopology.Triangles,
        Native.PrimitiveType.TriangleStrip => PrimitiveTopology.TriangleStrip,
        Native.PrimitiveType.TriangleFan => PrimitiveTopology.TriangleFan,
        Native.PrimitiveType.LinesAdjacency => PrimitiveTopology.LinesAdjacency,
        Native.PrimitiveType.LineStripAdjacency => PrimitiveTopology.LineStripAdjacency,
        Native.PrimitiveType.TrianglesAdjacency => PrimitiveTopology.TrianglesAdjacency,
        Native.PrimitiveType.TriangleStripAdjacency => PrimitiveTopology.TriangleStripAdjacency,
        Native.PrimitiveType.Patches => PrimitiveTopology.Patches,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
}
