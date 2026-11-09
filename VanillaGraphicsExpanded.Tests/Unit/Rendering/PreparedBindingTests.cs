using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Validates prepared resource activity against packaged production SPIR-V.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PreparedBindingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API




    /// <summary>Prepared indices and inactive required resources do not depend on sampler locations or names.</summary>
    [Fact]
    public void PreparationUsesSlotsAndRecordsInactiveResources()
    {
        // Arrange: Load the packaged shader, rename one contract entry, and add an inactive required sampler.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var shader = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram { CaptureMode = 3 }, new VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(LiquidShaderProgram.Contract).With("VGE_LIQUID_CAPTURE_MODE", "3"));
        Assert.True(shader.EnsureReady(), string.Join("\n", assets.Logs));
        var contract = CopyResources(LiquidShaderProgram.Contract.Bindings);
        var original = contract.Samplers["terrainTex"];
        contract.Samplers.Remove("terrainTex");
        contract.Samplers.Add("diagnostic-only-name", original);
        contract.Samplers.Add("inactive-required", new(31, true));
        var snapshot = RHIResourceManager.AcquireProgramMetadata(shader.ProgramId);
        using var acquisition = new ProgramAcquisitionCapture();
        acquisition.Calibrate(shader.ProgramId);
        // Act: Prepare bindings from supplied program metadata without querying native locations.
        var prepared = new GpuProgramInterface(shader.ProgramId, [contract], metadata: snapshot).PreparedBindings;
        // Assert: Stable indices and inactive entries are recorded without acquisition reads or legacy locations.
        Assert.Equal(0, acquisition.Reads);
        Assert.Equal(Enumerable.Range(0, prepared.Entries.Count), prepared.Entries.Select(entry => entry.Contract.Index));
        Assert.False(prepared.Entries.Single(entry => entry.Contract.Name == "inactive-required").Active);
        Assert.Empty(contract.UniformLocations);
    }

    #endregion

    #region Private
    /// <summary>Copies resource declarations while intentionally omitting every legacy uniform-location entry.</summary>
    private static GpuBindingContract CopyResources(GpuBindingContract source)
    {
        var result = new GpuBindingContract();
        foreach (var pair in source.Samplers) result.Samplers.Add(pair);
        foreach (var pair in source.Images) result.Images.Add(pair);
        foreach (var pair in source.UniformBlocks) result.UniformBlocks.Add(pair);
        foreach (var pair in source.StorageBlocks) result.StorageBlocks.Add(pair);
        foreach (var pair in source.AtomicCounters) result.AtomicCounters.Add(pair);
        return result;
    }
    #endregion
}
