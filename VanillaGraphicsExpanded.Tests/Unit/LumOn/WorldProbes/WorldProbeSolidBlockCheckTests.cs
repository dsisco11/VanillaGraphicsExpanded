using System;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSolidBlockCheckTests
{
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenChunkNotLoaded_DoesNotDisable()
    {
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => false,
            getBlock: _ => TestBlocks.SolidFull);

        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        Assert.False(inside);
    }

    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAir_DoesNotDisable()
    {
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: _ => TestBlocks.Air);

        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        Assert.False(inside);
    }

    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenInsideFullCube_Disables()
    {
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);

        // Center of y=0 block.
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        Assert.True(inside);
    }

    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAboveFullCube_DoesNotDisable()
    {
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);

        // Above y=0 block (floors to y=1, which is air).
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 1.25, 0.5));
        Assert.False(inside);
    }

    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAbovePartialCollision_DoesNotDisable()
    {
        var halfHeight = new CollisionBoxesOverrideBlock
        {
            BlockId = 1,
            Boxes = new[] { new Cuboidf { X1 = 0f, Y1 = 0f, Z1 = 0f, X2 = 1f, Y2 = 0.5f, Z2 = 1f } }
        };

        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? halfHeight : TestBlocks.Air);

        // Above the half-height collision volume within y=0..1 block space.
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.75, 0.5));
        Assert.False(inside);
    }

    [Fact]
    public void Topology_WithSpacing1p5_CanPlaceProbeCenterInsideVoxelEvenIfItLooksAboveGround()
    {
        // This matches the in-game observation: a probe center can be 0.75 above the block base
        // (visually "above" due to point sprite radius), yet still lies inside the full collision box [0,1].
        const int resolution = 8;
        const double spacing = 1.5;
        Vec3d cam = new(0.0, 1.0, 0.0);

        Vec3d anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing); // -> (0,0,0)
        Vec3d origin = LumOnClipmapTopology.GetOriginMinCorner(anchor, spacing, resolution); // -> y=-6

        Vec3d probe = LumOnClipmapTopology.IndexToProbeCenterWorld(new Vec3i(0, 4, 0), origin, spacing);
        Assert.Equal(0.75, probe.Y, 12);

        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);

        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(probe.X, probe.Y, probe.Z));
        Assert.True(inside);
    }

    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
    }

    private sealed class CollisionBoxesOverrideBlock : Block
    {
        public Cuboidf[]? Boxes { get; init; }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return Boxes!;
        }
    }

    private class FunctionalBlockAccessorProxy : DispatchProxy
    {
        private System.Func<(int X, int Y, int Z), bool>? isChunkLoaded;
        private System.Func<(int X, int Y, int Z), Block>? getBlock;

        public static IBlockAccessor Create(
            System.Func<(int X, int Y, int Z), bool> isChunkLoaded,
            System.Func<(int X, int Y, int Z), Block> getBlock)
        {
            object proxy = Create<IBlockAccessor, FunctionalBlockAccessorProxy>();
            var typed = (FunctionalBlockAccessorProxy)proxy;
            typed.isChunkLoaded = isChunkLoaded;
            typed.getBlock = getBlock;
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
                return isChunkLoaded!(pos) ? NullObjectProxy.Create<IWorldChunk>() : null;
            }

            if (name == nameof(IBlockAccessor.GetMostSolidBlock))
            {
                var pos = GetPos(args);
                return getBlock!(pos);
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
