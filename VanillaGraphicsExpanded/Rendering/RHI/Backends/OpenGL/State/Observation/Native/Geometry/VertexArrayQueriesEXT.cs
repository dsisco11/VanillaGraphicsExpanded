namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes EXT first-use realization without issuing any configuration query.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Records specified initialization independently of getter output or selector acceptance.</summary>
    [InterceptOpenGL("glGetVertexArrayPointervEXT")]
    internal static void ObserveVertexArrayQueryEXT(uint array, int parameter, nint values)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.RealizeExtVertexArray(unchecked((int)array));
    }

    /// <summary>Tracks indexed EXT getter initialization while output memory remains caller-owned.</summary>
    [InterceptOpenGL("glGetVertexArrayPointeri_vEXT")]
    internal static void ObserveVertexArrayIndexedQueryEXT(uint array, uint index, int parameter, nint values)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.RealizeExtVertexArray(unchecked((int)array));
    }
    /// <summary>Consumes integer getter initialization without reading its result.</summary>
    [InterceptOpenGL("glGetVertexArrayIntegervEXT")]
    internal static void ObserveVertexArrayIntegerQueryEXT(uint array, int parameter, int* values)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.RealizeExtVertexArray(unchecked((int)array));
    }

    /// <summary>Consumes indexed integer getter initialization independently of its result.</summary>
    [InterceptOpenGL("glGetVertexArrayIntegeri_vEXT")]
    internal static void ObserveVertexArrayIndexedIntegerQueryEXT(uint array, uint index, int parameter, int* values)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.RealizeExtVertexArray(unchecked((int)array));
    }
    #endregion
}
