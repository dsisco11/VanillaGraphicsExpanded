using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises explicit compute contracts with independently named SPIR-V files.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuComputePipelineSpirvIntegrationTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GL state and error isolation applied to all rendering tests.</summary>
    public GpuComputePipelineSpirvIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>The explicit contract selects a binary's interface independently of its file name.</summary>
    [Fact]
    public void DirectFileUsesExplicitContractDespiteArbitraryFilename()
    {
        EnsureContextValid();

        bool supportsSpirv = GpuShaderModule.SupportsSpirv();
        Assert.True(supportsSpirv, "GPU shader tests require GL_ARB_gl_spirv support.");

        string spvPath = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "shaders",
            "lumonscene_feedback_mark_pages.csh.spv");

        Assert.True(File.Exists(spvPath), $"SPIR-V test asset missing: {spvPath}");

        byte[] bytes = File.ReadAllBytes(spvPath);
        Assert.True(bytes.Length > 0, "SPIR-V asset bytes should be non-empty.");

        string directory = Path.Combine(Path.GetTempPath(), $"vge-compute-interface-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string renamed = Path.Combine(directory, "unrelated.bin");
        File.WriteAllBytes(renamed, bytes);
        try
        {
            // Relocation changes neither the contract identity nor the required binary/metadata pairing.
            File.Copy(Path.Combine(Path.GetDirectoryName(spvPath)!, ShaderBinaryDigest.FileName),
                Path.Combine(directory, ShaderBinaryDigest.FileName));
            bool ok = GpuComputePipeline.TryLoadFromSpirv(
                spirvBinaryPath: renamed,
                settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
                pipeline: out var pipeline,
                infoLog: out string infoLog,
                debugName: "Tests.GpuComputePipelineSpirvIntegration");

            Assert.True(ok, $"Expected SPIR-V pipeline creation to succeed. InfoLog:\n{infoLog}");
            Assert.NotNull(pipeline);
            Assert.True(pipeline!.IsValid);
            Assert.True(pipeline.ProgramId != 0);

            pipeline.Dispose();
            pipeline.Dispose();
        }
        finally
        {
            ShaderDigestIndexCache.Clear();
            File.Delete(renamed);
            File.Delete(Path.Combine(directory, ShaderBinaryDigest.FileName));
            Directory.Delete(directory);
        }

        // Newly introduced driver errors are failures; the shared fixture owns test isolation.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
