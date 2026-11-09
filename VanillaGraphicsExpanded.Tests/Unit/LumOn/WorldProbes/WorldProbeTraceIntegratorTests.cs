using System;
using System.Numerics;
using System.Threading;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks directional geometry samples and legacy versus cached lighting integration.</summary>
public sealed class WorldProbeTraceIntegratorTests
{
    #region Public API
    #region Sample publication and encoding
    /// <summary>A valid black contact at zero distance still carries nonzero directional readiness.</summary>
    [Fact]
    public void ZeroDistanceBlackContactHasPublishedDistanceMarker()
    {
        // Arrange
        var scene=new AlwaysHitScene(0,Vector4.Zero,new VectorInt3(0,-1,0));
        var request=new LumOnWorldProbeUpdateRequest(0,new Vec3i(),new Vec3i(),0);
        var work=new LumOnWorldProbeTraceWorkItem(0,request,new Vector3d(.5,.5,.5),32,4,4,false,.25f,-1,1e-6f,
            DeferSurfaceLighting:true);
        // Act
        var result=new LumOnWorldProbeTraceIntegrator().TraceProbe(scene,work,CancellationToken.None);
        // Assert
        Assert.True(result.Success);
        Assert.All(result.AtlasSamples,s=>
        {
            Assert.True(s.AlphaEncodedDistSigned>0);
            Assert.Equal(Vector3.Zero,s.RadianceRgb);
            Assert.NotNull(s.SurfaceHit);
        });
    }

