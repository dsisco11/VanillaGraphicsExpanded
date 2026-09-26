using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Publishes bounded delayed estimates only while their original dependencies remain current.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private GpuShaderStorageBuffer? fallbackCommitBuffer;
    private readonly SurfaceFallbackCommit[] fallbackCommitUpload = new SurfaceFallbackCommit[SurfaceFallbackWorker.MaximumTexels];
    private readonly List<LumonSceneRelightWorkGpu> fallbackWork = new();
    // At most one completed CPU batch plus one retained hit batch waits for publication credit.
    private readonly List<(SurfaceFallbackCommit Commit, SurfaceFallbackPage Origin, SurfaceFallbackLifetime Lifetime, bool Cpu)> pendingCommits = new(32);
    private SurfaceFallbackCommitDependencies? pendingCommitDependencies;
    private readonly BlockPos pendingDependencyPosition = new();

    #region Dependency validation
    /// <summary>Revalidates the shared batch once before spending publication credit, without copying dependencies or reading GPU data.</summary>
    private bool PendingFallbackDependenciesCurrent(TraceGeometryGpuScene current)
    {
        var dependencies = pendingCommitDependencies;
        if (dependencies == null || !ReferenceEquals(dependencies.Accessor, capi.World.BlockAccessor)) return false;
        // These include the original source pages and ready hit pages bound before the lighting query.
        foreach (var page in dependencies.Pages)
            if (!HitOriginCurrent(current, dependencies.Lifetime, page)) return false;
        try
        {
            foreach (var dependency in dependencies.Chunks)
            {
                var chunk = dependency.Chunk;
                pendingDependencyPosition.X = chunk.X << 5;
                pendingDependencyPosition.Y = chunk.Y << 5;
                pendingDependencyPosition.Z = chunk.Z << 5;
                if (!ReferenceEquals(dependency.Identity, dependencies.Accessor.GetChunkAtBlockPos(pendingDependencyPosition))) return false;
            }
        }
        catch
        {
            // Match pre-query validation: failed terrain access rejects delayed work, preserving displayed lighting.
            return false;
        }
        return true;
    }
    #endregion

    #region Validated GPU commit
    /// <summary>Reserves normal page publication credit for completed fallback texels and uses the ordinary combine/swap path.</summary>
    private int ApplyFallback(TraceGeometryGpuScene current, LumonScenePhysicalAtlasGpuResources resources,
        GpuShaderStorageBuffer workBuffer, int maxPages, int tile)
    {
        if (pendingCommits.Count == 0)
        {
            // Stop draining completed producers while publication is backlogged; never discard excess credits.
            var fallback = PollFallback(current, out var dependencies);
            pendingCommitDependencies = dependencies;
            var retained = PollHitRetries(current);
            var lifetime = new SurfaceFallbackLifetime(current, current.InvalidationRevision, dependencyRevision,
                Interlocked.Read(ref fallbackWorldRevision));
            foreach (var commit in retained)
                pendingCommits.Add((commit, new(commit.Page, identities[commit.Page], pageGenerations[commit.Page],
                    feedback.GetCaptureRevision(commit.Page)), lifetime, false));
            foreach (var commit in fallback)
                pendingCommits.Add((commit, new(commit.Page, identities[commit.Page], pageGenerations[commit.Page],
                    feedback.GetCaptureRevision(commit.Page)), lifetime, true));
        }
        if (pendingCommits.Count == 0) return 0;
        fallbackWork.Clear(); Array.Clear(fallbackCommitUpload);
        var pages = new HashSet<uint>();
        int accepted = 0, cpuAccepted = 0;
        // Every queued CPU texel shares one batch snapshot; validate it once per draining call.
        bool cpuCurrent = PendingFallbackDependenciesCurrent(current);
        for (int i = 0; i < pendingCommits.Count;)
        {
            var pending = pendingCommits[i];
            var commit = pending.Commit;
            if ((pending.Cpu && !cpuCurrent) || !HitOriginCurrent(current, pending.Lifetime, pending.Origin))
            { pendingCommits.RemoveAt(i); fallbackRejected++; continue; }
            if (accepted == SurfaceFallbackWorker.MaximumTexels || (!pages.Contains(commit.Page) && pages.Count >= maxPages))
            { i++; continue; }
            if (pages.Add(commit.Page)) fallbackWork.Add(new(commit.Page,commit.Slot,0,commit.Patch));
            fallbackCommitUpload[accepted++] = commit;
            if (pending.Cpu) cpuAccepted++;
            pendingCommits.RemoveAt(i);
        }
        if (pendingCommits.Count == 0) pendingCommitDependencies = null;
        if (accepted == 0) return 0;
        fallbackCommitBuffer ??= GpuShaderStorageBuffer.Create(debugName:"SurfaceLighting.FallbackCommits");
        fallbackCommitBuffer.EnsureCapacity(SurfaceFallbackWorker.MaximumTexels << 5,growExponentially:false);
        fallbackCommitBuffer.UploadSubData<SurfaceFallbackCommit>(fallbackCommitUpload,0,SurfaceFallbackWorker.MaximumTexels << 5);
        var work = CollectionsMarshal.AsSpan(fallbackWork);
        workBuffer.EnsureCapacity(work.Length << 4,growExponentially:false);
        workBuffer.UploadSubData(work,0,work.Length << 4);
        var cfg = config.LumOn.LumonScene;
        dispatch!.Run(current,snapshot,resources.PendingOutgoing,workBuffer,work.Length,5,(uint)(tile*tile),1,0,
            (uint)frame,cfg.SurfaceLightingMaterialEmission,cfg.RelightMaxFramesAccumulated,fallbackCommits:fallbackCommitBuffer);
        publishable.AddRange(fallbackWork);
        fallbackCommitted += cpuAccepted;
        return pages.Count;
    }
    #endregion
}
