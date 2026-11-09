using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.WorldProbes;

/// <summary>Checks block-accessor collision traversal and its legacy light-sampling boundary.</summary>
public sealed class BlockAccessorWorldProbeTraceSceneTests
{
    #region Public API
    /// <summary>A loaded geometric hit remains valid without its exterior lighting neighbor.</summary>
    [Fact]
    public void Trace_WithoutVanillaLighting_IgnoresMissingLightNeighbor()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config { RejectLightReads = true };
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;
        var scene = new BlockAccessorWorldProbeTraceScene(ScriptedBlockAccessorProxy.Create(cfg), sampleVanillaLighting: false);

        // Starting inside geometry chooses an exterior neighbor that traversal never needed to load.
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(.5, .5, .5), Vector3.UnitX, 10, CancellationToken.None, out var hit);
        Assert.Equal(WorldProbeTraceOutcome.Hit, outcome);
        Assert.Equal(0, hit.HitDistance);
        Assert.Equal(1, hit.HitBlockId);
        Assert.Equal(new VectorInt3(0, 0, 0), hit.HitBlockPos);
        Assert.Equal(new VectorInt3(-1, 0, 0), hit.HitFaceNormal);
        Assert.Equal(new VectorInt3(-1, 0, 0), hit.SampleBlockPos);
        Assert.Equal(Vector4.Zero, hit.SampleLightRgbS);
        Assert.Equal(new[] { (0, 0, 0) }, cfg.ChunkReads);
        Assert.Equal(0, cfg.LightReads);
    }

    /// <summary>Disabling vanilla lighting preserves every hit field while avoiding even loaded light samples.</summary>
    [Fact]
    public void Trace_WithoutVanillaLighting_PreservesGeometryAndNeverReadsLight()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.UnionWith(new[] { (0, 0, 0), (1, 0, 0) });
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;
        cfg.Lights[(0, 0, 0)] = new Vec4f(10, 20, 30, 40);
        var accessor = ScriptedBlockAccessorProxy.Create(cfg);
        var legacy = new BlockAccessorWorldProbeTraceScene(accessor);
        // Act and Assert
        Assert.Equal(WorldProbeTraceOutcome.Hit, legacy.Trace(new Vector3d(.5, .5, .5), Vector3.UnitX, 10, CancellationToken.None, out var expected));
        Assert.Equal(new Vector4(10, 20, 30, 40), expected.SampleLightRgbS);
        Assert.Equal(1, cfg.LightReads);

        cfg.LightReads = 0;
        cfg.ChunkReads.Clear();
        cfg.RejectLightReads = true;
        var scene = new BlockAccessorWorldProbeTraceScene(accessor, sampleVanillaLighting: false);
        // Act and Assert
        Assert.Equal(WorldProbeTraceOutcome.Hit, scene.Trace(new Vector3d(.5, .5, .5), Vector3.UnitX, 10, CancellationToken.None, out var actual));
        Assert.Equal(expected with { SampleLightRgbS = Vector4.Zero }, actual);
        Assert.Equal(new[] { (0, 0, 0), (1, 0, 0) }, cfg.ChunkReads);
        Assert.Equal(0, cfg.LightReads);
    }

    /// <summary>Geometry availability remains mandatory even when light sampling is disabled.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Trace_WithoutVanillaLighting_MissingTraversedGeometryAborts(bool originLoaded)
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config { RejectLightReads = true };
        if (originLoaded) cfg.LoadedChunks.Add((0, 0, 0));
        var scene = new BlockAccessorWorldProbeTraceScene(ScriptedBlockAccessorProxy.Create(cfg), sampleVanillaLighting: false);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(.5, .5, .5), Vector3.UnitX, 10, CancellationToken.None, out var hit);
        Assert.Equal(WorldProbeTraceOutcome.Unavailable, outcome);
        Assert.Equal(default, hit);
        Assert.Equal(0, cfg.LightReads);
    }





    /// <summary>Returns a miss when the maximum distance ends before the first voxel boundary.</summary>
    [Fact]
    public void Trace_WhenMaxDistanceShorterThanFirstVoxelBoundary_Misses()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // First boundary crossing along +X from (0.5,...) occurs at t=0.5.
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, maxDistance: 0.49, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    /// <summary>Traverses the next voxel when an axis-aligned ray starts on a voxel boundary.</summary>
    [Fact]
    public void Trace_WithAxisAlignedDirection_FromVoxelBoundary_Works()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // Origin is exactly on a voxel boundary for Y.
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.25, 0.0, 0.25), Vector3.UnitY, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, -1, 0), traceHit.HitFaceNormal);
        Assert.Equal(1.0, traceHit.HitDistance, 12);
    }

    /// <summary>Reports the hit distance and normal, then samples light from the loaded neighbor on a negative-Y traversal.</summary>
    [Fact]
    public void Trace_WhenHitInNextVoxelAlongNegativeY_ReturnsExpectedHitDistanceAndNormal_AndSamplesLightIfLoaded()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();

        // Loaded along path.
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.LoadedChunks.Add((0, 0, 0));

        cfg.Blocks[(0, 1, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;

        // Sample position should be (0,1,0) since we enter the hit voxel from +Y.
        cfg.Lights[(0, 1, 0)] = new Vec4f(10, 20, 30, 40);
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 1.5, 0.5), new Vector3(0, -1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(0.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(10, 20, 30, 40), traceHit.SampleLightRgbS);
    }

    /// <summary>Returns unavailable when the origin chunk is unloaded.</summary>
    [Fact]
    public void Trace_WhenChunkUnloadedAtOrigin_ReturnsAborted()
    {
        // Arrange
        var blockAccessor = ScriptedBlockAccessorProxy.Create(new ScriptedBlockAccessorProxy.Config());
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.False(hit);
        Assert.Equal(WorldProbeTraceOutcome.Unavailable, outcome);
        Assert.Equal(default, traceHit);
    }

    /// <summary>Returns unavailable when a traversed chunk becomes unloaded.</summary>
    [Fact]
    public void Trace_WhenChunkBecomesUnloadedMidTrace_ReturnsAborted()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        // Next voxel in +X is unloaded.
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.False(hit);
        Assert.Equal(WorldProbeTraceOutcome.Unavailable, outcome);
        Assert.Equal(default, traceHit);
    }

    /// <summary>Reports the hit distance and normal, then samples light from the loaded neighbor on an X traversal.</summary>
    [Fact]
    public void Trace_WhenHitInNextVoxelAlongX_ReturnsExpectedHitDistanceAndNormal_AndSamplesLightIfLoaded()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();

        // Loaded along path.
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((0, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;

        // Sample position should be (0,0,0) since we enter the hit voxel from -X.
        cfg.Lights[(0, 0, 0)] = new Vec4f(10, 20, 30, 40);
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(0.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(1, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(10, 20, 30, 40), traceHit.SampleLightRgbS);
    }

    /// <summary>Returns unavailable when the chunk selected for light sampling is unloaded.</summary>
    [Fact]
    public void Trace_WhenSampleChunkUnloaded_ReturnsAborted()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;
        // First-voxel hit: synthesized face normal for +X is (-1,0,0) => sample voxel (-1,0,0).
        // Intentionally keep that sample voxel unloaded.
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.False(hit);
        Assert.Equal(WorldProbeTraceOutcome.Unavailable, outcome);
        Assert.Equal(default, traceHit);
    }

    /// <summary>Emits a zero light sample when the selected loaded chunk has no light entry.</summary>
    [Fact]
    public void Trace_WhenSampleChunkLoadedButNoLightEntry_EmitsZeroSampleLight()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;
        // Sample voxel (0,0,0) is loaded but has no Lights entry.
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new Vector4(0, 0, 0, 0), traceHit.SampleLightRgbS);
    }

    /// <summary>Skips an intermediate solid with no collision boxes and continues tracing.</summary>
    [Fact]
    public void Trace_WhenIntermediateSolidHasNoCollisionBoxes_SkipsAndContinues()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((2, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidEmptyCollision;
        cfg.Blocks[(2, 0, 0)] = TestBlocks.SolidFull;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(1.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
    }

    /// <summary>Skips a block with null collision boxes and continues tracing.</summary>
    [Fact]
    public void Trace_WhenCollisionBoxesAreNull_SkipsAndContinues()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((2, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidNullCollision;
        cfg.Blocks[(2, 0, 0)] = TestBlocks.SolidFull;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
    }

    /// <summary>Advances along the axis with the smallest next-boundary distance.</summary>
    [Fact]
    public void Trace_SelectsAxisWithSmallestTMax()
    {
        // For positive direction, smallest tMax corresponds to the axis with the largest component.
        // Use a direction dominated by +Y so the first step should be into y=1.
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var origin = new Vector3d(0.1, 0.1, 0.1);
        var dir = new Vector3(1, 10, 1);
        // Act and Assert
        var outcome = scene.Trace(origin, dir, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);

        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, -1, 0), traceHit.HitFaceNormal);

        // t = (1 - originY) / (dyNormalized)
        double len = Math.Sqrt((1 * 1) + (10 * 10) + (1 * 1));
        double dyNorm = 10 / len;
        double expectedT = (1.0 - 0.1) / dyNorm;
        Assert.Equal(expectedT, traceHit.HitDistance, 12);
    }

    /// <summary>Breaks equal X/Y boundary-distance ties toward Y.</summary>
    [Fact]
    public void Trace_WhenTMaxTiesBetweenXandY_BreaksTieTowardY()
    {
        // With X==Y components and same origin offsets, tMax.X == tMax.Y.
        // Implementation uses '<' comparisons, so equality goes to the else-branch => prefers Y over X.
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.Air;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.1, 0.1, 0.1), new Vector3(1, 1, 0.1f), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, -1, 0), traceHit.HitFaceNormal);
    }

    /// <summary>Synthesizes the hit face normal from the ray's dominant direction axis.</summary>
    [Theory]
    [InlineData(1f, 0.1f, 0.2f, -1, 0, 0)]
    [InlineData(-1f, 0.1f, 0.2f, 1, 0, 0)]
    [InlineData(0.1f, 1f, 0.2f, 0, -1, 0)]
    [InlineData(0.1f, -1f, 0.2f, 0, 1, 0)]
    [InlineData(0.1f, 0.2f, 1f, 0, 0, -1)]
    [InlineData(0.1f, 0.2f, -1f, 0, 0, 1)]
    [InlineData(1f, 1f, 0.1f, -1, 0, 0)]
    public void Trace_WhenHitInFirstVoxel_SynthesizesFaceNormalFromDominantAxis(float dx, float dy, float dz, int ex, int ey, int ez)
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;

        // Ensure sample voxel chunk is loaded so we don't early-out the light sample.
        cfg.LoadedChunks.Add((ex, ey, ez));
        cfg.Lights[(ex, ey, ez)] = new Vec4f(1, 2, 3, 4);
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), new Vector3(dx, dy, dz), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(0.0, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(ex, ey, ez), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(ex, ey, ez), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(1, 2, 3, 4), traceHit.SampleLightRgbS);
    }

    /// <summary>Chooses the nearest face for light sampling when the ray origin is near the top face.</summary>
    [Fact]
    public void Trace_WhenHitInFirstVoxelAndOriginNearTopFace_PrefersNearestFaceForSampleVoxel()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;

        // Load the +Y sample voxel and give it light.
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.Lights[(0, 1, 0)] = new Vec4f(5, 6, 7, 8);
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // Origin is inside the full block but very close to its top face.
        // Direction is mostly +X: older behavior would have sampled (-X), but we want to sample out of the solid (+Y).
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.999999999, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(0.0, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(5, 6, 7, 8), traceHit.SampleLightRgbS);
    }

    /// <summary>Avoids a false collision hit when the ray passes through empty space inside a voxel.</summary>
    [Fact]
    public void Trace_WhenRayPassesThroughEmptyPartOfVoxel_DoesNotFalseHitCollisionBox()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((2, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.ThinNearZ0;
        cfg.Blocks[(2, 0, 0)] = TestBlocks.SolidFull;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // Ray runs along +X at Z=0.9, so it should miss the thin slab near Z=0 and hit the solid at x==2.
        // Act and Assert
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.9), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;
        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(1.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
    }

    /// <summary>Throws OperationCanceledException when tracing begins with a canceled token.</summary>
    [Fact]
    public void Trace_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Act and Assert
        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, cts.Token, out _);
        });
    }






    #endregion
    #region Private
    /// <summary>Provides the test blocks test helper.</summary>
    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        public static readonly Block SolidEmptyCollision = new() { BlockId = 1, CollisionBoxes = Array.Empty<Cuboidf>() };
        public static readonly Block SolidNullCollision = new CollisionBoxesOverrideBlock { BlockId = 1, Boxes = null };
        public static readonly Block ThinNearZ0 = new() { BlockId = 1, CollisionBoxes = new[] { new Cuboidf { X1 = 0f, Y1 = 0f, Z1 = 0f, X2 = 1f, Y2 = 1f, Z2 = 0.2f } } };

        /// <summary>Provides the collision boxes override block test helper.</summary>
        private sealed class CollisionBoxesOverrideBlock : Block
        {
            public Cuboidf[]? Boxes { get; init; }

            /// <summary>Provides the get collision boxes behavior used by this test helper.</summary>
            public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
            {
                return Boxes!;
            }
        }
    }

    /// <summary>Provides the scripted block accessor proxy test helper.</summary>
    private class ScriptedBlockAccessorProxy : DispatchProxy
    {
        /// <summary>Provides the config test helper.</summary>
        public sealed class Config
        {
            public HashSet<(int X, int Y, int Z)> LoadedChunks { get; } = new();
            public Dictionary<(int X, int Y, int Z), Block> Blocks { get; } = new();
            public Dictionary<(int X, int Y, int Z), Vec4f> Lights { get; } = new();
            public List<(int X, int Y, int Z)> ChunkReads { get; } = new();
            public bool RejectLightReads { get; set; }
            public int LightReads { get; set; }
        }

        private Config cfg = new();

        /// <summary>Provides the create behavior used by this test helper.</summary>
        public static IBlockAccessor Create(Config cfg)
        {
            object proxy = Create<IBlockAccessor, ScriptedBlockAccessorProxy>();
            ((ScriptedBlockAccessorProxy)proxy).cfg = cfg;
            return (IBlockAccessor)proxy;
        }

        /// <summary>Provides the invoke behavior used by this test helper.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            static (int X, int Y, int Z) GetPos(object?[] args)
            {
                if (args.Length == 1 && args[0] is BlockPos p)
                {
                    return (p.X, p.Y, p.Z);
                }

                if (args.Length >= 3 && args[0] is int x && args[1] is int y && args[2] is int z)
                {
                    return (x, y, z);
                }

                return default;
            }

            string name = targetMethod.Name;
            if (name == nameof(IBlockAccessor.GetChunkAtBlockPos))
            {
                var pos = GetPos(args);
                cfg.ChunkReads.Add(pos);
                return cfg.LoadedChunks.Contains(pos) ? NullObjectProxy.Create<IWorldChunk>() : null;
            }

            if (name == nameof(IBlockAccessor.GetMostSolidBlock))
            {
                var pos = GetPos(args);
                return cfg.Blocks.TryGetValue(pos, out var b) ? b : TestBlocks.Air;
            }

            if (name == nameof(IBlockAccessor.GetLightRGBs))
            {
                cfg.LightReads++;
                if (cfg.RejectLightReads) throw new InvalidOperationException("Vanilla lighting is unavailable.");
                var pos = GetPos(args);
                return cfg.Lights.TryGetValue(pos, out var v) ? v : new Vec4f(0, 0, 0, 0);
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    /// <summary>Provides the null object proxy test helper.</summary>
    private static class NullObjectProxy
    {
        /// <summary>Creates an interface proxy whose unused members return their default values.</summary>
        public static T Create<T>() where T : class
        {
            object proxy = DispatchProxy.Create<T, NullObjectDispatchProxy>();
            return (T)proxy;
        }

        /// <summary>Provides the null object dispatch proxy test helper.</summary>
        private class NullObjectDispatchProxy : DispatchProxy
        {
            /// <summary>Provides the invoke behavior used by this test helper.</summary>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod is null) return null;

                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(void)) return null;

                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }
        }
    }
    #endregion
}












