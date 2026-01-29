using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class TraceSceneClipmapMathTests
{
    [Fact]
    public void WorldCellToRegionCoord_UsesArithmeticShift_ForNegatives()
    {
        // RegionSize = 32 => >> 5
        Assert.Equal(new VectorInt3(0, 0, 0), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(0, 0, 0)));
        Assert.Equal(new VectorInt3(0, 0, 0), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(31, 31, 31)));
        Assert.Equal(new VectorInt3(1, 1, 1), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(32, 32, 32)));

        // Negative arithmetic shift should floor-divide for power-of-two region size.
        Assert.Equal(new VectorInt3(-1, -1, -1), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(-1, -1, -1)));
        Assert.Equal(new VectorInt3(-1, -1, -1), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(-32, -32, -32)));
        Assert.Equal(new VectorInt3(-2, -2, -2), LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(new VectorInt3(-33, -33, -33)));
    }

    [Fact]
    public void TryMapLevelCellToTexel_WrapsAndBoundsChecks()
    {
        var originMin = new VectorInt3(100, 200, 300);
        var ring = new VectorInt3(3, 5, 7);
        int res = 16;

        // In bounds: local = (0,0,0) -> tex = ring
        Assert.True(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: originMin,
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out VectorInt3 texel));
        Assert.Equal(new VectorInt3(3, 5, 7), texel);

        // In bounds: local = (15,15,15) -> tex = (ring + 15) % 16
        Assert.True(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: new VectorInt3(115, 215, 315),
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out texel));
        Assert.Equal(new VectorInt3((3 + 15) % 16, (5 + 15) % 16, (7 + 15) % 16), texel);

        // Out of bounds: local.x == res
        Assert.False(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: new VectorInt3(originMin.X + res, originMin.Y, originMin.Z),
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out _));
    }
}

