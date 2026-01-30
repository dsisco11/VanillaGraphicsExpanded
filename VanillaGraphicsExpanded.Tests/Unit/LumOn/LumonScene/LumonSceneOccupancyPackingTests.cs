using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneOccupancyPackingTests
{
    [Fact]
    public void Pack_ThenUnpack_RoundTripsMaskedBits()
    {
        uint packed = LumonSceneOccupancyPacking.Pack(
            blockLevel: 32,
            sunLevel: 17,
            lightId: LumonSceneOccupancyPacking.LightIdMask,
            materialPaletteIndex: LumonSceneOccupancyPacking.MaterialPaletteIndexMask);

        Assert.Equal(32u, LumonSceneOccupancyPacking.UnpackBlockLevel(packed));
        Assert.Equal(17u, LumonSceneOccupancyPacking.UnpackSunLevel(packed));
        Assert.Equal(LumonSceneOccupancyPacking.LightIdMask, LumonSceneOccupancyPacking.UnpackLightId(packed));
        Assert.Equal(LumonSceneOccupancyPacking.MaterialPaletteIndexMask, LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(packed));
    }

    [Fact]
    public void PackClamped_ClampsToExpectedRanges()
    {
        uint packed = LumonSceneOccupancyPacking.PackClamped(
            blockLevel: 999,
            sunLevel: -5,
            lightId: 999,
            materialPaletteIndex: -1);

        Assert.Equal(32u, LumonSceneOccupancyPacking.UnpackBlockLevel(packed));
        Assert.Equal(0u, LumonSceneOccupancyPacking.UnpackSunLevel(packed));
        Assert.Equal(LumonSceneOccupancyPacking.LightIdMask, LumonSceneOccupancyPacking.UnpackLightId(packed));
        Assert.Equal(0u, LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(packed));
    }

    [Fact]
    public void Pack_DoesNotLeakBitsAcrossFields()
    {
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

        Assert.Equal(expected, packed);
    }
}

