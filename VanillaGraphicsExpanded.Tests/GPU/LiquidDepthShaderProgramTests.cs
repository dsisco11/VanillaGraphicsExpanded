using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Links both owned liquid programs against their shared wave block contract.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidDepthShaderProgramTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Program linkage
    /// <summary>Visible and depth programs both link the same explicit wave-buffer slot.</summary>
    [Fact]
    public void ColorAndDepthProgramsLinkSharedWaveBlock()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var color = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        var depth = GpuShaderPrograms.Declare(assets.Api, new LiquidDepthShaderProgram());
        Assert.True(color.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.True(depth.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.Equal(15, GpuShaderContracts.Create("pbr_liquid").UniformBlocks[LiquidWaveParamsUbo.BlockName].Slot);
        Assert.Equal(15, GpuShaderContracts.Create("pbr_liquid_depth").UniformBlocks[LiquidWaveParamsUbo.BlockName].Slot);
        Assert.Equal(14, GpuShaderContracts.Create("pbr_liquid_depth").UniformBlocks[LiquidDrawParamsUbo.BlockName].Slot);
    }
    #endregion
}
