using VanillaGraphicsExpanded.Tests.TestSupport;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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
[Trait("Category", "GpuIntegration")]
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
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        bool supportsSpirv = GpuSupport.Graphics.SupportsArbGlSpirv;
        Assert.True(supportsSpirv, "GPU shader tests require GL_ARB_gl_spirv support.");

        string spvPath = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "shaders",
            "lumonscene_feedback_mark_pages.csh.spv");

        var storage = new CountingDiskCacheFileSystem();

        byte[] bytes = TestAssetSnapshot.ReadBuiltBytes(spvPath);
        Assert.True(bytes.Length > 0, "SPIR-V asset bytes should be non-empty.");

        string directory = Path.Combine(Path.GetTempPath(), $"vge-compute-interface-{Guid.NewGuid():N}");
        storage.CreateDirectory(directory);
        string renamed = Path.Combine(directory, "unrelated.bin");
        storage.WriteAllBytes(renamed, bytes);
        try
        {
            // Relocation changes neither the contract identity nor the required binary/metadata pairing.
            storage.WriteAllBytes(Path.Combine(directory, ShaderBinaryDigest.FileName), TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName));
            bool ok = ComputeShaderAsset.TryLoadFromSpirv(
                spirvBinaryPath: renamed,
                settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
                pipeline: out var pipeline,
                infoLog: out string infoLog,
                debugName: "Tests.GpuComputePipelineSpirvIntegration", fileSystem: storage);

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
            storage.DeleteFile(renamed);
            storage.DeleteFile(Path.Combine(directory, ShaderBinaryDigest.FileName));
            storage.DeleteDirectory(directory, false);
        }

        // Newly introduced driver errors are failures; the shared fixture owns test isolation.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
