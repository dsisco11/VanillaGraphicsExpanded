using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Runs bounded L0 compute admissions on the render thread using existing uploaded resources.</summary>
internal sealed partial class LumOnWorldProbeGpuTraceBackend : IWorldProbeTraceBackend
{
    private readonly ICoreAPI api;
    private readonly Func<TraceGeometryGpuScene?> readGeometry;
    private readonly Func<SurfaceLightingSnapshot?> readLighting;
    private readonly System.Func<LumOnWorldProbeUpdateRequest, int, bool> tryClaim;
    private readonly int worldHeight;
    private readonly List<Admission> queued = new();
    private readonly List<Admission> pending = new();
    private readonly Queue<LumOnWorldProbeTraceResult> completed = new();
    private ImmutableArray<WorldProbeTraceAnswerGpu> answers;
    private int nextAdmission, nextRay;
    private int queuedRays;
    private WorldProbeTraceBatch? batch;
    private WorldProbeResidentAnswers? resident;
    private readonly List<WorldProbeResidentAnswers> residentBatches = new();
    internal const int MaximumResidentRays = WorldProbeTraceBatch.MaximumRays << 1;
    private TraceGeometryGpuScene? submittedScene;
    private long submittedInvalidation, submittedLightingRevision;
    private bool disposed;

    /// <summary>Owns one immutable admission and its ordered nearby/atlas direction selectors.</summary>
    private readonly record struct Admission(LumOnWorldProbeTraceWorkItem Item, WorldProbeTraceProbeGpu Probe, ImmutableArray<uint> Directions);

    #region Lifecycle
    /// <summary>Borrows render-thread providers and captures an owning scheduler's claim function.</summary>
    public LumOnWorldProbeGpuTraceBackend(ICoreAPI api, int worldHeight,
        Func<TraceGeometryGpuScene?> readGeometry, Func<SurfaceLightingSnapshot?> readLighting,
        System.Func<LumOnWorldProbeUpdateRequest, int, bool> tryClaim,
        IWorldProbeTraceScene fallbackScene, System.Func<LumOnWorldProbeUpdateRequest, bool> isCurrent)
    {
        this.api = api; this.worldHeight = worldHeight;
        this.readGeometry = readGeometry; this.readLighting = readLighting; this.tryClaim = tryClaim;
        this.fallbackScene = fallbackScene; this.isCurrent = isCurrent;
    }

    /// <summary>Retires fences and bounded queues; the owner revokes scheduler tickets before replacement.</summary>
    public void Dispose()
    {
        disposed = true;
        fallback?.Dispose(); fallback = null; fallbackPending.Clear();
        batch?.Dispose(); batch = null;
        foreach (var allocation in residentBatches) allocation.Dispose();
        residentBatches.Clear(); resident = null;
        queued.Clear(); pending.Clear(); completed.Clear(); queuedRays = 0; submittedScene = null;
        answers = default;
    }
    #endregion

    #region Admission and completion
    /// <summary>Admits at most one batch of queued rays in addition to one submitted/completed batch.</summary>
    public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
    {
        if (disposed || item.Request.Level != 0) return false;
        int size = Math.Max(1, item.WorldProbeOctahedralTileSize);
        int upperBound = Math.Clamp(item.WorldProbeAtlasTexelsPerUpdate, 1, checked(size * size)) +
            (WorldProbeTraceDirectionSelection.NeedsNearby(item) ? 1 : 0);
        if (upperBound > WorldProbeTraceBatch.MaximumRays - queuedRays) return false;
        var directions = WorldProbeGpuIntegration.CreateDirections(item);
        // Validate and split the world origin before claiming a scheduler ticket.
        var probe = new WorldProbeTraceProbeGpu(item.ProbePosWorld, item.MaxTraceDistanceWorld, worldHeight,
            item.WorldProbeOctahedralTileSize, 0, directions.Length, item.NearbySolidHitDistance);
        queued.Add(new(item, probe, directions)); queuedRays += directions.Length;
        return true;
    }

