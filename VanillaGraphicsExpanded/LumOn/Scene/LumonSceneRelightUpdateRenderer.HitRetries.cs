using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.HitLighting;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Retries retained geometric hits only when their missing captured lighting progresses.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private readonly SurfaceHitRetryCache hitRetries = new();
    private readonly Dictionary<uint, long> hitPublications = new();
    private GpuQueue<SurfaceHitCapture>? hitCapture;
    private SurfaceLightingQueryBatch? hitQueries;
    private SurfaceHitRetryEntry? hitActive;
    private SurfaceFallbackLifetime? hitCaptureLifetime;
    private ImmutableArray<SurfaceFallbackPage> hitCaptureOrigins = [];
    private uint hitSelection;
    private long hitPublicationSerial, hitRetained, hitQueried, hitCompleted, hitRejected;
    private long hitCpuRetained, hitCpuCompleted;

    #region Ownership and dependencies
    /// <summary>Rejects changed geometry, world/resource lifetimes and originating surface identity.</summary>
    private bool HitOriginCurrent(TraceGeometryGpuScene current, SurfaceFallbackLifetime lifetime, SurfaceFallbackPage origin) =>
        ReferenceEquals(lifetime.Scene, current) && lifetime.Invalidation == current.InvalidationRevision &&
        lifetime.Lighting == dependencyRevision && lifetime.World == Interlocked.Read(ref fallbackWorldRevision) &&
        identities.TryGetValue(origin.Page, out var key) && key == origin.Key &&
        pageGenerations.TryGetValue(origin.Page, out var generation) && generation == origin.Generation &&
        feedback.GetCaptureRevision(origin.Page) == origin.CaptureRevision && feedback.IsCaptureAvailable(origin.Page) &&
        publishedPages.Contains(origin.Page);

    /// <summary>Observes each exact hit page, allowing initially absent capture to become available.</summary>
    private ImmutableArray<SurfaceHitDependency> HitDependencies(ImmutableArray<SurfaceLightingQuery> queries)
    {
        var states = ImmutableArray.CreateBuilder<SurfaceHitDependency>(queries.Length);
        bool available = feedback.TryGetNearDispatchState(out _, out _, out _, out var mirror);
        foreach (var query in queries)
        {
            if (!available || !SurfaceFallbackHitAddress.TryResolve(query, out var chunk, out uint patch) ||
                !feedback.TryGetNearChunkSlotAndGeneration(chunk, out uint slot, out ushort generation))
            { states.Add(default); continue; }
            var value = mirror[checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + (int)patch)];
            uint page = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(value);
            if (page == 0) { states.Add(default); continue; }
            states.Add(new(page, LumonSceneVirtualPageKeyUtil.Pack(slot, patch), generation,
                feedback.GetCaptureRevision(page), feedback.IsCaptureAvailable(page), hitPublications.GetValueOrDefault(page)));
        }
        return states.MoveToImmutable();
    }

    /// <summary>Checks CPU segment chunk ownership only before GPU queries and commits, never per waiting entry per frame.</summary>
    private bool HitChunksCurrent(SurfaceHitRetryEntry entry)
    {
        if (entry.Chunks.IsEmpty) return true;
        var accessor = capi.World.BlockAccessor;
        foreach (var dependency in entry.Chunks)
        {
            var c = dependency.Chunk;
            if (!ReferenceEquals(dependency.Identity, accessor.GetChunkAtBlockPos(new BlockPos(c.X << 5, c.Y << 5, c.Z << 5)))) return false;
        }
        return true;
    }

    /// <summary>Retires all retry ownership at resource teardown without clearing displayed lighting itself.</summary>
    private void ResetHitRetries()
    {
        hitCapture?.Dispose(); hitCapture = null; hitQueries?.Dispose(); hitQueries = null;
        hitCaptureLifetime = null; hitCaptureOrigins = []; hitActive = null;
        hitRetries.Clear(); hitPublications.Clear();
    }
    #endregion

    #region Bounded capture and admission
    /// <summary>Binds stable suppression during pending readback, admitting new capture only when storage is idle.</summary>
    private GpuShaderStorageBuffer BeginHitCapture(TraceGeometryGpuScene current, ReadOnlySpan<LumonSceneRelightWorkGpu> work, out bool capture)
    {
        hitCapture ??= new(SurfaceHitCaptureCodec.MaximumCaptures, SurfaceHitCaptureCodec.HeaderBytes, "SurfaceLighting.HitRetries");
        capture = !hitCapture.Pending;
        if (!capture) return hitCapture.Buffer;
        hitCaptureOrigins = work.ToArray().Select(item => new SurfaceFallbackPage(item.PhysicalPageId, identities[item.PhysicalPageId],
            pageGenerations[item.PhysicalPageId], feedback.GetCaptureRevision(item.PhysicalPageId))).ToImmutableArray();
        hitCaptureLifetime = new(current, current.InvalidationRevision, dependencyRevision, Interlocked.Read(ref fallbackWorldRevision));
        Span<uint> header = stackalloc uint[SurfaceHitCaptureCodec.HeaderBytes >> 2];
        SurfaceHitCaptureCodec.EncodeHeader(header, work.Length, hitSelection++, hitRetries.Entries);
        hitCapture.WriteHeader(header);
        capture = hitRetries.Count < SurfaceHitRetryCache.Capacity;
        return hitCapture.Buffer;
    }

    /// <summary>Retains complete texel geometry with any missing hit result, shared by GPU and CPU tracing.</summary>
    private void AdmitHitRetries(SurfaceFallbackResult result, ReadOnlySpan<SurfaceLightingQuery> answers,
        SurfaceFallbackLifetime lifetime, ImmutableArray<SurfaceFallbackPage> origins)
    {
        foreach (var texel in result.Texels)
        {
            if (!texel.Complete || texel.QueryCount == 0 || hitRetries.Count == SurfaceHitRetryCache.Capacity) continue;
            var origin = origins.FirstOrDefault(page => page.Page == texel.Request.Page);
            if (origin.Page == 0) continue;
            var queries = ImmutableArray.Create(answers.Slice(texel.FirstQuery, texel.QueryCount));
            var entry = new SurfaceHitRetryEntry(texel.Request, queries, result.Dependencies, origin, lifetime, frame);
            if (entry.Complete(queries.AsSpan()) || !entry.Observe(HitDependencies(queries))) continue;
            if (hitRetries.TryAdd(entry))
            {
                hitRetained++;
                if (!entry.Chunks.IsEmpty) hitCpuRetained++;
            }
        }
    }

    /// <summary>Drains complete GPU geometry without reading or overwriting an unsignaled capture.</summary>
    private void CollectHitCapture(TraceGeometryGpuScene current)
    {
        if (hitCapture?.Pending != true || !hitCapture.TryRead<ImmutableArray<SurfaceFallbackResult>>(SurfaceHitCaptureCodec.Decode, out var captured)) return;
        foreach (var result in captured)
        {
            var origin = hitCaptureOrigins.FirstOrDefault(page => page.Page == result.Texels[0].Request.Page);
            if (hitCaptureLifetime == null || !HitOriginCurrent(current, hitCaptureLifetime, origin)) { hitRejected++; continue; }
            AdmitHitRetries(result, result.Queries.AsSpan(), hitCaptureLifetime, hitCaptureOrigins);
        }
        hitCaptureLifetime = null; hitCaptureOrigins = [];
    }
    #endregion

    #region Lighting completion
    /// <summary>Queries at most one retained batch per frame and commits only after revalidating both sides of its fence.</summary>
    private ImmutableArray<SurfaceFallbackCommit> PollHitRetries(TraceGeometryGpuScene current)
    {
        try
        {
            CollectHitCapture(current);
            int retainedCount = hitRetries.Count;
            hitRetries.Prune(frame, entry => HitOriginCurrent(current, entry.Lifetime, entry.Origin) && entry.Observe(HitDependencies(entry.Queries)));
            hitRejected += retainedCount - hitRetries.Count;
            if (hitActive != null && !hitRetries.Entries.Contains(hitActive))
            {
                hitQueries?.Dispose(); hitQueries = null; hitActive = null;
            }
            var commits = ImmutableArray<SurfaceFallbackCommit>.Empty;
            if (hitActive != null && hitQueries!.TryRead(out var answers))
            {
                var entry = hitActive; hitActive = null;
                if (!HitOriginCurrent(current, entry.Lifetime, entry.Origin) || !entry.Validate(HitDependencies(entry.Queries)) || !HitChunksCurrent(entry))
                { hitRetries.Remove(entry); hitRejected++; }
                else if (entry.Complete(answers))
                {
                    var result = new SurfaceFallbackResult([new(entry.Request, 0, entry.Queries.Length, true)], entry.Queries, entry.Chunks);
                    commits = SurfaceFallbackEstimates.Resolve(result, answers);
                    hitRetries.Remove(entry); hitCompleted += commits.Length;
                    if (!entry.Chunks.IsEmpty) hitCpuCompleted += commits.Length;
                }
            }
            if (hitActive == null && hitRetries.Select() is { } selected)
            {
                if (!HitChunksCurrent(selected)) { hitRetries.Remove(selected); hitRejected++; }
                else
                {
                    hitQueries ??= new(capi);
                    hitQueries.Submit(current, snapshot, selected.Queries.AsSpan());
                    selected.Submitted(); hitActive = selected; hitQueried++;
                }
            }
            return commits;
        }
        catch
        {
            // Fail closed for delayed work. Live lighting textures remain owned by normal publication.
            if (hitActive != null) hitRetries.Remove(hitActive);
            hitActive = null; hitQueries?.Dispose(); hitQueries = null;
            hitCapture?.Dispose(); hitCapture = null; hitCaptureLifetime = null; hitCaptureOrigins = [];
            hitRejected++;
            return [];
        }
    }
    #endregion
}
