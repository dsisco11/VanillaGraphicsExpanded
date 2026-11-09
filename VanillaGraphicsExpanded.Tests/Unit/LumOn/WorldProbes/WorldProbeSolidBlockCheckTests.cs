using System;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks world probe solid block check behavior and contract rules.</summary>
public sealed class WorldProbeSolidBlockCheckTests
{
    #region Public API
    #region Center occupancy
    /// <summary>Verifies the is probe center inside solid block when chunk not loaded does not disable behavior.</summary>
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenChunkNotLoaded_DoesNotDisable()
    {
        // Arrange
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => false,
            getBlock: _ => TestBlocks.SolidFull);
        // Act
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        // Assert
        Assert.False(inside);
    }
    /// <summary>Verifies the is probe center inside solid block when air does not disable behavior.</summary>
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAir_DoesNotDisable()
    {
        // Arrange
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: _ => TestBlocks.Air);
        // Act
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        // Assert
        Assert.False(inside);
    }
    /// <summary>Verifies the is probe center inside solid block when inside full cube disables behavior.</summary>
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenInsideFullCube_Disables()
    {
        // Arrange
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);

        // Center of y=0 block.
        // Act
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.5, 0.5));
        // Assert
        Assert.True(inside);
    }
    /// <summary>Verifies the is probe center inside solid block when above full cube does not disable behavior.</summary>
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAboveFullCube_DoesNotDisable()
    {
        // Arrange
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);
        // Act
        // Above y=0 block (floors to y=1, which is air).
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 1.25, 0.5));
        // Assert
        Assert.False(inside);
    }
    /// <summary>Verifies the classify probe center outside world height is outside world height behavior.</summary>
    [Fact]
    public void ClassifyProbeCenter_OutsideWorldHeight_IsOutsideWorldHeight()
    {
        // Arrange
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: _ => TestBlocks.Air,
            mapSizeY: 256);
        // Act
        var belowWorld = LumOnWorldProbeSolidBlockCheck.ClassifyProbeCenter(
            blockAccessor,
            new Vector3d(0.5, -0.25, 0.5));
        var aboveWorld = LumOnWorldProbeSolidBlockCheck.ClassifyProbeCenter(
            blockAccessor,
            new Vector3d(0.5, 256.0, 0.5));
        // Assert
        Assert.Equal(LumOnWorldProbeCenterOccupancy.OutsideWorldHeight, belowWorld);
        Assert.Equal(LumOnWorldProbeCenterOccupancy.OutsideWorldHeight, aboveWorld);
    }

    /// <summary>Verifies the is probe center inside solid block when above partial collision does not disable behavior.</summary>
    [Fact]
    public void IsProbeCenterInsideSolidBlock_WhenAbovePartialCollision_DoesNotDisable()
    {
        // Arrange
        var halfHeight = new CollisionBoxesOverrideBlock
        {
            BlockId = 1,
            Boxes = new[] { new Cuboidf { X1 = 0f, Y1 = 0f, Z1 = 0f, X2 = 1f, Y2 = 0.5f, Z2 = 1f } }
        };
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? halfHeight : TestBlocks.Air);

        // Above the half-height collision volume within y=0..1 block space.
        // Act
        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(0.5, 0.75, 0.5));
        // Assert
        Assert.False(inside);
    }
    #endregion

    #region Clipmap placement interaction
    /// <summary>Verifies the topology with spacing1p5 can place probe center inside voxel even if it looks above ground behavior.</summary>
    [Fact]
    public void Topology_WithSpacing1p5_CanPlaceProbeCenterInsideVoxelEvenIfItLooksAboveGround()
    {
        // This matches the in-game observation: a probe center can be 0.75 above the block base
        // (visually "above" due to point sprite radius), yet still lies inside the full collision box [0,1].
        // Arrange
        const int resolution = 8;
        const double spacing = 1.5;
        Vec3d cam = new(0.0, 1.0, 0.0);

        Vec3d anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing); // -> (0,0,0)
        Vec3d origin = LumOnClipmapTopology.GetOriginMinCorner(anchor, spacing, resolution); // -> y=-6

        // Act
        Vec3d probe = LumOnClipmapTopology.IndexToProbeCenterWorld(new Vec3i(0, 4, 0), origin, spacing);
        // Assert
        Assert.Equal(0.75, probe.Y, 12);

        // Act and Assert
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            isChunkLoaded: _ => true,
            getBlock: p => p.Y == 0 ? TestBlocks.SolidFull : TestBlocks.Air);

        bool inside = LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, new Vector3d(probe.X, probe.Y, probe.Z));
        // Assert
        Assert.True(inside);
    }
    #endregion

    #endregion
    #region Private
    /// <summary>Provides authored air and full-cube block fixtures for center classification cases.</summary>
    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
    }

    /// <summary>Provides a block with caller-authored collision boxes for partial-volume checks.</summary>
    private sealed class CollisionBoxesOverrideBlock : Block
    {
        #region Public API
        /// <summary>Gets or initializes the collision boxes returned for this test block.</summary>
        public Cuboidf[]? Boxes { get; init; }

        /// <summary>Returns the authored collision boxes instead of the base block's full-cube shape.</summary>
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return Boxes!;
        }
        #endregion
    }

    /// <summary>Maps accessor queries to controlled chunk-availability and block-selection functions.</summary>
    private class FunctionalBlockAccessorProxy : DispatchProxy
    {
        #region Public API
        /// <summary>Creates an accessor proxy with authored chunk availability, blocks, and world height.</summary>
        public static IBlockAccessor Create(
            System.Func<(int X, int Y, int Z), bool> isChunkLoaded,
            System.Func<(int X, int Y, int Z), Block> getBlock,
            int mapSizeY = 0)
        {
            object proxy = Create<IBlockAccessor, FunctionalBlockAccessorProxy>();
            var typed = (FunctionalBlockAccessorProxy)proxy;
            typed.isChunkLoaded = isChunkLoaded;
            typed.getBlock = getBlock;
            typed.mapSizeY = mapSizeY;
            return (IBlockAccessor)proxy;
        }
        #endregion

        #region Protected
        /// <summary>Returns controlled values for map height, chunk presence, and solid-block lookup calls.</summary>
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
            if (name == "get_" + nameof(IBlockAccessor.MapSizeY))
            {
                return mapSizeY;
            }

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
        #endregion

        #region Private
        private System.Func<(int X, int Y, int Z), bool>? isChunkLoaded;
        private System.Func<(int X, int Y, int Z), Block>? getBlock;
        private int mapSizeY;
        #endregion
    }

    /// <summary>Provides default-valued chunk objects for accessor queries marked as loaded.</summary>
    private static class NullObjectProxy
    {
        #region Public API
        /// <summary>Creates a proxy whose members return their type's default value.</summary>
        public static T Create<T>() where T : class
        {
            object proxy = DispatchProxy.Create<T, NullObjectDispatchProxy>();
            return (T)proxy;
        }
        #endregion

        #region Private
        /// <summary>Supplies default values for chunk members irrelevant to block occupancy checks.</summary>
        private class NullObjectDispatchProxy : DispatchProxy
        {
            #region Protected
            /// <summary>Returns the default value for an unused chunk member.</summary>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod is null) return null;

                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(void)) return null;

                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }
            #endregion
        }
        #endregion
    }
    #endregion
}
