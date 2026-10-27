using OpenTK.Graphics.OpenGL;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes admitted sampling effects without owning cached state or suppression.</summary>
internal static partial class OpenGLStateEffects
{
    #region Internal API
    /// <summary>Issues the core SampleCoverage effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void SampleCoverage(float value, bool invert) => Core.GL.SampleCoverage(value, invert);

    /// <summary>Issues the core MinSampleShading effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void MinSampleShading(float value) => Core.GL.MinSampleShading(value);

    /// <summary>Issues the core SampleMask effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void SampleMask(uint index, uint mask) => Core.GL.SampleMask(index, mask);

    #endregion
}
