using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.TraceScene;

public sealed class LumonSceneTraceSceneClipmapMathTests
{
    [Fact]
    public void ComputeWorldCellBoundsForLevelWindow_Level0_MatchesResolution()
    {
        var originMin = new VectorInt3(100, 0, -50);
        LumonSceneTraceSceneClipmapMath.ComputeWorldCellBoundsForLevelWindow(
            originMinCellLevel: originMin,
            level: 0,
            resolution: 64,
            worldMinCell: out VectorInt3 wmin,
            worldMaxInclusiveCell: out VectorInt3 wmax);

        Assert.Equal(originMin, wmin);
        Assert.Equal(new VectorInt3(100 + 63, 0 + 63, -50 + 63), wmax);
    }

    [Fact]
    public void ComputeWorldCellBoundsForLevelWindow_Level4_ExpandsBySpacing()
    {
        // Level 4 spacing = 16 blocks/cell; window width in blocks = resolution * 16.
        var originMinLevelCell = new VectorInt3(10, 2, 20);
        LumonSceneTraceSceneClipmapMath.ComputeWorldCellBoundsForLevelWindow(
            originMinCellLevel: originMinLevelCell,
            level: 4,
            resolution: 64,
            worldMinCell: out VectorInt3 wmin,
            worldMaxInclusiveCell: out VectorInt3 wmax);

        Assert.Equal(new VectorInt3(10 << 4, 2 << 4, 20 << 4), wmin);
        Assert.Equal(new VectorInt3(((10 + 63) << 4) + 15, ((2 + 63) << 4) + 15, ((20 + 63) << 4) + 15), wmax);
    }
}

