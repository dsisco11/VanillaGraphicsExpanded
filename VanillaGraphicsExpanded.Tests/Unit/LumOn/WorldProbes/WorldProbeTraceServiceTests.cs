using System;
using System.Numerics;
using System.Threading;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeTraceServiceTests
{
    [Fact]
    public void Service_ProducesResults_ForEnqueuedWork()
    {
        var scene = new NeverHitScene();

        using var svc = new LumOnWorldProbeTraceService(scene, maxQueuedWorkItems: 8);

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        Assert.True(svc.TryEnqueue(new LumOnWorldProbeTraceWorkItem(0, request, new Vec3d(0.5, 0.5, 0.5), 8)));

        LumOnWorldProbeTraceResult res = default;
        bool got = SpinWait.SpinUntil(() => svc.TryDequeueResult(out res), TimeSpan.FromSeconds(1));

        Assert.True(got);
        Assert.Equal(0, res.FrameIndex);
    }

    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        public bool Trace(Vec3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return false;
        }
    }
}
