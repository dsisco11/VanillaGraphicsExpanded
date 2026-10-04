using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates prepared resource activity against packaged production SPIR-V.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PreparedBindingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Different buffer extents remain invalid even when a driver reports stage-local block entries.</summary>
    [Fact]
    public void UnequalBlockSizesRejectPreparation()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var shader = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(shader.EnsureReady(), string.Join("\n", assets.Logs));
        var installed = shader.ProgramLayout.BinaryInterface!;
        int waves = installed.GetUniformBlockIndex(LiquidWaveParamsUbo.BlockName);
        int draw = installed.GetUniformBlockIndex(LiquidDrawParamsUbo.BlockName);
        GL.GetActiveUniformBlock(shader.ProgramId, waves, ActiveUniformBlockParameter.UniformBlockDataSize, out int waveSize);
        GL.GetActiveUniformBlock(shader.ProgramId, draw, ActiveUniformBlockParameter.UniformBlockDataSize, out int drawSize);
        Assert.Equal(32, waveSize);
        Assert.Equal(80, drawSize);
        foreach (int block in new[] { waves, draw })
        {
            GL.GetActiveUniformBlock(shader.ProgramId, block, ActiveUniformBlockParameter.UniformBlockReferencedByVertexShader, out int vertex);
            GL.GetActiveUniformBlock(shader.ProgramId, block, ActiveUniformBlockParameter.UniformBlockReferencedByFragmentShader, out int fragment);
            Assert.True(vertex != 0 || fragment != 0);
        }
        // Deliberately corrupt the linked interface: the owning APIs correctly
        // provide no operation for aliasing incompatible resource declarations.
        GL.UniformBlockBinding(shader.ProgramId, draw, 15);
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(() => new GpuProgramInterface(shader.ProgramId, [LiquidShaderProgram.Contract.Bindings]));
            Assert.Contains("Ambiguous linked UniformBlock binding 15", failure.Message);
            Assert.Same(installed, shader.ProgramLayout.BinaryInterface);
        }
        // Restore the executable's original resource map even if inspection fails.
        finally { GL.UniformBlockBinding(shader.ProgramId, draw, 14); }
        Assert.NotNull(new GpuProgramInterface(shader.ProgramId, [LiquidShaderProgram.Contract.Bindings]));
        Assert.Same(installed, shader.ProgramLayout.BinaryInterface);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Linked resource assignments and array extents must agree with the authoritative contract.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WrongLinkedRangeRejectsPreparation(bool wrongSlot)
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "prepared_binding.csh.spv");
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, new ShaderSettings(PreparedBindingComputeShader.Contract),
            out var pipeline, out string log), log);
        using (pipeline)
        {
            var contract = CopyResources(PreparedBindingComputeShader.Contract.Bindings);
            var inputs = contract.Samplers["inputs"];
            contract.Samplers["inputs"] = wrongSlot ? inputs with { Slot = 4 } : inputs with { ArrayLength = 1 };
            var installed = pipeline!.ProgramLayout.BinaryInterface!.PreparedBindings;
            Assert.Throws<InvalidOperationException>(() => new GpuProgramInterface(pipeline.ProgramId, [contract]));
            Assert.Same(installed, pipeline.ProgramLayout.BinaryInterface.PreparedBindings);
        }
    }

    /// <summary>Offline array samplers and images may share unit numbers while preserving element activity.</summary>
    [Fact]
    public void ArraysAndImagesUseIndependentUnits()
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "prepared_binding.csh.spv");
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, new ShaderSettings(PreparedBindingComputeShader.Contract),
            out var pipeline, out string log), log);
        using (pipeline)
        {
            var entries = pipeline!.ProgramLayout.BinaryInterface!.PreparedBindings.Entries;
            var array = entries.Single(entry => entry.Contract.Name == "inputs");
            Assert.True(array.Active);
            Assert.Equal([true, true], array.ActiveElements);
            Assert.True(entries.Single(entry => entry.Contract.Name == "outputImage").Active);
            Assert.False(entries.Single(entry => entry.Contract.Name == "unused").Active);
            // Rebuilding compatibility caches retains the already validated immutable projections.
            var layout = pipeline.ProgramLayout;
            var preparedInterface = layout.BinaryInterface!;
            // Engine APIs receive private inspection addresses even with no authored locations.
            int samplerLocation = preparedInterface.GetUniformLocation("inputs");
            Assert.True(samplerLocation >= 0);
            Assert.Equal(samplerLocation, preparedInterface.GetUniformLocation("inputs[0]"));
            Assert.Equal(samplerLocation + 1, preparedInterface.GetUniformLocation("inputs[1]"));
            Assert.Equal(-1, preparedInterface.GetUniformLocation("inputs[2]"));
            Assert.True(preparedInterface.GetUniformLocation("outputImage") >= 0);
            Assert.Equal(-1, preparedInterface.Uniforms["unused"]);
            for (int rebuild = 0; rebuild < 3; rebuild++)
            {
                layout.RebuildCache(pipeline.ProgramId);
                Assert.Same(preparedInterface.ActiveBindings(ShaderBindingKind.Sampler), layout.SamplerBindings);
                Assert.Same(preparedInterface.ActiveBindings(ShaderBindingKind.Image), layout.ImageBindings);
                Assert.Same(preparedInterface.ActiveBindings(ShaderBindingKind.UniformBlock), layout.UniformBlockBindings);
                Assert.Same(preparedInterface.ActiveBindings(ShaderBindingKind.StorageBlock), layout.ShaderStorageBlockBindings);
                Assert.Equal(7, layout.SamplerBindings["inputs"]);
                Assert.Equal(7, layout.ImageBindings["outputImage"]);
                Assert.DoesNotContain("unused", layout.SamplerBindings.Keys);
            }
        }
    }

    /// <summary>Prepared indices and inactive required resources do not depend on sampler locations or names.</summary>
    [Fact]
    public void PreparationUsesSlotsAndRecordsInactiveResources()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var shader = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram { CaptureMode = 3 });
        Assert.True(shader.EnsureReady(), string.Join("\n", assets.Logs));
        var contract = CopyResources(LiquidShaderProgram.Contract.Bindings);
        var original = contract.Samplers["terrainTex"];
        contract.Samplers.Remove("terrainTex");
        contract.Samplers.Add("diagnostic-only-name", original);
        contract.Samplers.Add("inactive-required", new(31, true));
        var prepared = new GpuProgramInterface(shader.ProgramId, [contract]).PreparedBindings;
        Assert.Equal(Enumerable.Range(0, prepared.Entries.Count), prepared.Entries.Select(entry => entry.Contract.Index));
        Assert.False(prepared.Entries.Single(entry => entry.Contract.Name == "inactive-required").Active);
        Assert.Empty(contract.UniformLocations);
    }

    /// <summary>A incompatible active type fails inspection without replacing the owning executable.</summary>
    [Fact]
    public void IncompatibleActiveTypeRejectsPreparation()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var shader = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(shader.EnsureReady(), string.Join("\n", assets.Logs));
        int executable = shader.ProgramId;
        var wrong = CopyResources(LiquidShaderProgram.Contract.Bindings);
        wrong.Samplers["terrainTex"] = wrong.Samplers["terrainTex"] with { ShaderType = ShaderResourceType.SamplerCube };
        Assert.Throws<InvalidOperationException>(() => new GpuProgramInterface(executable, [wrong]));
        Assert.Equal(executable, shader.ProgramId);
        Assert.True(shader.EnsureReady());
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
