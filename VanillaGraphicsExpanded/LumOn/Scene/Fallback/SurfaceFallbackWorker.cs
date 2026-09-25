using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Noise;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Owns one bounded collision-only batch, retaining cancellation credit until its worker actually exits.</summary>
internal sealed class SurfaceFallbackWorker : IDisposable
{
    public const int MaximumTexels = 16, MaximumRays = 64, RaysPerFrame = 64, MaximumDependencies = 512;
    private readonly Func<Action<VectorInt3, object?>, IWorldProbeTraceScene> createScene;
    private readonly object gate = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private CancellationTokenSource? cancellation;
    private Task<SurfaceFallbackResult?>? task;
    private int credit, lastFrame;
    private bool hasFrame, disposed;
    public bool Busy => task != null;

    #region Lifetime and admission
    /// <summary>Injects geometry access only; worker code cannot access cache textures or block lighting.</summary>
    public SurfaceFallbackWorker(Func<Action<VectorInt3, object?>, IWorldProbeTraceScene> createScene) => this.createScene = createScene;

    /// <summary>Replenishes bounded starts once per render frame without accumulating unused credit.</summary>
    public void BeginFrame(int frame)
    {
        lock (gate)
        {
            if (disposed || hasFrame && lastFrame == frame) return;
            hasFrame = true; lastFrame = frame; credit = RaysPerFrame;
            if (wake.CurrentCount == 0) wake.Release();
        }
    }

    /// <summary>Admits at most sixteen texels and 1024 retained rays across queued, running and undrained work.</summary>
    public bool TrySubmit(ImmutableArray<SurfaceFallbackRequest> requests)
    {
        if (requests.IsDefaultOrEmpty || requests.Length > MaximumTexels) throw new ArgumentOutOfRangeException(nameof(requests));
        foreach (var request in requests)
            if (request.Fraction.W < 1 || request.Fraction.W > MaximumRays || !float.IsFinite(request.Fraction.W))
                throw new ArgumentOutOfRangeException(nameof(requests));
        if (disposed || task != null) return false;
        cancellation = new();
        var token = cancellation.Token;
        task = Task.Run(() => Trace(requests, token));
        return true;
    }

    /// <summary>Releases retained worker credit only after completion; cancellation produces no estimates.</summary>
    public bool TryRead(out SurfaceFallbackResult? result)
    {
        result = null;
        if (task == null || !task.IsCompleted) return false;
        if (task.IsCompletedSuccessfully) result = task.Result;
        else _ = task.Exception;
        task = null; cancellation?.Dispose(); cancellation = null;
        return true;
    }

    /// <summary>Cancels obsolete work without allowing a replacement worker to overlap its terrain access.</summary>
    public void Cancel() => cancellation?.Cancel();

    /// <summary>Defers synchronization disposal until any terrain access has returned.</summary>
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        Cancel();
        var pending = task;
        if (pending == null) { cancellation?.Dispose(); wake.Dispose(); }
        else _ = pending.ContinueWith(_ => { cancellation?.Dispose(); wake.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    #endregion

    #region Collision work
    /// <summary>Waits asynchronously for frame credit instead of blocking or spinning on the render thread.</summary>
    private async Task TakeCredit(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (gate) { if (credit > 0) { credit--; return; } }
            await wake.WaitAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>Retraces full original batches so unsupported shapes cannot hide an earlier collision.</summary>
    private async Task<SurfaceFallbackResult?> Trace(ImmutableArray<SurfaceFallbackRequest> requests, CancellationToken token)
    {
        var dependencies = new Dictionary<VectorInt3, object?>();
        // A changed loaded identity inside one batch is an incoherent source, not a usable partial result.
        var scene = createScene((coordinate, identity) =>
        {
            if (dependencies.TryGetValue(coordinate, out var previous))
            { if (!ReferenceEquals(previous, identity)) throw new InvalidOperationException("Chunk replaced during fallback."); }
            else
            {
                if (dependencies.Count >= MaximumDependencies) throw new InvalidOperationException("Fallback dependency budget exhausted.");
                dependencies.Add(coordinate, identity);
            }
        });
        var queries = ImmutableArray.CreateBuilder<SurfaceLightingQuery>();
        var texels = ImmutableArray.CreateBuilder<SurfaceFallbackTexel>(requests.Length);
        try
        {
            foreach (var request in requests)
            {
                int first = queries.Count;
                bool complete = true;
                var origin = new Vector3d(request.X + (double)request.Fraction.X, request.Y + (double)request.Fraction.Y,
                    request.Z + (double)request.Fraction.Z);
                for (uint ray = 0; ray < (uint)request.Fraction.W; ray++)
                {
                    await TakeCredit(token).ConfigureAwait(false);
                    Vector3 direction = Direction(request, ray);
                    var outcome = scene.Trace(origin, direction, 512, token, out var hit);
                    if (outcome == WorldProbeTraceOutcome.Sky) continue;
                    if (outcome != WorldProbeTraceOutcome.Hit) { complete = false; break; }
                    var point = origin + Vector3d.Normalize(Vector3d.FromVector3(direction)) * hit.HitDistance;
                    queries.Add(new(hit.HitBlockPos, hit.HitFaceNormal,
                        new((float)(point.X-hit.HitBlockPos.X), (float)(point.Y-hit.HitBlockPos.Y), (float)(point.Z-hit.HitBlockPos.Z)), hit.HitBlockId));
                }
                texels.Add(new(request, first, queries.Count-first, complete));
            }
            token.ThrowIfCancellationRequested();
            var observed = ImmutableArray.CreateBuilder<SurfaceFallbackDependency>(dependencies.Count);
            foreach (var pair in dependencies) observed.Add(new(pair.Key, pair.Value));
            return new(texels.MoveToImmutable(), queries.ToImmutable(), observed.MoveToImmutable());
        }
        catch { return null; }
    }

    /// <summary>Reconstructs the producer's cosine distribution from its original seed and local normal.</summary>
    internal static Vector3 Direction(in SurfaceFallbackRequest request, uint ray)
    {
        var normal = new Vector3(request.Normal.X, request.Normal.Y, request.Normal.Z);
        var tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Z)<.999f ? Vector3.UnitZ : Vector3.UnitY, normal));
        var bitangent = Vector3.Cross(normal,tangent);
        float u = Math.Clamp(Squirrel3Noise.Noise01(request.Seed,ray,0u),.000001f,.999999f);
        float phi = 6.28318530718f * Squirrel3Noise.Noise01(request.Seed,ray,1u);
        return tangent*(MathF.Sqrt(u)*MathF.Cos(phi)) + bitangent*(MathF.Sqrt(u)*MathF.Sin(phi)) + normal*MathF.Sqrt(1-u);
    }
    #endregion
}
