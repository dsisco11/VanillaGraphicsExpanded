namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes generated query names while their native output arrays remain valid.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reserves names in both roots and the supported ARB alias without descriptor reads.</summary>
    [InterceptOpenGL("glGenQueries")]
    [InterceptOpenGL("glGenQueriesARB")]
    internal static void ObserveGenQueries(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        for (int i = 0; i < count; i++)
            RHIResourceManagerSystem.CurrentManager!.ObserveQueryCreation(unchecked((int)names[i]), false);
    }

    /// <summary>Establishes target-specific objects only for admitted native factory arguments.</summary>
    [InterceptOpenGL("glCreateQueries")]
    internal static void ObserveCreateQueries(int target, int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        bool established = IsSupportedQueryTarget(target, true);
        bool uncertain = !GpuSupport.HasCachedCapabilitiesForCurrentContext && IsStatisticsQueryTarget(target);
        if (!established && !uncertain) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
        {
            int id = unchecked((int)names[i]);
            if (id == 0 || manager.FindResource(GpuResourceKind.Query, id) is not null) continue;
            manager.ObserveQueryCreation(id, established);
            if (established) cache.EstablishQueryTarget(id, target);
            else if (manager.FindResource(GpuResourceKind.Query, id) is GpuResourceMetadataEntry<GpuQueryMetadata> entry)
                entry.Withdraw(GpuMetadataChange.Metadata);
        }
    }

    /// <summary>Consumes a caller's existing realization result without issuing an identity query.</summary>
    [InterceptOpenGL("glIsQuery")]
    [InterceptOpenGL("glIsQueryARB")]
    internal static void ObserveIsQuery(uint name, byte result)
    {
        if (result != 0 && OpenGLObservationContext.TryGet(out _, out _, false))
            RHIResourceManagerSystem.CurrentManager!.RealizeObservedQuery(unchecked((int)name));
    }
    #endregion
}
