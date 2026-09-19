using VanillaGraphicsExpanded.LumOn.WorldProbes;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class LumOnWorldProbeImportanceTests
{
    [Fact]
    public void GetFactor_WhenProbeAndImmediateLowerProbeAreAboveRainMap_ReturnsStackedFactor()
    {
        float factor = LumOnWorldProbeImportance.GetFactor(
            sunlight: 32,
            maximumSunlight: 32,
            localY: 2,
            probeY: 17.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportance.StackedAboveRainMapFactor, factor);
    }

    [Fact]
    public void GetFactor_WhenImmediateLowerProbeIsNotAboveRainMap_UsesSunlightFactor()
    {
        float factor = LumOnWorldProbeImportance.GetFactor(
            sunlight: 32,
            maximumSunlight: 32,
            localY: 1,
            probeY: 15.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportance.DirectSunlightFactor, factor);
    }

    [Fact]
    public void GetFactor_WhenThereIsNoLowerProbeInTheClipmap_UsesSunlightFactor()
    {
        float factor = LumOnWorldProbeImportance.GetFactor(
            sunlight: 0,
            maximumSunlight: 32,
            localY: 0,
            probeY: 17.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportance.IndirectSunlightFactor, factor);
    }
}