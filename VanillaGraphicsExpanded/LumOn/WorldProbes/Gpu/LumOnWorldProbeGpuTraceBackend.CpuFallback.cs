using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns CPU fallback admission and render-thread validation of delayed hybrid results.</summary>
internal sealed partial class LumOnWorldProbeGpuTraceBackend
{
    private readonly IWorldProbeTraceScene fallbackScene;
    private readonly Func<LumOnWorldProbeUpdateRequest, bool> isCurrent;
    private readonly Dictionary<long, FallbackLifetime> fallbackPending = new();
    private WorldProbeCpuFallbackService? fallback;
    private long nextFallbackId;
    private int fallbackFrame;

    /// <summary>Keeps borrowed resource identity on the render thread while workers see only immutable ray answers.</summary>
    private readonly record struct FallbackLifetime(TraceGeometryGpuScene? Scene, long Invalidation, long LightingRevision, WorldProbeGpuLease Lease);

    #region Fallback scheduling
    /// <summary>Grants frame-limited CPU ray credit independently of how often completions are polled.</summary>
    public void BeginFrame(int frameIndex)
    {
        fallbackFrame = frameIndex;
        fallback?.BeginFrame(frameIndex);
    }

    /// <summary>Distinguishes geometry fallback from missing lighting or otherwise unresolved GPU traversal.</summary>
    private bool NeedsFallback(int first, int count)
    {
        for (int index = first; index < first + count; index++)
            if (answers[index].RequiresCpuFallback) return true;
        return false;
    }

    /// <summary>Copies only this admission's retained answers into the bounded worker queue.</summary>
    private bool TryQueueFallback(in Admission admission, int first)
    {
        fallback ??= new WorldProbeCpuFallbackService(fallbackScene, isCurrent);
        fallback.BeginFrame(fallbackFrame);
        if (!fallback.HasCapacity(admission.Directions.Length)) return false;
        var retained = ImmutableArray.CreateBuilder<WorldProbeTraceAnswerGpu>(admission.Directions.Length);
        for (int index = 0; index < admission.Directions.Length; index++) retained.Add(answers[first + index]);
        long id = ++nextFallbackId;
        var work = new WorldProbeCpuFallbackWork(id, admission.Item, admission.Directions, retained.MoveToImmutable());
        if (!fallback.TryEnqueue(work)) return false;
        fallbackPending.Add(id, new(submittedScene, submittedInvalidation, submittedLightingRevision, CreateLease(admission, first)));
        return true;
    }

    /// <summary>Revalidates the original geometry/cache lifetime and scheduler ticket after CPU collision completes.</summary>
    private bool TryReadFallback(out LumOnWorldProbeTraceResult result)
    {
        result = default;
        if (fallback == null || !fallback.TryDequeue(out var completion)) return false;
        var work = completion.Work;
        result = WorldProbeGpuIntegration.Reject(work.Item);
        if (!fallbackPending.Remove(work.Id, out var lifetime)) return true;
        try
        {
            var current = readGeometry();
            if (!completion.Success || !isCurrent(work.Item.Request) ||
                !ReferenceEquals(current, lifetime.Scene) || (current?.InvalidationRevision ?? -1) != lifetime.Invalidation ||
                (readLighting()?.DependencyRevision ?? -1) != lifetime.LightingRevision || work.Item.SurfaceRevision != lifetime.LightingRevision)
                return true;
            result = WorldProbeGpuIntegration.Integrate(work.Item, work.Answers, 0, work.Answers.Length, lifetime.Lease);
        }
        catch (Exception error)
        {
            // Returning the failed admission releases its scheduler ticket even if providers fail.
            api.Logger.Error("[VGE] World-probe CPU fallback completion failed: {0}", error.Message);
        }
        finally { if (!result.Success) lifetime.Lease.Dispose(); }
        return true;
    }
    #endregion
}
