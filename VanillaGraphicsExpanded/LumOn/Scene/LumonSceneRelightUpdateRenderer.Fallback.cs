using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Routes bounded geometry-only CPU fallback back through validated cache queries and normal publication.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private GpuQueue<SurfaceFallbackRequest>? fallbackQueue;
    private SurfaceFallbackWorker? fallbackWorker;
    private SurfaceLightingQueryBatch? fallbackQueries;
    private SurfaceFallbackResult? fallbackResult;
    private SurfaceFallbackLifetime? fallbackLifetime;
    private ImmutableArray<SurfaceFallbackPage> fallbackPages = ImmutableArray<SurfaceFallbackPage>.Empty;
    private IBlockAccessor? fallbackAccessor;
    private long fallbackWorldRevision;
    private int fallbackFrame;
    private uint fallbackSelection;
    private long fallbackAdmitted, fallbackCommitted, fallbackRejected;

    #region Lifetime checks
    /// <summary>Invalidates full CPU segments even when an edited chunk lies outside the current GPU domain.</summary>
    private void OnFallbackChunkDirty(Vec3i coordinate, IWorldChunk chunk, EnumChunkDirtyReason reason) =>
        Interlocked.Increment(ref fallbackWorldRevision);

    /// <summary>Checks ownership without treating ordinary lighting generation swaps as surface-identity changes.</summary>
    private bool FallbackCurrent(TraceGeometryGpuScene current)
    {
        var lifetime = fallbackLifetime;
        if (lifetime == null || !ReferenceEquals(lifetime.Scene,current) || lifetime.Invalidation != current.InvalidationRevision ||
            lifetime.Lighting != dependencyRevision || lifetime.World != Interlocked.Read(ref fallbackWorldRevision)) return false;
        foreach (var page in fallbackPages)
            if (!identities.TryGetValue(page.Page,out var key) || key != page.Key ||
                !pageGenerations.TryGetValue(page.Page,out var generation) || generation != page.Generation ||
                feedback.GetCaptureRevision(page.Page) != page.CaptureRevision || !feedback.IsCaptureAvailable(page.Page) ||
                !publishedPages.Contains(page.Page)) return false;
        return true;
    }

    /// <summary>Rejects chunk unload/reload even when the engine emitted no edit notification.</summary>
    private bool FallbackDependenciesCurrent()
    {
        if (fallbackResult == null || fallbackAccessor == null) return false;
        foreach (var dependency in fallbackResult.Dependencies)
        {
            var c = dependency.Chunk;
            if (!ReferenceEquals(dependency.Identity,fallbackAccessor.GetChunkAtBlockPos(new BlockPos(c.X << 5,c.Y << 5,c.Z << 5)))) return false;
        }
        return true;
    }

    /// <summary>Retains hit-page ownership as well as source-page ownership before asynchronous cache lookup.</summary>
    private void CaptureFallbackHitPages(ImmutableArray<SurfaceLightingQuery> queries)
    {
        if (!feedback.TryGetNearDispatchState(out _,out _,out _,out var mirror)) return;
        var pages = fallbackPages.ToBuilder();
        var seen = fallbackPages.Select(page=>page.Page).ToHashSet();
        foreach (var query in queries)
        {
            if (!SurfaceFallbackHitAddress.TryResolve(query,out var chunk,out uint patch) ||
                !feedback.TryGetNearChunkSlotAndGeneration(chunk,out uint slot,out ushort generation)) continue;
            var entry = mirror[checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + (int)patch)];
            uint page = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
            // Missing hit lighting rejects that texel in the query result, not other already resolved texels.
            if (page == 0 || !publishedPages.Contains(page) || !feedback.IsCaptureAvailable(page) || !seen.Add(page)) continue;
            pages.Add(new(page,LumonSceneVirtualPageKeyUtil.Pack(slot,patch),generation,feedback.GetCaptureRevision(page)));
        }
        fallbackPages = pages.ToImmutable();
    }

    /// <summary>Cancels work but retains its worker admission until terrain access acknowledges cancellation.</summary>
    private void ResetFallback()
    {
        fallbackWorker?.Cancel(); pendingCommits.Clear(); pendingCommitDependencies = null;
        fallbackQueue?.Dispose(); fallbackQueue = null;
        fallbackQueries?.Dispose(); fallbackQueries = null;
        fallbackLifetime = null; fallbackResult = null; fallbackPages = ImmutableArray<SurfaceFallbackPage>.Empty;
    }
    #endregion

    #region Capture and asynchronous completion
    /// <summary>Admits one GPU collection only while all previous GPU/CPU/query ownership has retired.</summary>
    private GpuShaderStorageBuffer? BeginFallbackCapture(TraceGeometryGpuScene current, ReadOnlySpan<LumonSceneRelightWorkGpu> work)
    {
        if (fallbackLifetime != null || fallbackWorker?.Busy == true) return null;
        fallbackQueue ??= new(SurfaceFallbackWorker.MaximumTexels, 16, "SurfaceLighting.FallbackRequests");
        fallbackPages = work.ToArray().Select(item => new SurfaceFallbackPage(item.PhysicalPageId,identities[item.PhysicalPageId],
            pageGenerations[item.PhysicalPageId],feedback.GetCaptureRevision(item.PhysicalPageId))).ToImmutableArray();
        fallbackLifetime = new(current,current.InvalidationRevision,dependencyRevision,Interlocked.Read(ref fallbackWorldRevision));
        if (work.IsEmpty) throw new InvalidOperationException("Invalid fallback capture admission.");
        fallbackQueue.WriteHeader([0, SurfaceFallbackWorker.MaximumTexels, (uint)work.Length, fallbackSelection++]);
        return fallbackQueue.Buffer;
    }

    /// <summary>Polls without blocking, tracing only collision data on a worker and querying lighting on the render thread.</summary>
    private ImmutableArray<SurfaceFallbackCommit> PollFallback(TraceGeometryGpuScene current, out SurfaceFallbackCommitDependencies? dependencies)
    {
        dependencies = null;
        fallbackWorker?.BeginFrame(++fallbackFrame);
        try
        {
            if (fallbackLifetime == null)
            { fallbackWorker?.TryRead(out _); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
            if (!FallbackCurrent(current)) { fallbackRejected++; ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
            if (fallbackQueue?.Pending == true)
            {
                if (!fallbackQueue.TryRead<ImmutableArray<SurfaceFallbackRequest>>(static records => ImmutableArray.Create(records), out var requests)) return ImmutableArray<SurfaceFallbackCommit>.Empty;
                if (requests.IsEmpty) { fallbackLifetime = null; fallbackPages = ImmutableArray<SurfaceFallbackPage>.Empty; return ImmutableArray<SurfaceFallbackCommit>.Empty; }
                // Retain only pages that actually emitted work, avoiding unrelated delayed-page invalidations.
                fallbackPages = fallbackPages.Where(page => requests.Any(request => request.Page == page.Page)).ToImmutableArray();
                var accessor = capi.World.BlockAccessor;
                if (accessor == null) { ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
                if (!ReferenceEquals(accessor,fallbackAccessor))
                {
                    fallbackWorker?.Dispose(); fallbackAccessor = accessor;
                    fallbackWorker = new(observer => new BlockAccessorWorldProbeTraceScene(accessor,false,SurfaceFallbackWorker.MaximumTraversalSteps,observer));
                }
                fallbackWorker!.BeginFrame(fallbackFrame);
                if (!fallbackWorker.TrySubmit(requests)) { ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
                fallbackAdmitted += requests.Length;
            }
            if (fallbackWorker?.TryRead(out var result) == true)
            {
                fallbackResult = result;
                if (result == null || !FallbackCurrent(current) || !FallbackDependenciesCurrent())
                { fallbackRejected++; ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
                // CPU distance/budget failures share the GPU bucket delay; completed sibling texels still commit.
                int tile = snapshot.TileSize;
                int texels = Math.Min(tile * tile, config.LumOn.LumonScene.RelightTexelsPerPagePerFrame);
                int bucketCount = (tile * tile + texels - 1) / texels;
                foreach (var texel in result.Texels)
                    if (texel.Exhausted)
                        refreshSchedule.RecordExhaustion(texel.Request.Page, texel.Request.Linear % (uint)bucketCount, frame);
                if (!result.Queries.IsEmpty)
                {
                    CaptureFallbackHitPages(result.Queries);
                    if (!FallbackCurrent(current)) { fallbackRejected++; ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
                    fallbackQueries ??= new(capi);
                    fallbackQueries.Submit(current,snapshot,result.Queries.AsSpan());
                }
            }
            if (fallbackResult == null) return ImmutableArray<SurfaceFallbackCommit>.Empty;
            SurfaceLightingQuery[] answers = Array.Empty<SurfaceLightingQuery>();
            if (!fallbackResult.Queries.IsEmpty && !fallbackQueries!.TryRead(out answers)) return ImmutableArray<SurfaceFallbackCommit>.Empty;
            if (!FallbackCurrent(current) || !FallbackDependenciesCurrent())
            { fallbackRejected++; ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty; }
            var commits = SurfaceFallbackEstimates.Resolve(fallbackResult,answers);
            AdmitHitRetries(fallbackResult, answers, fallbackLifetime!, fallbackPages);
            if (!commits.IsEmpty) dependencies = new(fallbackLifetime!, fallbackPages, fallbackResult.Dependencies, fallbackAccessor!);
            fallbackResult = null; fallbackLifetime = null; fallbackPages = ImmutableArray<SurfaceFallbackPage>.Empty;
            return commits;
        }
        catch
        {
            // Resource/readback/terrain failures discard delayed work, never the last displayed lighting.
            fallbackRejected++; ResetFallback(); return ImmutableArray<SurfaceFallbackCommit>.Empty;
        }
    }
    #endregion

}
