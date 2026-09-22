using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuComputePipelineSpirvIntegrationTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;

    public GpuComputePipelineSpirvIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
    }

    [Fact]
    public void DirectFileUsesExplicitContractDespiteArbitraryFilename()
    {
        _fixture.EnsureContextValid();

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

        string renamed = Path.Combine(Path.GetTempPath(), $"unrelated-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(renamed, bytes);
        try
        {
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
        finally { File.Delete(renamed); }

        // Drain errors so flaky drivers don't poison later tests.
        while (GL.GetError() != ErrorCode.NoError) { }
    }
}
