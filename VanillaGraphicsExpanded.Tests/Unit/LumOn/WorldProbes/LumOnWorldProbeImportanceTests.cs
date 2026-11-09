using VanillaGraphicsExpanded.LumOn.WorldProbes;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks lum on world probe importance behavior and contract rules.</summary>
public sealed class LumOnWorldProbeImportanceTests
{
    #region Public API
    /// <summary>Verifies the get dynamic flags when probe and immediate lower probe are above rain map returns stacked flag behavior.</summary>
    [Fact]
    public void GetDynamicFlags_WhenProbeAndImmediateLowerProbeAreAboveRainMap_ReturnsStackedFlag()
    {
        // Arrange
        const int sunlight = 32;
        const double probeY = 17.5;
        const int rainMapHeight = 12;
        // Act
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: sunlight,
            maximumSunlight: 32,
            localY: 2,
            probeY: probeY,
            spacing: 4.0,
            rainMapHeight: rainMapHeight);
        // Assert
        Assert.Equal(LumOnWorldProbeImportanceFlags.StackedAboveRainMap, flags);
        Assert.Equal(LumOnWorldProbeImportance.StackedAboveRainMapFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    /// <summary>Verifies the get dynamic flags when immediate lower probe is not above rain map uses sunlight flags behavior.</summary>
    [Fact]
    public void GetDynamicFlags_WhenImmediateLowerProbeIsNotAboveRainMap_UsesSunlightFlags()
    {
        // Arrange
        const int sunlight = 32;
        const double probeY = 15.5;
        const int rainMapHeight = 12;
        // Act
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: sunlight,
            maximumSunlight: 32,
            localY: 1,
            probeY: probeY,
            spacing: 4.0,
            rainMapHeight: rainMapHeight);
        // Assert
        Assert.Equal(LumOnWorldProbeImportanceFlags.None, flags);
        Assert.Equal(LumOnWorldProbeImportance.DirectSunlightFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    /// <summary>Verifies the get dynamic flags when there is no lower probe in the clipmap uses indirect sunlight flag behavior.</summary>
    [Fact]
    public void GetDynamicFlags_WhenThereIsNoLowerProbeInTheClipmap_UsesIndirectSunlightFlag()
    {
        // Arrange
        const int sunlight = 0;
        const double probeY = 17.5;
        const int rainMapHeight = 12;
        // Act
        LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportance.GetDynamicFlags(
            sunlight: sunlight,
            maximumSunlight: 32,
            localY: 0,
            probeY: probeY,
            spacing: 4.0,
            rainMapHeight: rainMapHeight);
        // Assert
        Assert.Equal(LumOnWorldProbeImportanceFlags.IndirectSunlight, flags);
        Assert.Equal(LumOnWorldProbeImportance.IndirectSunlightFactor, LumOnWorldProbeImportance.ComputeFactor(flags));
    }

    /// <summary>Verifies the compute factor when probe has nearby solid hit adds half behavior.</summary>
    [Fact]
    public void ComputeFactor_WhenProbeHasNearbySolidHit_AddsHalf()
    {
        // Arrange
        const LumOnWorldProbeImportanceFlags flags = LumOnWorldProbeImportanceFlags.NearbySolidHit;
        // Act
        float factor = LumOnWorldProbeImportance.ComputeFactor(flags);
        // Assert
        Assert.Equal(1.5f, factor);
    }
    /// <summary>Verifies the compute factor when indirect probe has nearby solid hit composes factors behavior.</summary>
    [Fact]
    public void ComputeFactor_WhenIndirectProbeHasNearbySolidHit_ComposesFactors()
    {
        // Arrange
        const LumOnWorldProbeImportanceFlags flags =
            LumOnWorldProbeImportanceFlags.IndirectSunlight | LumOnWorldProbeImportanceFlags.NearbySolidHit;
        // Act
        float factor = LumOnWorldProbeImportance.ComputeFactor(
            flags);
        // Assert
        Assert.Equal(2.5f, factor);
    }
    #endregion
}
