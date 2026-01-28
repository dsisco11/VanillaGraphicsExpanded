using System;
using System.Numerics;
using System.Threading;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeTraceServiceTests
{
    [Fact]
    public void Service_ProducesResults_ForEnqueuedWork()
    {
        var scene = new NeverHitScene();

        using var svc = new LumOnWorldProbeTraceService(
            scene,
            maxQueuedWorkItems: 8,
            tryClaim: (_, _) => true);

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        Assert.True(svc.TryEnqueue(new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 0,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 8,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 8,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f)));

        LumOnWorldProbeTraceResult res = default;
        bool got = SpinWait.SpinUntil(() => svc.TryDequeueResult(out res), TimeSpan.FromSeconds(1));

        Assert.True(got);
        Assert.Equal(0, res.FrameIndex);
        Assert.True(res.Success);
    }

    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return WorldProbeTraceOutcome.Miss;
        }
    }
}
