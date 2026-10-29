namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Tracks semaphore reservations separately from external payload realization.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Consumes returned names once; generation alone establishes no semaphore payload.</summary>
    [InterceptOpenGL("glGenSemaphoresEXT")]
    internal static void ObserveGenSemaphoresEXT(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        for (int i = 0; i < count; i++)
            GpuResourceManagerSystem.CurrentManager!.ObserveSemaphoreGeneration(unchecked((int)names[i]));
    }

    /// <summary>Uses an explicit caller result without issuing a compensating validity probe.</summary>
    [InterceptOpenGL("glIsSemaphoreEXT")]
    internal static void ObserveIsSemaphoreEXT(uint name, byte result)
    {
        if (result == 0 || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        GpuResourceManagerSystem.CurrentManager!.ObserveSemaphoreRealization(unchecked((int)name));
    }
    #endregion
}