    /// <summary>Polls GPU completion, validates borrowed resource lifetimes, and integrates within the shared drain budget.</summary>
    public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)
    {
        result = default;
        if (disposed) return false;
        if (completed.TryDequeue(out result)) return true;
        if (TryReadFallback(out result)) return true;
        if (batch?.Pending == true)
        {
            try
            {
                if (!batch.TryReadResident(out answers, out resident)) return false;
                residentBatches.Add(resident!);
            }
            catch (Exception error)
            {
                FailPending(error);
                return completed.TryDequeue(out result);
            }
        }
        while (!answers.IsDefaultOrEmpty)
        {
            // Validate each drain, including answers retained across frame/upload budgets.
            // Metadata integration consumes only one admission under the caller's drain limit.
            var admission = pending[nextAdmission];
            bool deferred = false;
            try
            {
                var current = readGeometry();
                bool currentLifetime = ReferenceEquals(current, submittedScene) &&
                    (current?.InvalidationRevision ?? -1) == submittedInvalidation &&
                    (readLighting()?.DependencyRevision ?? -1) == submittedLightingRevision &&
                    admission.Item.SurfaceRevision == submittedLightingRevision && isCurrent(admission.Item.Request);
                if (currentLifetime && NeedsFallback(nextRay, admission.Directions.Length))
                {
                    // Keep the readback admission intact when the fallback queue is full.
                    // No ticket is completed and no successful GPU direction is retraced.
                    if (!TryQueueFallback(admission, nextRay)) return false;
                    deferred = true;
                }
                else if (currentLifetime)
                {
                    var lease = CreateLease(admission, nextRay);
                    try { result = WorldProbeGpuIntegration.Integrate(admission.Item, answers, nextRay, admission.Directions.Length, lease); }
                    catch { lease.Dispose(); throw; }
                    if (!result.Success) lease.Dispose();
                }
                else result = WorldProbeGpuIntegration.Reject(admission.Item);
            }
            catch (Exception error)
            {
                FailPending(error);
                return completed.TryDequeue(out result);
            }
            nextAdmission++;
            nextRay += admission.Directions.Length;
            if (nextAdmission == pending.Count)
            { pending.Clear(); submittedScene = null; answers = default; nextAdmission = nextRay = 0; resident?.Release(); resident = null; }
            if (!deferred) return true;
        }
        SubmitQueued();
        return completed.TryDequeue(out result);
    }

    /// <summary>Claims original tickets only when dispatch starts and submits one exact bounded ray range.</summary>
    private void SubmitQueued()
    {
        if (queued.Count == 0) return;
        residentBatches.RemoveAll(allocation => !allocation.IsValid);
        int retained = 0;
        foreach (var allocation in residentBatches) retained += allocation.Count;
        // Never overwrite allocations referenced by fallback workers or delayed cache completion.
        if (queuedRays > MaximumResidentRays - retained) return;
        var directions = ImmutableArray.CreateBuilder<uint>(queuedRays);
        var probes = ImmutableArray.CreateBuilder<WorldProbeTraceProbeGpu>(queued.Count);
        foreach (var admission in queued)
        {
            if (!tryClaim(admission.Item.Request, admission.Item.FrameIndex)) continue;
            pending.Add(admission);
            var probe = admission.Probe;
            probe.FirstDirection = (uint)directions.Count;
            probes.Add(probe);
            directions.AddRange(admission.Directions);
        }
        queued.Clear(); queuedRays = 0;
        if (pending.Count == 0) return;
        try
        {
            submittedScene = readGeometry();
            submittedInvalidation = submittedScene?.InvalidationRevision ?? -1;
            var lighting = readLighting();
            submittedLightingRevision = lighting?.DependencyRevision ?? -1;
            batch ??= new WorldProbeTraceBatch(api);
            batch.Submit(submittedScene, lighting, probes.ToImmutable().AsSpan(), directions.ToImmutable().AsSpan());
        }
        catch (Exception error)
        {
            FailPending(error);
        }
    }

    /// <summary>Turns failed submissions/readbacks into explicit failed completions so claimed work can retry.</summary>
    private void FailPending(Exception error)
    {
        api.Logger.Error("[VGE] World-probe GPU trace failed: {0}", error.Message);
        for (int index = nextAdmission; index < pending.Count; index++)
            completed.Enqueue(WorldProbeGpuIntegration.Reject(pending[index].Item));
        pending.Clear(); submittedScene = null; answers = default; nextAdmission = nextRay = 0;
        resident?.Release(); resident = null;
        batch?.Dispose(); batch = null;
    }

    /// <summary>Captures the original admission and borrowed resource revisions for commit-time validation.</summary>
    private WorldProbeGpuLease CreateLease(in Admission admission, int first)
    {
        var item = admission.Item;
        var scene = submittedScene;
        long invalidation = submittedInvalidation, revision = submittedLightingRevision;
        return new WorldProbeGpuLease(resident!, first, admission.Directions.Length, () =>
            !disposed && isCurrent(item.Request) && ReferenceEquals(readGeometry(), scene) &&
            (scene?.InvalidationRevision ?? -1) == invalidation &&
            (readLighting()?.DependencyRevision ?? -1) == revision && item.SurfaceRevision == revision);
    }
    #endregion
}
