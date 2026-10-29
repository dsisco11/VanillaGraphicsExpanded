namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes core realized-buffer factory results without reading descriptors.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Publishes guaranteed defaults once for each new returned allocation.</summary>
    [InterceptOpenGL("glCreateBuffers")]
    internal static void ObserveCreateBuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        for (int index = 0; index < count; index++)
        {
            int id = unchecked((int)names[index]);
            if (id == 0 || manager.FindResource(GpuResourceKind.Buffer, id) is not null) continue;
            manager.ObserveBufferCreation(id, true);
            cache.EstablishBufferUnmapped(id);
        }
    }
    #endregion
}
