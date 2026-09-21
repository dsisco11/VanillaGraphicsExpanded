using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies conservative live-cell geometry classification and normalized light capture.</summary>
public sealed class NearFieldCellCaptureTests
{
    #region Classification
    /// <summary>Known dark air remains distinguishable from unavailable geometry.</summary>
    [Fact]
    public void DarkAir_IsKnownEmpty()
    {
        var world = new ControlledVoxelWorld();
        var result = NearFieldCellCapture.Capture(ControlledBlockAccessor.Create(world),
            new Block { BlockId = 0 }, new BlockPos(0), new NearFieldMaterialRegistry());
        Assert.Equal(1u, result.Geometry);
        Assert.Equal(Vector4.Zero, result.Light);
    }

    /// <summary>A supported wall remains opaque when its material is unresolved.</summary>
    [Fact]
    public void OpaqueCube_WithoutMaterial_RemainsOpaque()
    {
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(-1, 0.25f, 2, 0.5f) };
        var cube = new Block { BlockId = 7, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        var result = NearFieldCellCapture.Capture(ControlledBlockAccessor.Create(world),
            cube, new BlockPos(0), new NearFieldMaterialRegistry());
        Assert.Equal(2u, result.Geometry);
        Assert.Equal(new Vector4(0, 0.25f, 1, 0.5f), result.Light);
    }

    /// <summary>Partial, noncolliding and transparent geometry cannot become known air or opaque full cubes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedGeometry_IsUnavailable(int variant)
    {
        var world = new ControlledVoxelWorld();
        var block = new Block { BlockId = 7, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        if (variant == 0) block.CollisionBoxes = [new Cuboidf(0, 0, 0, 1, 0.5f, 1)];
        if (variant == 1) block.CollisionBoxes = [];
        if (variant == 2) block.AllSidesOpaque = false;
        var result = NearFieldCellCapture.Capture(ControlledBlockAccessor.Create(world),
            block, new BlockPos(0), new NearFieldMaterialRegistry());
        Assert.Equal(0u, result.Geometry);
    }

    /// <summary>An omitted non-solid layer is classified through the accessor instead of silently becoming air.</summary>
    [Fact]
    public void AirSnapshot_WithOtherLayerGeometry_IsUnavailable()
    {
        var world = new ControlledVoxelWorld();
        world.SetBlock(0, 0, 0, new Block { BlockId = 7, AllSidesOpaque = false });
        var result = NearFieldCellCapture.Capture(ControlledBlockAccessor.Create(world),
            new Block { BlockId = 0 }, new BlockPos(0), new NearFieldMaterialRegistry());
        Assert.Equal(0u, result.Geometry);
    }
    #endregion
}
