using System;
using System.Reflection;
using System.Threading;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeTraceEndToEndVoxelArrangementTests
{
    [Fact]
    public void TraceProbe_VoxelFloor_SamplesSkylightFromAboveFace_AndProducesHemisphereSkyVisibilitySamples()
    {
        // World: infinite solid floor at y=0.
        // Light: skylight exists only above the floor (y>=1). If sampling uses the wrong face normal,
        // the sample point ends up below the floor where skylight is 0 and the sky SH DC term collapses.
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air,
            getLight: p => p.Y >= 1 ? new Vec4f(0, 0, 0, 1) : new Vec4f(0, 0, 0, 0));

        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var item = CreateWorkItem(
            frameIndex: 1,
            probePosWorld: new Vector3d(0.5, 1.5, 0.5),
            maxTraceDistanceWorld: 256);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        // Sky visibility from the probe center: floor occludes the -Y hemisphere.
        // Misses are sky-visible and encoded via signed alpha.
        Assert.Equal(0.5f, res.ShortRangeAoConfidence, 2);
        Assert.True(res.AtlasSamples.Length > 0);

        int missCount = 0;
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            if (res.AtlasSamples[i].AlphaEncodedDistSigned < 0f)
            {
                missCount++;
                Assert.True(res.AtlasSamples[i].RadianceRgb.Length() < 1e-6f);
            }
            else
            {
                Assert.True(res.AtlasSamples[i].AlphaEncodedDistSigned >= 0f);
            }
        }

        float missFrac = (float)missCount / res.AtlasSamples.Length;
        Assert.InRange(missFrac, 0.45f, 0.55f);
        Assert.InRange(res.SkyIntensity, 0.99f, 1.0f);
    }

    [Fact]
    public void TraceProbe_VoxelWall_SamplesSkylightFromOutsideFace_AndProducesHemisphereSkyVisibilitySamples()
    {
        // World: infinite solid wall plane at x=0.
        // Light: skylight exists only on the +X side of the wall (x>=1).
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            getBlock: p => p.X == 0 ? TestBlocks.SolidFull : TestBlocks.Air,
            getLight: p => p.X >= 1 ? new Vec4f(0, 0, 0, 1) : new Vec4f(0, 0, 0, 0));

        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        var integrator = new LumOnWorldProbeTraceIntegrator();

        var item = CreateWorkItem(
            frameIndex: 2,
            probePosWorld: new Vector3d(1.5, 0.5, 0.5),
            maxTraceDistanceWorld: 256);

        var res = integrator.TraceProbe(scene, item, CancellationToken.None);

        // Sky visibility from the probe center: wall occludes the -X hemisphere.
        Assert.Equal(0.5f, res.ShortRangeAoConfidence, 2);
        Assert.True(res.AtlasSamples.Length > 0);

        int missCount = 0;
        for (int i = 0; i < res.AtlasSamples.Length; i++)
        {
            if (res.AtlasSamples[i].AlphaEncodedDistSigned < 0f)
            {
                missCount++;
                Assert.True(res.AtlasSamples[i].RadianceRgb.Length() < 1e-6f);
            }
        }

        float missFrac = (float)missCount / res.AtlasSamples.Length;
        Assert.InRange(missFrac, 0.45f, 0.55f);
        Assert.InRange(res.SkyIntensity, 0.99f, 1.0f);
    }

    private static LumOnWorldProbeTraceWorkItem CreateWorkItem(int frameIndex, Vector3d probePosWorld, double maxTraceDistanceWorld)
    {
        var request = new LumOnWorldProbeUpdateRequest(Level: 0, LocalIndex: new Vec3i(0, 0, 0), StorageIndex: new Vec3i(0, 0, 0), StorageLinearIndex: 0);
        return new LumOnWorldProbeTraceWorkItem(
            FrameIndex: frameIndex,
            Request: request,
            ProbePosWorld: probePosWorld,
            MaxTraceDistanceWorld: maxTraceDistanceWorld,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 256);
    }

    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
    }

    private class FunctionalBlockAccessorProxy : DispatchProxy
    {
        private System.Func<(int X, int Y, int Z), Block>? getBlock;
        private System.Func<(int X, int Y, int Z), Vec4f>? getLight;

        public static IBlockAccessor Create(
            System.Func<(int X, int Y, int Z), Block> getBlock,
            System.Func<(int X, int Y, int Z), Vec4f> getLight)
        {
            object proxy = Create<IBlockAccessor, FunctionalBlockAccessorProxy>();
            var typed = (FunctionalBlockAccessorProxy)proxy;
            typed.getBlock = getBlock;
            typed.getLight = getLight;
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
                // Always treat chunks as loaded for these unit tests.
                return NullObjectProxy.Create<IWorldChunk>();
            }

            var pos = GetPos(args);
            if (name == nameof(IBlockAccessor.GetMostSolidBlock))
            {
                return getBlock!(pos);
            }

            if (name == nameof(IBlockAccessor.GetLightRGBs))
            {
                return getLight!(pos);
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
