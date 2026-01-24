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

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class BlockAccessorWorldProbeTraceSceneTests
{
    [Fact]
    public void Trace_RejectsZeroDirection()
    {
        var blockAccessor = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(0, 0, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Trace_RejectsNonPositiveMaxDistance(double maxDistance)
    {
        var blockAccessor = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0, 0, 0), Vector3.UnitX, maxDistance, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Fact]
    public void Trace_WhenMaxDistanceShorterThanFirstVoxelBoundary_Misses()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // First boundary crossing along +X from (0.5,...) occurs at t=0.5.
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, maxDistance: 0.49, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Fact]
    public void Trace_WithAxisAlignedDirection_FromVoxelBoundary_Works()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        // Origin is exactly on a voxel boundary for Y.
        var outcome = scene.Trace(new Vector3d(0.25, 0.0, 0.25), Vector3.UnitY, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, -1, 0), traceHit.HitFaceNormal);
        Assert.Equal(1.0, traceHit.HitDistance, 12);
    }

    [Fact]
    public void Trace_WhenHitInNextVoxelAlongNegativeY_ReturnsExpectedHitDistanceAndNormal_AndSamplesLightIfLoaded()
    {
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

        var outcome = scene.Trace(new Vector3d(0.5, 1.5, 0.5), new Vector3(0, -1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(0.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(10, 20, 30, 40), traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenChunkUnloadedAtOrigin_ReturnsMiss()
    {
        var blockAccessor = ScriptedBlockAccessorProxy.Create(new ScriptedBlockAccessorProxy.Config());
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Fact]
    public void Trace_WhenChunkBecomesUnloadedMidTrace_ReturnsMiss()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        // Next voxel in +X is unloaded.

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Fact]
    public void Trace_WhenHitInNextVoxelAlongX_ReturnsExpectedHitDistanceAndNormal_AndSamplesLightIfLoaded()
    {
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

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(0.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(1, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(10, 20, 30, 40), traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenSampleChunkUnloaded_EmitsZeroSampleLight()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;
        // First-voxel hit: synthesized face normal for +X is (-1,0,0) => sample voxel (-1,0,0).
        // Intentionally keep that sample voxel unloaded.

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(Vector4.Zero, traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenSampleChunkLoadedButNoLightEntry_EmitsZeroSampleLight()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidFull;
        // Sample voxel (0,0,0) is loaded but has no Lights entry.

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new Vector4(0, 0, 0, 0), traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenIntermediateSolidHasNoCollisionBoxes_SkipsAndContinues()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((2, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidEmptyCollision;
        cfg.Blocks[(2, 0, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(1.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
    }

    [Fact]
    public void Trace_WhenCollisionBoxesAreNull_SkipsAndContinues()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((1, 0, 0));
        cfg.LoadedChunks.Add((2, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.SolidNullCollision;
        cfg.Blocks[(2, 0, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
    }

    [Fact]
    public void Trace_SelectsAxisWithSmallestTMax()
    {
        // For positive direction, smallest tMax corresponds to the axis with the largest component.
        // Use a direction dominated by +Y so the first step should be into y=1.
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var origin = new Vector3d(0.1, 0.1, 0.1);
        var dir = new Vector3(1, 10, 1);

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

    [Fact]
    public void Trace_WhenTMaxTiesBetweenXandY_BreaksTieTowardY()
    {
        // With X==Y components and same origin offsets, tMax.X == tMax.Y.
        // Implementation uses '<' comparisons, so equality goes to the else-branch => prefers Y over X.
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.LoadedChunks.Add((0, 1, 0));
        cfg.LoadedChunks.Add((1, 0, 0));

        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;
        cfg.Blocks[(0, 1, 0)] = TestBlocks.SolidFull;
        cfg.Blocks[(1, 0, 0)] = TestBlocks.Air;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.1, 0.1, 0.1), new Vector3(1, 1, 0.1f), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, -1, 0), traceHit.HitFaceNormal);
    }

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
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.SolidFull;

        // Ensure sample voxel chunk is loaded so we don't early-out the light sample.
        cfg.LoadedChunks.Add((ex, ey, ez));
        cfg.Lights[(ex, ey, ez)] = new Vec4f(1, 2, 3, 4);

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.5), new Vector3(dx, dy, dz), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(0.0, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(ex, ey, ez), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(ex, ey, ez), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(1, 2, 3, 4), traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenHitInFirstVoxelAndOriginNearTopFace_PrefersNearestFaceForSampleVoxel()
    {
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
        var outcome = scene.Trace(new Vector3d(0.5, 0.999999999, 0.5), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(0.0, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(0, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.HitFaceNormal);
        Assert.Equal(new VectorInt3(0, 1, 0), traceHit.SampleBlockPos);
        Assert.Equal(new Vector4(5, 6, 7, 8), traceHit.SampleLightRgbS);
    }

    [Fact]
    public void Trace_WhenRayPassesThroughEmptyPartOfVoxel_DoesNotFalseHitCollisionBox()
    {
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
        var outcome = scene.Trace(new Vector3d(0.5, 0.5, 0.9), Vector3.UnitX, 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.True(hit);
        Assert.Equal(new VectorInt3(2, 0, 0), traceHit.HitBlockPos);
        Assert.Equal(1.5, traceHit.HitDistance, 12);
        Assert.Equal(new VectorInt3(-1, 0, 0), traceHit.HitFaceNormal);
    }

    [Fact]
    public void Trace_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var cfg = new ScriptedBlockAccessorProxy.Config();
        cfg.LoadedChunks.Add((0, 0, 0));
        cfg.Blocks[(0, 0, 0)] = TestBlocks.Air;

        var blockAccessor = ScriptedBlockAccessorProxy.Create(cfg);
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 10, cts.Token, out _);
        });
    }

    [Fact]
    public void Trace_WhenDirectionContainsNaN_ReturnsMiss()
    {
        var blockAccessor = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(float.NaN, 1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    [Fact]
    public void Trace_WhenDirectionContainsInfinity_ReturnsMiss()
    {
        var blockAccessor = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(float.PositiveInfinity, 1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        bool hit = outcome == WorldProbeTraceOutcome.Hit;

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        public static readonly Block SolidEmptyCollision = new() { BlockId = 1, CollisionBoxes = Array.Empty<Cuboidf>() };
        public static readonly Block SolidNullCollision = new CollisionBoxesOverrideBlock { BlockId = 1, Boxes = null };
        public static readonly Block ThinNearZ0 = new() { BlockId = 1, CollisionBoxes = new[] { new Cuboidf { X1 = 0f, Y1 = 0f, Z1 = 0f, X2 = 1f, Y2 = 1f, Z2 = 0.2f } } };

        private sealed class CollisionBoxesOverrideBlock : Block
        {
            public Cuboidf[]? Boxes { get; init; }

            public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
            {
                return Boxes!;
            }
        }
    }

    private class ScriptedBlockAccessorProxy : DispatchProxy
    {
        public sealed class Config
        {
            public HashSet<(int X, int Y, int Z)> LoadedChunks { get; } = new();
            public Dictionary<(int X, int Y, int Z), Block> Blocks { get; } = new();
            public Dictionary<(int X, int Y, int Z), Vec4f> Lights { get; } = new();
        }

        private Config cfg = new();

        public static IBlockAccessor Create(Config cfg)
        {
            object proxy = Create<IBlockAccessor, ScriptedBlockAccessorProxy>();
            ((ScriptedBlockAccessorProxy)proxy).cfg = cfg;
            return (IBlockAccessor)proxy;
        }

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
                return cfg.LoadedChunks.Contains(pos) ? NullObjectProxy.Create<IWorldChunk>() : null;
            }

            if (name == nameof(IBlockAccessor.GetMostSolidBlock))
            {
                var pos = GetPos(args);
                return cfg.Blocks.TryGetValue(pos, out var b) ? b : TestBlocks.Air;
            }

            if (name == nameof(IBlockAccessor.GetLightRGBs))
            {
                var pos = GetPos(args);
                return cfg.Lights.TryGetValue(pos, out var v) ? v : new Vec4f(0, 0, 0, 0);
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private static class NullObjectProxy
    {
        public static T Create<T>() where T : class
        {
            object proxy = DispatchProxy.Create<T, NullObjectDispatchProxy>();
            return (T)proxy;
        }

        private class NullObjectDispatchProxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod is null) return null;

                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(void)) return null;

                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }
        }
    }
}
