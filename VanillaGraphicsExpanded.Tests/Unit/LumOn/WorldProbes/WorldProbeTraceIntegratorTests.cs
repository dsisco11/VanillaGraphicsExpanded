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
    public void TraceProbe_WhenAllHitsWithSkylightButDownFacingNormals_ProducesZeroRadianceSh()
    {
        // Skylight bounce is intentionally gated by HitFaceNormal.Y so ceilings don't "bounce" skylight.
        // With a down-facing normal and no block light, all hit radiance should be zero.
        var scene = new AlwaysHitScene(hitDistance: 4.0, sampleLight: new Vector4(0, 0, 0, 1f), hitFaceNormal: new VectorInt3(0, -1, 0));
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 4,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        Assert.True(res.ShR.Length() < 1e-6f);
        Assert.True(res.ShG.Length() < 1e-6f);
        Assert.True(res.ShB.Length() < 1e-6f);
        Assert.True(res.ShSky.Length() > 1e-3f);
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

    [Fact]
    public void TraceProbe_OpenCornerPlanes_ProducesExpectedAoConfidence_AndAnisotropicSh()
    {
        // "Voxel-like" arrangement: a floor at y=-1 and a wall at x=-1.
        // Rays with x<0 or y<0 hit; rays with x>=0 AND y>=0 miss.
        var scene = new AxisAlignedPlanesScene(
            sampleLight: new Vector4(0, 0, 0, 1f),
            new AxisAlignedPlanesScene.Plane(AxisAlignedPlanesScene.Axis.X, -1),
            new AxisAlignedPlanesScene.Plane(AxisAlignedPlanesScene.Axis.Y, -1));

        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 5,
            Request: request,
            ProbePosWorld: new Vector3d(0, 0, 0),
            MaxTraceDistanceWorld: 256);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        Assert.Equal(0.25f, res.ShortRangeAoConfidence, 6);

        // Scene is symmetric in Z, but not in X/Y.
        Assert.True(res.ShR.Y > 0f);
        Assert.True(res.ShR.W > 0f);
        Assert.InRange(MathF.Abs(res.ShR.Z), 0f, 0.05f);
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

    private sealed class AxisAlignedPlanesScene : IWorldProbeTraceScene
    {
        public enum Axis
        {
            X,
            Y,
            Z,
        }

        public readonly record struct Plane(Axis Axis, double Coordinate);

        private readonly Plane[] planes;
        private readonly Vector4 sampleLight;

        public AxisAlignedPlanesScene(Vector4 sampleLight, params Plane[] planes)
        {
            this.sampleLight = sampleLight;
            this.planes = planes ?? Array.Empty<Plane>();
        }

        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;

            if (maxDistance <= 0)
            {
                return WorldProbeTraceOutcome.Miss;
            }

            float lenSq = dirWorld.LengthSquared();
            if (lenSq < 1e-18f || float.IsNaN(lenSq) || float.IsInfinity(lenSq))
            {
                return WorldProbeTraceOutcome.Miss;
            }

            Vector3 dir = Vector3.Normalize(dirWorld);

            double bestT = double.PositiveInfinity;
            VectorInt3 bestN = default;

            for (int i = 0; i < planes.Length; i++)
            {
                Plane p = planes[i];

                double o;
                float d;
                VectorInt3 n;

                switch (p.Axis)
                {
                    case Axis.X:
                        o = originWorld.X;
                        d = dir.X;
                        n = new VectorInt3(d >= 0 ? -1 : 1, 0, 0);
                        break;
                    case Axis.Y:
                        o = originWorld.Y;
                        d = dir.Y;
                        n = new VectorInt3(0, d >= 0 ? -1 : 1, 0);
                        break;
                    default:
                        o = originWorld.Z;
                        d = dir.Z;
                        n = new VectorInt3(0, 0, d >= 0 ? -1 : 1);
                        break;
                }

                if (Math.Abs(d) < 1e-12)
                {
                    continue;
                }

                double t = (p.Coordinate - o) / d;
                if (t < 0 || t > maxDistance)
                {
                    continue;
                }

                if (t < bestT)
                {
                    bestT = t;
                    bestN = n;
                }
            }

            if (double.IsInfinity(bestT))
            {
                return WorldProbeTraceOutcome.Miss;
            }

            hit = new LumOnWorldProbeTraceHit(
                HitDistance: bestT,
                HitBlockId: 1,
                HitFace: ProbeHitFaceUtil.FromAxisNormal(bestN),
                HitBlockPos: default,
                HitFaceNormal: bestN,
                SampleBlockPos: default,
                SampleLightRgbS: sampleLight);

            return WorldProbeTraceOutcome.Hit;
        }
    }
}
