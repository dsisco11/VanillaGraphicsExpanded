namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes generated buffer names during native output lifetime.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reserves nonzero names without claiming realization or overwriting live allocations.</summary>
    [InterceptOpenGL("glGenBuffers")]
    [InterceptOpenGL("glGenBuffersARB")]
    internal static void ObserveGenBuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int index = 0; index < count; index++)
            manager.ObserveBufferCreation(unchecked((int)names[index]), false);
    }
    #endregion
}
