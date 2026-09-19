using VanillaGraphicsExpanded.LumOn.WorldProbes;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class LumOnWorldProbeImportanceTests
{
    [Fact]
    public void GetDynamicFlags_WhenProbeAndImmediateLowerProbeAreAboveRainMap_ReturnsStackedFlag()
    {
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: 32,
            maximumSunlight: 32,
            localY: 2,
            probeY: 17.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportanceFlags.StackedAboveRainMap, flags);
        Assert.Equal(LumOnWorldProbeImportance.StackedAboveRainMapFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    [Fact]
    public void GetDynamicFlags_WhenImmediateLowerProbeIsNotAboveRainMap_UsesSunlightFlags()
    {
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: 32,
            maximumSunlight: 32,
            localY: 1,
            probeY: 15.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportanceFlags.None, flags);
        Assert.Equal(LumOnWorldProbeImportance.DirectSunlightFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    [Fact]
    public void GetDynamicFlags_WhenThereIsNoLowerProbeInTheClipmap_UsesIndirectSunlightFlag()
    {
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: 0,
            maximumSunlight: 32,
            localY: 0,
            probeY: 17.5,
            spacing: 4.0,
            rainMapHeight: 12);

        Assert.Equal(LumOnWorldProbeImportanceFlags.IndirectSunlight, flags);
        Assert.Equal(LumOnWorldProbeImportance.IndirectSunlightFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    [Fact]
    public void ComputeFactor_WhenProbeHasNearbySolidHit_AddsHalf()
    {
        float factor = LumOnWorldProbeImportance.ComputeFactor(LumOnWorldProbeImportanceFlags.NearbySolidHit);

        Assert.Equal(1.5f, factor);
    }

    [Fact]
    public void ComputeFactor_WhenIndirectProbeHasNearbySolidHit_ComposesFactors()
    {
        float factor = LumOnWorldProbeImportance.ComputeFactor(
            LumOnWorldProbeImportanceFlags.IndirectSunlight | LumOnWorldProbeImportanceFlags.NearbySolidHit);

        Assert.Equal(2.5f, factor);
    }
}