using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Selects compact GPU direction indices and integrates answers with the CPU metadata policy.</summary>
internal static class WorldProbeGpuIntegration
{
    #region Direction admission
    /// <summary>Preserves the shared selection as compact texel indices, with an optional high-bit cardinal selector.</summary>
    public static ImmutableArray<uint> CreateDirections(in LumOnWorldProbeTraceWorkItem item)
    {
        int size = Math.Max(1, item.WorldProbeOctahedralTileSize);
        int count = Math.Clamp(item.WorldProbeAtlasTexelsPerUpdate, 1, checked(size * size));
        if (count >= WorldProbeTraceBatch.MaximumRays) throw new ArgumentOutOfRangeException(nameof(item));
        Span<int> indices = count <= 256 ? stackalloc int[count] : new int[count];
        int selected = WorldProbeTraceDirectionSelection.Fill(item, indices);
        bool nearby = WorldProbeTraceDirectionSelection.NeedsNearby(item);
        var directions = ImmutableArray.CreateBuilder<uint>(selected + (nearby ? 1 : 0));
        if (nearby)
            directions.Add(0x80000000u | (uint)WorldProbeTraceDirectionSelection.NearbyIndex(item));
        for (int index = 0; index < selected; index++) directions.Add((uint)indices[index]);
        return directions.MoveToImmutable();
    }
    #endregion
    #region Integration
    /// <summary>Replays completed geometry into the common integrator, then attaches exact GPU lighting answers.</summary>
    public static LumOnWorldProbeTraceResult Integrate(in LumOnWorldProbeTraceWorkItem item,
        ImmutableArray<WorldProbeTraceAnswerGpu> answers, int offset, int count)
    {
        // Replaying answers performs no collision or lighting queries. It shares the primary
        // completion gate, distance encoding, AO, confidence and importance calculations.
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(new CompletedGeometry(answers, offset, count),
            item with { DeferSurfaceLighting = true }, CancellationToken.None);
        if (!result.Success) return result;
        var samples = result.AtlasSamples.ToBuilder();
        int first = offset + (WorldProbeTraceDirectionSelection.NeedsNearby(item) ? 1 : 0);
        for (int index = 0; index < samples.Count; index++)
        {
            var ray = answers[first + index];
            if (ray.TraceOutcome != WorldProbeTraceOutcome.Hit) continue;
            var light = ray.Hit.Result;
            bool ready = light.W == 1 && float.IsFinite(light.X) && float.IsFinite(light.Y) && float.IsFinite(light.Z);
            samples[index] = samples[index] with
            {
                RadianceRgb = ready ? Vector3.Max(Vector3.Zero, new(light.X, light.Y, light.Z)) : Vector3.Zero,
                SurfaceHit = ready ? null : ray.Hit,
            };
        }
        return result with { AtlasSamples = samples.ToImmutable() };
    }

    /// <summary>Rejects obsolete dispatches without manufacturing valid darkness or losing the admission identity.</summary>
    public static LumOnWorldProbeTraceResult Reject(in LumOnWorldProbeTraceWorkItem item) => new(
        item.FrameIndex, item.Request, false, WorldProbeTraceFailureReason.Aborted,
        ImmutableArray<LumOnWorldProbeAtlasSample>.Empty, 0, Vector3.UnitY, 0, 0, 0,
        LumOnWorldProbeImportanceFlags.None, item.SurfaceRevision);
    #endregion

    #region Completed geometry adapter
    /// <summary>Feeds the common integrator only the ordered answers belonging to one submitted admission.</summary>
    private sealed class CompletedGeometry : IWorldProbeTraceScene
    {
        private readonly ImmutableArray<WorldProbeTraceAnswerGpu> answers;
        private readonly int end;
        private int next;

        /// <summary>Limits the replay to one admission's ray range.</summary>
        public CompletedGeometry(ImmutableArray<WorldProbeTraceAnswerGpu> answers, int offset, int count)
        { this.answers = answers; next = offset; end = checked(offset + count); }

        /// <summary>Returns a GPU geometry answer without touching terrain or sampling legacy lighting.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance,
            CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (next >= end) throw new InvalidOperationException("Probe integration exceeded its GPU ray admission.");
            var ray = answers[next++];
            var query = ray.Hit;
            var normal = new VectorInt3(query.NormalX, query.NormalY, query.NormalZ);
            var cell = new VectorInt3(query.X, query.Y, query.Z);
            hit = new(query.Fraction.W, query.BlockId, ProbeHitFaceUtil.FromAxisNormal(normal), cell, normal, cell + normal, default);
            return ray.TraceOutcome;
        }
    }
    #endregion
}
