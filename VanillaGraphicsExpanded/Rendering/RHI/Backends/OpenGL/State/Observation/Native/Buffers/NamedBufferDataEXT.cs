namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes EXT named allocation including its implicit object initialization.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes defaults before classifying the EXT storage request.</summary>
    [InterceptOpenGL("glNamedBufferDataEXT")]
    internal static void ObserveNamedBufferDataEXT(uint a0, nint a1, nint a2, int a3)
    {
        ObserveBufferStorage(unchecked((int)a0), true, true, a1, a3, 0, false);
    }
    #endregion
}
