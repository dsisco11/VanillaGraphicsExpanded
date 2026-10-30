using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks startup version policy and coverage without a native context or settings-file writes.</summary>
[Trait("Category", "Unit")]
public sealed class RenderingContextRequirementTests
{
    #region Public API
    /// <summary>Only a sufficiently recent desktop context permits rendering startup.</summary>
    [Theory]
    [InlineData("4.3.0 NVIDIA 610.88", false)]
    [InlineData("4.5.0 NVIDIA", true)]
    [InlineData("4.6.0 NVIDIA", true)]
    [InlineData("OpenGL ES 4.5", false)]
    [InlineData(null, false)]
    [InlineData("unavailable", false)]
    public void ActualContextControlsAdmission(string? actual, bool expected) =>
        Assert.Equal(expected, RenderingContextRequirement.IsSupported(actual));

    /// <summary>Upgrades outdated settings without reducing a newer requested context.</summary>
    [Theory]
    [InlineData("4.3", "4.5")]
    [InlineData("4.5", "4.5")]
    [InlineData("4.6", "4.6")]
    [InlineData(null, "4.5")]
    [InlineData("invalid", "4.5")]
    public void UpgradePreservesHigherSettings(string? configured, string expected) =>
        Assert.Equal(expected, RenderingContextRequirement.UpgradeSetting(configured));

    /// <summary>Every concrete client mod system must pass the shared gate before its startup stages run.</summary>
    [Fact]
    public void AllClientSystemsUseAdmissionGate()
    {
        foreach (var type in typeof(VanillaGraphicsExpandedModSystem).Assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(ModSystem).IsAssignableFrom(type)))
        {
            var system = (ModSystem)System.Activator.CreateInstance(type, nonPublic: true)!;
            if (system.ShouldLoad(EnumAppSide.Client))
                Assert.True(typeof(ClientRenderingModSystem).IsAssignableFrom(type), type.FullName);
        }
    }
    #endregion
}
