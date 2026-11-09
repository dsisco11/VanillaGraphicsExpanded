using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Links both owned liquid programs against their shared wave block contract.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LiquidDepthShaderProgramTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Visible and depth programs both link the same explicit wave-buffer slot.</summary>
    [Fact]
    public void ColorAndDepthProgramsLinkSharedWaveBlock()
    {
        // Arrange both packaged shader owners with the same frame inputs under the production shader platform.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        var color = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        var depth = GpuShaderProgramLibraries.Declare(assets.Api, LiquidDepthShaderProgram.Contract.Identity, () => new LiquidDepthShaderProgram());
        color.FrameInputs = frameCamera;
        depth.FrameInputs = frameCamera;
        // Act by preparing both real programs from their SPIR-V artifacts.
        Assert.True(color.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.True(depth.EnsureReady(), string.Join("\n", assets.Logs));
        // Assert the visible and depth contracts publish the intended frame, draw, and shared wave slots.
        Assert.Equal(12, GpuShaderContracts.Create("pbr_liquid").UniformBlocks["VgeFrameUBO"].Slot);
        Assert.Equal(12, GpuShaderContracts.Create("pbr_liquid_depth").UniformBlocks["VgeFrameUBO"].Slot);
        Assert.Equal(28, GpuShaderContracts.Create("pbr_liquid").UniformBlocks[LiquidFrameParamsUbo.BlockName].Slot);
        Assert.Equal(15, GpuShaderContracts.Create("pbr_liquid").UniformBlocks[LiquidWaveParamsUbo.BlockName].Slot);
        Assert.Equal(15, GpuShaderContracts.Create("pbr_liquid_depth").UniformBlocks[LiquidWaveParamsUbo.BlockName].Slot);
        Assert.Equal(14, GpuShaderContracts.Create("pbr_liquid_depth").UniformBlocks[LiquidDrawParamsUbo.BlockName].Slot);
    }
    #endregion
}
