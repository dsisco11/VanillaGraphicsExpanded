using System.Runtime.InteropServices;
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
    private readonly List<LumOnWorldProbeTraceResult> surfaceRetries = new();
    private readonly List<SurfaceLightingQuery> surfaceQueryWork = new();
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
        pendingSurfaceResults.Clear(); surfaceRetries.Clear(); surfaceQueryWork.Clear(); surfaceRevision=-1; lightingProbeResources=null;
    }
    #endregion

    #region Resolution and upload
    private const int MaximumSurfaceRetries = 3;

    /// <summary>Publishes ready directions and retains only unresolved descriptors for bounded GPU retries.</summary>
    private void PublishSurfaceResult(LumOnWorldProbeClipmapGpuResources resources,
        LumOnWorldProbeClipmapGpuUploader uploader, in LumOnWorldProbeTraceResult source,
        in LumOnWorldProbeTraceResult resolved, ref int budget, bool limited,
        List<LumOnWorldProbeTraceResult>? retries)
    {
        if (scheduler == null) return;
        if (source.SurfaceRevision != surfaceRevision || !scheduler.IsCurrent(source.Request))
        { scheduler.Complete(source.Request, frameIndex, false); return; }
        bool uploaded = false;
        if (resolved.Success && !resolved.AtlasSamples.IsDefaultOrEmpty)
        {
            int bytes = 40 + 24 * resolved.AtlasSamples.Length;
            var uploadResult = resolved;
            uploaded = (!limited || bytes <= budget) && uploader.Upload(resources, MemoryMarshal.CreateReadOnlySpan(ref uploadResult, 1), bytes) > 0;
            if (!uploaded)
            {
                // Retry the original admission through normal scheduling if its ready subset
                // cannot fit. Never retain only unresolved hits while silently losing ready work.
                scheduler.Complete(source.Request, frameIndex, false);
                return;
            }
            if (limited) budget -= bytes;
            scheduler.MergeImportanceFlags(source.Request.Level, source.Request.StorageLinearIndex, source.ImportanceFlags);
        }
        var unresolved = resolved.RetrySamples;
        if (!unresolved.IsDefaultOrEmpty && retries != null && source.SurfaceRetryCount < MaximumSurfaceRetries)
        {
            // The same ticket stays in flight, so dirtying, relocation and cache revisions
            // reject delayed answers. Already uploaded directions are never queried again here.
            retries.Add(source with { AtlasSamples = unresolved, RetrySamples = default,
                SurfaceRetryCount = source.SurfaceRetryCount + 1 });
            return;
        }
        scheduler.Complete(source.Request, frameIndex, uploaded && unresolved.IsDefaultOrEmpty);
    }

    /// <summary>Polls one bounded query batch and shares the existing upload budget across partial and complete results.</summary>
    private void ResolveSurfaceLighting(LumOnWorldProbeClipmapGpuResources resources, LumOnWorldProbeClipmapGpuUploader uploader)
    {
        if (scheduler == null || traceService == null) return;
        int budget = config.WorldProbeClipmap.UploadBudgetBytesPerFrame;
        bool limited = budget > 0;
        surfaceRetries.Clear();
        var retries = surfaceRetries;
        if (surfaceQueries?.Pending == true)
        {
            if (!surfaceQueries.TryRead(out var completed)) return;
            int query = 0;
            foreach (var result in pendingSurfaceResults)
            {
                var resolved = WorldProbeSurfaceLighting.Resolve(result, completed, ref query);
                PublishSurfaceResult(resources, uploader, result, resolved, ref budget, limited, retries);
            }
            pendingSurfaceResults.Clear();
        }
        SurfaceLightingSnapshot snapshot = default;
        var scene = geometryProvider?.PrepareScene();
        bool canResolveHits = surfaceProvider != null && surfaceProvider.TryGetSurfaceLighting(out snapshot) &&
            snapshot.DependencyRevision == surfaceRevision && scene != null;

        surfaceQueryWork.Clear();
        var queries = surfaceQueryWork;
        int admittedBytes = 0;
        foreach (var retry in retries)
        {
            if (!canResolveHits || !scheduler.IsCurrent(retry.Request))
            { scheduler.Complete(retry.Request, frameIndex, false); continue; }
            // Retry descriptors are a strict subset of the previous <=4096-query batch.
            pendingSurfaceResults.Add(retry);
            admittedBytes += 40 + 24 * retry.AtlasSamples.Length;
            foreach (var sample in retry.AtlasSamples) queries.Add(sample.SurfaceHit!.Value);
        }
        int maxProbes = Math.Max(1, config.WorldProbeClipmap.TraceMaxProbesPerFrame);
        for (int i = 0; i < maxProbes && traceService.TryDequeueResult(out var result); i++)
        {
            int needed = result.AtlasSamples.IsDefaultOrEmpty ? 0 : result.AtlasSamples.Count(sample => sample.SurfaceHit.HasValue);
            int bytes = 40 + 24 * result.AtlasSamples.AsSpan().Length;
            if (!result.Success || result.SurfaceRevision != surfaceRevision || !scheduler.IsCurrent(result.Request) ||
                queries.Count + needed > SurfaceLightingQueryBatch.MaximumQueries ||
                (limited && admittedBytes + bytes > budget))
            { scheduler.Complete(result.Request, frameIndex, false, aborted: result.FailureReason == WorldProbeTraceFailureReason.Aborted); continue; }
            if (needed == 0 || !canResolveHits)
            {
                // Even without a cache, established sky directions can publish. Missing
                // surface directions remain absent from the atlas and retry via scheduling.
                int answer = 0;
                var resolved = WorldProbeSurfaceLighting.Resolve(result, ReadOnlySpan<SurfaceLightingQuery>.Empty, ref answer);
                PublishSurfaceResult(resources, uploader, result, resolved, ref budget, limited, null);
                continue;
            }
            admittedBytes += bytes;
            pendingSurfaceResults.Add(result);
            foreach (var sample in result.AtlasSamples)
                if (sample.SurfaceHit is { } hit) queries.Add(hit);
        }
        if (queries.Count == 0) return;
        surfaceQueries ??= new(capi);
        surfaceQueries.Submit(scene!, snapshot, CollectionsMarshal.AsSpan(queries));
    }
    #endregion
}
