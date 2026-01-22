using System;
using System.Numerics;
using System.Threading;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeTraceIntegratorTests
{
    [Fact]
    public void TraceProbe_WhenNoHits_ProducesNonZeroSkySh_AndAoConfidenceOne()
    {
        var scene = new NeverHitScene();
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 1,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        Assert.True(res.ShortRangeAoConfidence > 0.99f);
        Assert.True(res.ShR.Length() > 0.001f);
        Assert.True(res.ShG.Length() > 0.001f);
        Assert.True(res.ShB.Length() > 0.001f);
        Assert.Equal(0f, res.MeanLogHitDistance, 6);
    }

    [Fact]
    public void TraceProbe_WhenAllHits_ProducesZeroSh_AndAoConfidenceZero()
    {
        var scene = new AlwaysHitScene(hitDistance: 4.0);
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 2,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        Assert.True(res.ShortRangeAoConfidence < 0.01f);
        Assert.True(res.ShR.Length() < 1e-3f);
        Assert.True(res.ShG.Length() < 1e-3f);
        Assert.True(res.ShB.Length() < 1e-3f);
        Assert.True(res.MeanLogHitDistance > 1.0f);
    }

    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        public bool Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return false;
        }
    }

    private sealed class AlwaysHitScene : IWorldProbeTraceScene
    {
        private readonly double hitDistance;

        public AlwaysHitScene(double hitDistance)
        {
            this.hitDistance = hitDistance;
        }

        public bool Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = new LumOnWorldProbeTraceHit(
                HitDistance: this.hitDistance,
                HitBlockPos: new VectorInt3(0, 0, 0),
                HitFaceNormal: new VectorInt3(0, 1, 0),
                SampleBlockPos: new VectorInt3(0, 0, 0),
                SampleLightRgbS: new Vector4(0, 0, 0, 0));
            return true;
        }
    }
}