    /// <summary>Cached hits keep sky scaling neutral; legacy hits retain their vanilla sunlight estimate.</summary>
    [Theory]
    [InlineData(true, 0f, 1f)]
    [InlineData(true, .25f, 1f)]
    [InlineData(false, 0f, 0f)]
    [InlineData(false, .25f, .25f)]
    public void TraceProbe_AllHits_UsesSkyMultiplierForSelectedLightingPath(bool deferred, float sunlight, float expectedSky)
    {
        // Arrange
        var scene = new AlwaysHitScene(4, new Vector4(0, 0, 0, sunlight), new VectorInt3(0, -1, 0));
        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(), new Vec3i(), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 2, Request: request, ProbePosWorld: new Vector3d(.5, .5, .5),
            MaxTraceDistanceWorld: 32, WorldProbeOctahedralTileSize: 4, WorldProbeAtlasTexelsPerUpdate: 4,
            EnableDirectionPIS: false, DirectionPISExploreFraction: .25f, DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f, DeferSurfaceLighting: deferred);
        // Act
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedSky, result.SkyIntensity, 6);
        Assert.Equal(4, result.AtlasSamples.Length);
        Assert.All(result.AtlasSamples, sample =>
        {
            Assert.True(sample.AlphaEncodedDistSigned > 0);
            Assert.Equal(deferred, sample.SurfaceHit.HasValue);
            Assert.Equal(Vector3.Zero, sample.RadianceRgb);
        });
    }

    /// <summary>Verifies the trace probe when no hits produces all miss samples with negative alpha and ao confidence one behavior.</summary>
    [Fact]
    public void TraceProbe_WhenNoHits_ProducesAllMissSamples_WithNegativeAlpha_AndAoConfidenceOne()
    {
        // Arrange
        var scene = new NeverHitScene();
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 1,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 16,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        float expectedMissAlpha = -(float)Math.Log(item.MaxTraceDistanceWorld + 1.0);
        Assert.True(res.ShortRangeAoConfidence > 0.99f);
        Assert.False(res.AtlasSamples.IsDefault);
        Assert.True(res.AtlasSamples.Length > 0);
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            Assert.True(res.AtlasSamples[i].AlphaEncodedDistSigned < 0f);
            Assert.Equal(expectedMissAlpha, res.AtlasSamples[i].AlphaEncodedDistSigned, 5);
            Assert.True(res.AtlasSamples[i].RadianceRgb.Length() < 1e-6f);
        }
        Assert.Equal(1f, res.SkyIntensity, 6);
        Assert.Equal(0f, res.MeanLogHitDistance, 6);
        Assert.Equal(LumOnWorldProbeImportanceFlags.None, res.ImportanceFlags);
    }

    /// <summary>Verifies the trace probe when all hits with skylight but down facing normals produces zero radiance samples behavior.</summary>
    [Fact]
    public void TraceProbe_WhenAllHitsWithSkylightButDownFacingNormals_ProducesZeroRadianceSamples()
    {
        // In the world-probe bounce model, skylight is an upper-hemisphere (+Y) source.
        // Down-facing normals have zero cosine term for all sky directions, so they receive no direct skylight.
        // With a down-facing normal and no block light, all hit radiance should be zero.
        // Arrange
        var scene = new AlwaysHitScene(hitDistance: 4.0, sampleLight: new Vector4(0, 0, 0, 1f), hitFaceNormal: new VectorInt3(0, -1, 0));
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 4,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 16,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.False(res.AtlasSamples.IsDefault);
        Assert.True(res.AtlasSamples.Length > 0);
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            Assert.True(res.AtlasSamples[i].AlphaEncodedDistSigned >= 0f);
            Assert.True(res.AtlasSamples[i].RadianceRgb.Length() < 1e-6f);
        }
        Assert.Equal(1f, res.SkyIntensity, 6);
    }

    /// <summary>Verifies the trace probe when all hits produces hit samples with non negative alpha and ao confidence zero behavior.</summary>
    [Fact]
    public void TraceProbe_WhenAllHits_ProducesHitSamples_WithNonNegativeAlpha_AndAoConfidenceZero()
    {
        // Arrange
        const double hitDistance = 4.0;
        var scene = new AlwaysHitScene(hitDistance: hitDistance);
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 2,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 16,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f,
            NearbySolidHitDistance: 4.5d);
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        float expectedHitAlpha = (float)Math.Log(hitDistance + 1.0);
        Assert.True(res.ShortRangeAoConfidence < 0.01f);
        Assert.False(res.AtlasSamples.IsDefault);
        Assert.True(res.AtlasSamples.Length > 0);
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            Assert.True(res.AtlasSamples[i].AlphaEncodedDistSigned >= 0f);
            Assert.Equal(expectedHitAlpha, res.AtlasSamples[i].AlphaEncodedDistSigned, 5);
        }
        Assert.Equal(0f, res.SkyIntensity, 6);
        Assert.True(res.MeanLogHitDistance > 1.0f);
        Assert.Equal(LumOnWorldProbeImportanceFlags.NearbySolidHit, res.ImportanceFlags);
    }
    #endregion

    #region Sky lighting and proximity importance
    /// <summary>Verifies the trace probe when only downward cardinal ray hits nearby solid sets nearby solid hit behavior.</summary>
    [Fact]
    public void TraceProbe_WhenOnlyDownwardCardinalRayHitsNearbySolid_SetsNearbySolidHit()
    {
        // Arrange
        var integrator = new LumOnWorldProbeTraceIntegrator();
        var request = new LumOnWorldProbeUpdateRequest(1, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            // Storage index 0 at frame 3 selects the -Y cardinal direction.
            FrameIndex: 3,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 4.0, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 1,
            EnableDirectionPIS: true,
            DirectionPISExploreFraction: 0f,
            DirectionPISExploreCount: 0,
            DirectionPISWeightEpsilon: 1e-6f,
            NearbySolidHitDistance: 4.0d);
        // Act
        var res = integrator.TraceProbe(new DownwardOnlyHitScene(hitDistance: 4.0), item, CancellationToken.None);
        // Assert
        Assert.Equal(LumOnWorldProbeImportanceFlags.NearbySolidHit, res.ImportanceFlags);
    }

    /// <summary>Verifies the trace probe when nearby solid hit is already known skips cardinal proximity trace behavior.</summary>
    [Fact]
    public void TraceProbe_WhenNearbySolidHitIsAlreadyKnown_SkipsCardinalProximityTrace()
    {
        // Arrange
        var integrator = new LumOnWorldProbeTraceIntegrator();
        var request = new LumOnWorldProbeUpdateRequest(
            1,
            new Vec3i(0, 0, 0),
            new Vec3i(0, 0, 0),
            0,
            LumOnWorldProbeImportanceFlags.NearbySolidHit);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 3,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 4.0, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 1,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0f,
            DirectionPISExploreCount: 0,
            DirectionPISWeightEpsilon: 1e-6f,
            NearbySolidHitDistance: 4.0d);
        var scene = new CountingNeverHitScene();
        // Act
        integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.Equal(1, scene.TraceCount);
    }
    /// <summary>Verifies the trace probe when dark walls surround a sky visible opening preserves dynamic sky intensity behavior.</summary>
    [Fact]
    public void TraceProbe_WhenDarkWallsSurroundASkyVisibleOpening_PreservesDynamicSkyIntensity()
    {
        // Arrange
        var integrator = new LumOnWorldProbeTraceIntegrator();
        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 7,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 256,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);
        // Act
        var res = integrator.TraceProbe(new OpeningAmongDarkWallsScene(), item, CancellationToken.None);
        // Assert
        Assert.Contains(res.AtlasSamples, sample => sample.AlphaEncodedDistSigned < 0f);
        Assert.Contains(res.AtlasSamples, sample => sample.AlphaEncodedDistSigned >= 0f);
        Assert.Equal(1f, res.SkyIntensity, 6);
    }

    /// <summary>Verifies the trace probe when all hits with skylight produces non zero radiance samples behavior.</summary>
    [Fact]
    public void TraceProbe_WhenAllHitsWithSkylight_ProducesNonZeroRadianceSamples()
    {
        // Arrange
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 3,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 16,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);

        // Primary rays always hit a surface that samples skylight.
        // Secondary sky-visibility rays always miss, so the skylight bounce factor is non-zero.
        var scene = new PrimaryOnlyHitScene(
            primaryOrigin: item.ProbePosWorld,
            hitDistance: 4.0,
            sampleLight: new Vector4(0, 0, 0, 0.9f),
            hitFaceNormal: new VectorInt3(0, 1, 0));
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.True(res.ShortRangeAoConfidence < 0.01f);
        Assert.False(res.AtlasSamples.IsDefault);
        Assert.True(res.AtlasSamples.Length > 0);
        bool anyNonZero = false;
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            if (res.AtlasSamples[i].RadianceRgb.Length() > 1e-5f)
            {
                anyNonZero = true;
                break;
            }
        }
        Assert.True(anyNonZero);
    }

    /// <summary>Verifies the trace probe when all hits with skylight but side facing normals produces non zero radiance samples behavior.</summary>
    [Fact]
    public void TraceProbe_WhenAllHitsWithSkylightButSideFacingNormals_ProducesNonZeroRadianceSamples()
    {
        // Regression test for 1.1: vertical surfaces should bounce some skylight when the sky hemisphere is visible.
        // Arrange
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 6,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 32,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 16,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);

        var scene = new PrimaryOnlyHitScene(
            primaryOrigin: item.ProbePosWorld,
            hitDistance: 4.0,
            sampleLight: new Vector4(0, 0, 0, 1f),
            hitFaceNormal: new VectorInt3(1, 0, 0));
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.False(res.AtlasSamples.IsDefault);
        Assert.True(res.AtlasSamples.Length > 0);
        bool anyNonZero = false;
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            if (res.AtlasSamples[i].RadianceRgb.Length() > 1e-6f)
            {
                anyNonZero = true;
                break;
            }
        }
        Assert.True(anyNonZero);
    }
    #endregion

    #region Occlusion direction
    /// <summary>Verifies the trace probe open corner planes produces expected ao confidence and bent direction behavior.</summary>
    [Fact]
    public void TraceProbe_OpenCornerPlanes_ProducesExpectedAoConfidence_AndBentDirection()
    {
        // "Voxel-like" arrangement: a floor at y=-1 and a wall at x=-1.
        // Rays with x<0 or y<0 hit; rays with x>=0 AND y>=0 miss.
        // Arrange
        var scene = new AxisAlignedPlanesScene(
            sampleLight: new Vector4(1f, 0f, 0f, 0f),
            new AxisAlignedPlanesScene.Plane(AxisAlignedPlanesScene.Axis.X, -1),
            new AxisAlignedPlanesScene.Plane(AxisAlignedPlanesScene.Axis.Y, -1));

        var integrator = new LumOnWorldProbeTraceIntegrator();

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 5,
            Request: request,
            ProbePosWorld: new Vector3d(0, 0, 0),
            MaxTraceDistanceWorld: 256,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 256,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f);
        // Act
        var res = integrator.TraceProbe(scene, item, CancellationToken.None);
        // Assert
        Assert.Equal(0.25f, res.ShortRangeAoConfidence, 6);

        // Misses are in the +X/+Y quadrant, so the bent direction should point there.
        Assert.True(res.ShortRangeAoDirWorld.X > 0f);
        Assert.True(res.ShortRangeAoDirWorld.Y > 0f);
        Assert.InRange(MathF.Abs(res.ShortRangeAoDirWorld.Z), 0f, 0.25f);
    }
    #endregion

    #endregion
    #region Private
    /// <summary>Models a fully sky-visible environment where every ray misses geometry.</summary>
    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Returns Sky for every ray and leaves the hit value empty.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return WorldProbeTraceOutcome.Sky;
        }
        #endregion
    }

    /// <summary>Models a deterministic collision response with one configured distance, normal, and light sample.</summary>
    private sealed class AlwaysHitScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Creates a hit response with zero sampled light and an upward face normal.</summary>
        public AlwaysHitScene(double hitDistance)
        {
            this.hitDistance = hitDistance;
            sampleLight = new Vector4(0, 0, 0, 0);
            hitFaceNormal = new VectorInt3(0, 1, 0);
        }

        /// <summary>Creates a hit response with the supplied distance, sampled light, and face normal.</summary>
        public AlwaysHitScene(double hitDistance, Vector4 sampleLight, VectorInt3 hitFaceNormal)
        {
            this.hitDistance = hitDistance;
            this.sampleLight = sampleLight;
            this.hitFaceNormal = hitFaceNormal;
        }

        /// <summary>Returns the configured collision response for every requested ray.</summary>
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
        #endregion

        #region Private
        private readonly double hitDistance;
        private readonly Vector4 sampleLight;
        private readonly VectorInt3 hitFaceNormal;
        #endregion
    }

    /// <summary>Models a downward-facing hit while other ray directions pass through to sky.</summary>
    private sealed class DownwardOnlyHitScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Creates a downward-hit response at the supplied distance.</summary>
        public DownwardOnlyHitScene(double hitDistance)
        {
            this.hitDistance = hitDistance;
        }

        /// <summary>Returns the configured hit for downward rays and Sky for all other directions.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            if (dirWorld.Y >= -0.999f)
            {
                hit = default;
                return WorldProbeTraceOutcome.Sky;
            }

            hit = new LumOnWorldProbeTraceHit(
                HitDistance: hitDistance,
                HitBlockId: 1,
                HitFace: ProbeHitFace.Up,
                HitBlockPos: default,
                HitFaceNormal: new VectorInt3(0, 1, 0),
                SampleBlockPos: default,
                SampleLightRgbS: Vector4.Zero);
            return WorldProbeTraceOutcome.Hit;
        }
        #endregion

        #region Private
        private readonly double hitDistance;
        #endregion
    }

    /// <summary>Models a sky-visible scene and records how many trace requests it receives.</summary>
    private sealed class CountingNeverHitScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Gets the number of ray requests handled by this scene.</summary>
        public int TraceCount { get; private set; }

        /// <summary>Counts the request and returns Sky without a collision hit.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            TraceCount++;
            hit = default;
            return WorldProbeTraceOutcome.Sky;
        }
        #endregion
    }

    /// <summary>Models a +X sky opening surrounded by deterministic collision responses.</summary>
    private sealed class OpeningAmongDarkWallsScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Returns Sky for +X rays and a one-unit wall hit for other directions.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            if (dirWorld.X > 0f)
            {
                hit = default;
                return WorldProbeTraceOutcome.Sky;
            }

            hit = new LumOnWorldProbeTraceHit(
                HitDistance: 1d,
                HitBlockId: 1,
                HitFace: ProbeHitFace.North,
                HitBlockPos: default,
                HitFaceNormal: new VectorInt3(0, 0, -1),
                SampleBlockPos: default,
                SampleLightRgbS: Vector4.Zero);
            return WorldProbeTraceOutcome.Hit;
        }
        #endregion
    }

    /// <summary>Models primary probe hits while secondary rays observe sky.</summary>
    private sealed class PrimaryOnlyHitScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Creates a scene whose primary hit uses authored distance, light, and face values.</summary>
        public PrimaryOnlyHitScene(Vector3d primaryOrigin, double hitDistance, Vector4 sampleLight, VectorInt3 hitFaceNormal)
        {
            this.primaryOrigin = primaryOrigin;
            this.hitDistance = hitDistance;
            this.sampleLight = sampleLight;
            this.hitFaceNormal = hitFaceNormal;
        }

        /// <summary>Returns a hit at the primary origin and Sky for secondary visibility rays.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            // Secondary rays start away from the probe center; returning Sky here models an open sky view.
            if (!originWorld.Equals(primaryOrigin))
            {
                hit = default;
                return WorldProbeTraceOutcome.Sky;
            }

            hit = new LumOnWorldProbeTraceHit(
                HitDistance: hitDistance,
                HitBlockId: 1,
                HitFace: ProbeHitFaceUtil.FromAxisNormal(hitFaceNormal),
                HitBlockPos: new VectorInt3(0, 0, 0),
                HitFaceNormal: hitFaceNormal,
                SampleBlockPos: new VectorInt3(0, 0, 0),
                SampleLightRgbS: sampleLight);
            return WorldProbeTraceOutcome.Hit;
        }
        #endregion

        #region Private
        private readonly Vector3d primaryOrigin;
        private readonly double hitDistance;
        private readonly Vector4 sampleLight;
        private readonly VectorInt3 hitFaceNormal;
        #endregion
    }

    /// <summary>Returns ray responses from the nearest forward intersection with authored axis-aligned planes.</summary>
    private sealed class AxisAlignedPlanesScene : IWorldProbeTraceScene
    {
        #region Public API
        /// <summary>Identifies a coordinate axis used by an authored test plane.</summary>
        public enum Axis
        {
            X,
            Y,
            Z,
        }

        /// <summary>Stores a plane's coordinate axis and location in world space.</summary>
        public readonly record struct Plane(Axis Axis, double Coordinate);

        /// <summary>Creates a scene with the supplied authored planes and hit-light value.</summary>
        public AxisAlignedPlanesScene(Vector4 sampleLight, params Plane[] planes)
        {
            this.sampleLight = sampleLight;
            this.planes = planes ?? Array.Empty<Plane>();
        }

        /// <summary>Returns the nearest forward plane hit, or Sky if the ray reaches no plane.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;

            if (maxDistance <= 0)
            {
                return WorldProbeTraceOutcome.Invalid;
            }

            float lenSq = dirWorld.LengthSquared();
            if (lenSq < 1e-18f || float.IsNaN(lenSq) || float.IsInfinity(lenSq))
            {
                return WorldProbeTraceOutcome.Invalid;
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
                return WorldProbeTraceOutcome.Sky;
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
        #endregion

        #region Private
        private readonly Plane[] planes;
        private readonly Vector4 sampleLight;
        #endregion
    }
    #endregion
}
