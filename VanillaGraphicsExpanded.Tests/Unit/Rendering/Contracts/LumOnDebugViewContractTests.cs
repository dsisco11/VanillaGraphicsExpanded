using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Verifies exhaustive per-view contracts without retaining a universal debug executable.</summary>
public sealed class LumOnDebugViewContractTests
{
    #region Mode ownership
    /// <summary>Every fullscreen enum value owns one unique fragment and shares the fullscreen vertex stage.</summary>
    [Fact]
    public void FullscreenModesHaveDistinctOwnedContracts()
    {
        var modes = Enum.GetValues<LumOnDebugMode>().Where(mode => mode is not
            (LumOnDebugMode.Off or LumOnDebugMode.VgeNormalDepthAtlas or LumOnDebugMode.WorldProbeOrbsPoints)).ToArray();
        Assert.Equal(68, modes.Length);
        string[] names = modes.Select(LumOnDebugShaderProgramFamily.GetProgramName).ToArray();
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
        Assert.Throws<ArgumentOutOfRangeException>(() => LumOnDebugShaderProgramFamily.GetProgramName(mode));
        Assert.False(LumOnDebugShaderProgramFamily.TryGet(mode, out _));
    }
    #endregion
}
