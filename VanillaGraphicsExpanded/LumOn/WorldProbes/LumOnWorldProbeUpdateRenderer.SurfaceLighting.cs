using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>Owns bounded GPU hit resolution between CPU tracing and world-probe publication.</summary>
internal sealed partial class LumOnWorldProbeUpdateRenderer
{
    private ISurfaceLightingProvider? surfaceProvider;
    private ITraceGeometrySceneProvider? geometryProvider;
    private SurfaceLightingQueryBatch? surfaceQueries;
    private readonly List<LumOnWorldProbeTraceResult> pendingSurfaceResults = new();
    private long surfaceRevision = -1;
    private LumOnWorldProbeClipmapGpuResources? lightingProbeResources;

    #region Cache ownership
    /// <summary>Injects render-thread providers; neither provider is passed to CPU tracing workers.</summary>
    internal void SetSurfaceLightingProvider(ISurfaceLightingProvider? provider, ITraceGeometrySceneProvider? geometry)
    { surfaceProvider=provider; geometryProvider=geometry; }

    /// <summary>Invalidates all dependent probe history when cache identity changes or becomes unavailable.</summary>
    private void PrepareSurfaceLighting(Gpu.LumOnWorldProbeClipmapGpuResources resources)
    {
        long revision=surfaceProvider != null && surfaceProvider.TryGetSurfaceLighting(out var value) ? value.DependencyRevision : -1;
        if (revision==surfaceRevision && ReferenceEquals(resources,lightingProbeResources)) return;
        lightingProbeResources=resources;
        surfaceRevision=revision;
        surfaceQueries?.Dispose(); surfaceQueries=null; pendingSurfaceResults.Clear();
        // Retiring the service prevents obsolete workers from publishing into the new scheduler generation.
        traceService?.Dispose(); traceService=null;
        scheduler?.ResetAll(); resources.ClearAll();
    }

    /// <summary>Retires pending queries without borrowing disposed cache resources.</summary>
    private void ReleaseSurfaceLighting()
    {
        surfaceQueries?.Dispose(); surfaceQueries=null;
        pendingSurfaceResults.Clear(); surfaceRevision=-1; lightingProbeResources=null;
    }
    #endregion

    #region Resolution and upload
    /// <summary>Reads only signaled GPU work, retries unresolved hits, and admits whole probe uploads within budget.</summary>
    private void ResolveSurfaceLighting(LumOnWorldProbeClipmapGpuResources resources, LumOnWorldProbeClipmapGpuUploader uploader)
    {
        if (scheduler == null || traceService == null) return;
        int budget=config.WorldProbeClipmap.UploadBudgetBytesPerFrame;
        bool limited=budget>0;
        if (surfaceQueries?.Pending == true)
        {
            if (!surfaceQueries.TryRead(out var completed)) return;
            int query=0;
            foreach (var result in pendingSurfaceResults)
            {
                var resolved=WorldProbeSurfaceLighting.Resolve(result, completed, ref query);
                bool current=resolved.Success && resolved.SurfaceRevision==surfaceRevision && scheduler.IsCurrent(resolved.Request);
                int bytes=40+24*resolved.AtlasSamples.Length;
                bool admitted=current && (!limited || bytes<=budget);
                bool uploaded=admitted && uploader.Upload(resources,new[]{resolved},bytes)>0;
                if (uploaded)
                {
                    if (limited) budget-=bytes;
                    scheduler.MergeImportanceFlags(resolved.Request.Level,resolved.Request.StorageLinearIndex,resolved.ImportanceFlags);
                }
                scheduler.Complete(resolved.Request,frameIndex,uploaded);
            }
            pendingSurfaceResults.Clear();
        }
        if (surfaceProvider == null || !surfaceProvider.TryGetSurfaceLighting(out var snapshot) ||
            snapshot.DependencyRevision!=surfaceRevision || geometryProvider?.PrepareScene() is not { } scene) return;

        var queries=new List<SurfaceLightingQuery>();
        int admittedBytes=0;
        int maxProbes=Math.Max(1,config.WorldProbeClipmap.TraceMaxProbesPerFrame);
        // CPU result draining is bounded too; a missing cache cannot grow an extra renderer queue.
        for (int i=0;i<maxProbes && traceService.TryDequeueResult(out var result);i++)
        {
            int needed=result.AtlasSamples.Count(sample=>sample.SurfaceHit.HasValue);
            int bytes=40+24*result.AtlasSamples.Length;
            if (!result.Success || result.SurfaceRevision!=surfaceRevision || !scheduler.IsCurrent(result.Request) ||
                queries.Count+needed>SurfaceLightingQueryBatch.MaximumQueries ||
                (limited && admittedBytes+bytes>budget))
            { scheduler.Complete(result.Request,frameIndex,false, aborted:result.FailureReason==WorldProbeTraceFailureReason.Aborted); continue; }
            if (needed==0)
            {
                bool uploaded=uploader.Upload(resources,new[]{result},bytes)>0;
                scheduler.Complete(result.Request,frameIndex,uploaded);
                if (uploaded) admittedBytes+=bytes;
                continue;
            }
            admittedBytes+=bytes;
            pendingSurfaceResults.Add(result);
            foreach (var sample in result.AtlasSamples)
                if (sample.SurfaceHit is { } hit) queries.Add(hit);
        }
        if (queries.Count==0) return;
        surfaceQueries ??= new(capi);
        surfaceQueries.Submit(scene,snapshot,queries.ToArray());
    }
    #endregion
}


