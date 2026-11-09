using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene occupancy packing behavior and contract rules.</summary>
public sealed class LumonSceneOccupancyPackingTests
{
    #region Public API
    /// <summary>Verifies the pack then unpack round trips masked bits behavior.</summary>
    [Fact]
    public void Pack_ThenUnpack_RoundTripsMaskedBits()
    {
        // Arrange legal field values, including the maximum representable identifiers.
        // Act by packing the fields, then assert each decoder recovers its own value.
        uint packed = LumonSceneOccupancyPacking.Pack(
            blockLevel: 32,
            sunLevel: 17,
            lightId: LumonSceneOccupancyPacking.LightIdMask,
            materialPaletteIndex: LumonSceneOccupancyPacking.MaterialPaletteIndexMask);
        // Assert
        Assert.Equal(32u, LumonSceneOccupancyPacking.UnpackBlockLevel(packed));
        Assert.Equal(17u, LumonSceneOccupancyPacking.UnpackSunLevel(packed));
        Assert.Equal(LumonSceneOccupancyPacking.LightIdMask, LumonSceneOccupancyPacking.UnpackLightId(packed));
        Assert.Equal(LumonSceneOccupancyPacking.MaterialPaletteIndexMask, LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(packed));
    }

    /// <summary>Verifies the pack clamped clamps to expected ranges behavior.</summary>
    [Fact]
    public void PackClamped_ClampsToExpectedRanges()
    {
        // Arrange values below and above the supported field ranges.
        // Act by packing with clamping, then assert each decoded endpoint.
        uint packed = LumonSceneOccupancyPacking.PackClamped(
            blockLevel: 999,
            sunLevel: -5,
            lightId: 999,
            materialPaletteIndex: -1);
        // Assert
        Assert.Equal(32u, LumonSceneOccupancyPacking.UnpackBlockLevel(packed));
        Assert.Equal(0u, LumonSceneOccupancyPacking.UnpackSunLevel(packed));
        Assert.Equal(LumonSceneOccupancyPacking.LightIdMask, LumonSceneOccupancyPacking.UnpackLightId(packed));
        Assert.Equal(0u, LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(packed));
    }

    /// <summary>Verifies the pack does not leak bits across fields behavior.</summary>
    [Fact]
    public void Pack_DoesNotLeakBitsAcrossFields()
    {
        // Arrange every field at its mask so adjacent bit boundaries are exercised together.
        // Act by packing all fields, then assert against their independently shifted bit pattern.
        uint packed = LumonSceneOccupancyPacking.Pack(
            blockLevel: LumonSceneOccupancyPacking.BlockLevelMask,
            sunLevel: LumonSceneOccupancyPacking.SunLevelMask,
            lightId: LumonSceneOccupancyPacking.LightIdMask,
            materialPaletteIndex: LumonSceneOccupancyPacking.MaterialPaletteIndexMask);

        uint expected =
            (LumonSceneOccupancyPacking.BlockLevelMask << LumonSceneOccupancyPacking.BlockLevelShift) |
            (LumonSceneOccupancyPacking.SunLevelMask << LumonSceneOccupancyPacking.SunLevelShift) |
            (LumonSceneOccupancyPacking.LightIdMask << LumonSceneOccupancyPacking.LightIdShift) |
            (LumonSceneOccupancyPacking.MaterialPaletteIndexMask << LumonSceneOccupancyPacking.MaterialPaletteIndexShift);
        // Assert
        Assert.Equal(expected, packed);
    }
    #endregion
}












