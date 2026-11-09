using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Verifies exhaustive per-view contracts without retaining a universal debug executable.</summary>
public sealed class LumOnDebugViewContractTests
{
    #region Public API
    #region Mode ownership
    /// <summary>Every fullscreen enum value owns one unique fragment and shares the fullscreen vertex stage.</summary>
    [Fact]
    public void FullscreenModesHaveDistinctOwnedContracts()
    {
        // Arrange: Select only modes that own a fullscreen debug shader contract.
        var modes = Enum.GetValues<LumOnDebugMode>().Where(mode => mode is not
            (LumOnDebugMode.Off or LumOnDebugMode.VgeNormalDepthAtlas or LumOnDebugMode.WorldProbeOrbsPoints)).ToArray();
        // Act: Resolve each mode's program identity.
        Assert.Equal(69, modes.Length);
        string[] names = modes.Select(LumOnDebugShaderProgramFamily.GetProgramName).ToArray();
        // Assert: Every mode owns one unique registered contract with the shared fullscreen vertex stage.
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names.Order(), LumOnDebugShaderProgram.Contracts.Select(contract => contract.Identity).Order());
        foreach (string name in names)
        {
            var contract = GpuShaderContracts.Registry.FindProgram(name);
            Assert.Equal("lumon_debug.vsh", contract.Stages[0].Source);
            Assert.Equal(name + ".fsh", contract.Stages[1].Source);
            Assert.Same(contract, new LumOnDebugShaderProgram { PassName = name }.ProgramContract);
        }
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.Registry.FindProgram("lumon_debug"));
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.Registry.FindProgram("lumon_debug_worldprobe"));
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.Registry.FindProgram("lumon_debug_gbuffer"));
    }

    /// <summary>Non-fullscreen views and unknown values cannot silently select a universal fallback.</summary>
    [Theory]
    [InlineData(LumOnDebugMode.Off)]
    [InlineData(LumOnDebugMode.VgeNormalDepthAtlas)]
    [InlineData(LumOnDebugMode.WorldProbeOrbsPoints)]
    [InlineData((LumOnDebugMode)99)]
    public void NonFullscreenModesHaveNoFallback(LumOnDebugMode mode)
    {
        // Arrange: xUnit supplies non-fullscreen and unknown values which must not name an owned program.
        // Act: Ask both public lookup paths to resolve the value.
        Assert.Throws<ArgumentOutOfRangeException>(() => LumOnDebugShaderProgramFamily.GetProgramName(mode));
        // Assert: Neither lookup supplies an implicit fallback.
        Assert.False(LumOnDebugShaderProgramFamily.TryGet(mode, out _));
    }
    #endregion
    #endregion
}
