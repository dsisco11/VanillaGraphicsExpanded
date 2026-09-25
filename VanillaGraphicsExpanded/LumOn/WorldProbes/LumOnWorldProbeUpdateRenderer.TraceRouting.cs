using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>Owns runtime backend changes without clearing displayed probe history.</summary>
internal sealed partial class LumOnWorldProbeUpdateRenderer
{
    #region Backend lifetime
    /// <summary>Creates bounded CPU and GPU backends sharing one scheduler lifetime.</summary>
    private void EnsureTraceRouting(IBlockAccessor worldAccessor)
    {
        if (traceService is not null) return;
        // Capture this scheduler, never the mutable renderer field: retired workers cannot
        // claim admissions on a replacement scheduler after clipmap resources are recreated.
        var owner = scheduler!;
        traceScene ??= new BlockAccessorWorldProbeTraceScene(worldAccessor, sampleVanillaLighting: false);
        var cpu = new LumOnWorldProbeTraceService(traceScene, 2048,
            (request, frame) => owner.TryClaim(request, frame));
        traceService = new LumOnWorldProbeTraceRouter(config.WorldProbeClipmap.EnableGpuTracing,
            cpu, new LumOnWorldProbeGpuTraceBackend(capi, worldAccessor.MapSizeY,
                () => geometryProvider?.PrepareScene(),
                () => surfaceProvider != null && surfaceProvider.TryGetSurfaceLighting(out var snapshot) ? snapshot : null,
                (request, frame) => owner.TryClaim(request, frame), traceScene,
                request => owner.IsCurrent(request)));
    }

    /// <summary>Retires queued, running and delayed lighting admissions before switching routing.</summary>
    private void PrepareTraceRouting()
    {
        if (traceService is null || traceService.EnableGpuTracing == config.WorldProbeClipmap.EnableGpuTracing) return;

        // Invalidate tickets first: even a worker that outlives disposal cannot claim or publish
        // an old admission. Published atlas data remains available while replacement work runs.
        scheduler?.RetireOutstanding(frameIndex);
        traceService.Dispose();
        traceService = null;
        surfaceQueries?.Dispose();
        surfaceQueries = null;
        RetireSurfaceResults();
        surfaceRetries.Clear();
        surfaceQueryWork.Clear();
        queriedGeometry = null;
    }
    #endregion
}
