namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Registers native memory-object results without inventing imported payload metadata.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Consumes returned names synchronously and publishes the explicit empty schema once.</summary>
    [InterceptOpenGL("glCreateMemoryObjectsEXT")]
    internal static void ObserveCreateMemoryObjectsEXT(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        for (int index = 0; index < count; index++)
            GpuResourceManagerSystem.CurrentManager!.ObserveMemoryObjectCreation(unchecked((int)names[index]));
    }
    #endregion
}
