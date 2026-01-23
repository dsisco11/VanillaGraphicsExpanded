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

    [Fact]
    public void TraceProbe_WhenAllHitsWithSkylight_ProducesNonZeroRadianceSh()
    {
        var scene = new AlwaysHitScene(hitDistance: 4.0, sampleLight: new Vector4(0, 0, 0, 0.9f), hitFaceNormal: new VectorInt3(0, 1, 0));
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 3,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        Assert.True(res.ShortRangeAoConfidence < 0.01f);
        Assert.True(res.ShR.Length() > 1e-5f);
        Assert.True(res.ShG.Length() > 1e-5f);
        Assert.True(res.ShB.Length() > 1e-5f);
    }

    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return WorldProbeTraceOutcome.Miss;
        }
    }

    private sealed class AlwaysHitScene : IWorldProbeTraceScene
    {
        private readonly double hitDistance;
        private readonly Vector4 sampleLight;
        private readonly VectorInt3 hitFaceNormal;

        public AlwaysHitScene(double hitDistance)
        {
            this.hitDistance = hitDistance;
            sampleLight = new Vector4(0, 0, 0, 0);
            hitFaceNormal = new VectorInt3(0, 1, 0);
        }

        public AlwaysHitScene(double hitDistance, Vector4 sampleLight, VectorInt3 hitFaceNormal)
        {
            this.hitDistance = hitDistance;
            this.sampleLight = sampleLight;
            this.hitFaceNormal = hitFaceNormal;
        }

        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = new LumOnWorldProbeTraceHit(
                HitDistance: this.hitDistance,
                HitBlockId: 1,
                HitFace: ProbeHitFaceUtil.FromAxisNormal(hitFaceNormal),
                HitBlockPos: new VectorInt3(0, 0, 0),
                HitFaceNormal: hitFaceNormal,
                SampleBlockPos: new VectorInt3(0, 0, 0),
                SampleLightRgbS: sampleLight);
            return WorldProbeTraceOutcome.Hit;
        }
    }
}
