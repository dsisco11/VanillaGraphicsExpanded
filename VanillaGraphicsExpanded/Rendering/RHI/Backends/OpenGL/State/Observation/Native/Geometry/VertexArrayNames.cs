namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Tracks array identity prerequisites for buffer realization without certifying vertex layouts.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains reserved array names so EXT buffer-side realization can establish native preconditions.</summary>
    [InterceptOpenGL("glGenVertexArrays")]
    internal static void ObserveGenVertexArrays(int count, uint* names)
    {
        ObserveVertexArrayNames(count, names, false);
    }

    /// <summary>Retains realized array identities for core named buffer binding preconditions.</summary>
    [InterceptOpenGL("glCreateVertexArrays")]
    internal static void ObserveCreateVertexArrays(int count, uint* names)
    {
        ObserveVertexArrayNames(count, names, true);
    }
    #endregion

    #region Private
    /// <summary>Consumes factory outputs once; configuration certification remains with its existing owner.</summary>
    private static void ObserveVertexArrayNames(int count, uint* names, bool realized)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
        {
            int id = unchecked((int)names[i]);
            if (id == 0 || manager.FindResource(GpuResourceKind.VertexArray, id) is not null) continue;
            var entry = manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, id);
            if (realized)
            {
                manager.PublishMetadata(entry, new GpuVertexArrayMetadata(), GpuMetadataChange.None);
                Current.EstablishVertexArrayElementKnowledge(id, 0);
            }
        }
    }
    #endregion
}
