using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;


using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Verifies borrowed SPIR-V slice loading and compute asset replacement ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SpirvLoadingMechanicsTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API
    /// <summary>Uses the shared headless context for real driver integration.</summary>
    public SpirvLoadingMechanicsTests(HeadlessGLFixture fixture) => this.fixture = fixture;

    /// <summary>Loads and links a valid unaligned SPIR-V slice without changing its guarded backing bytes.</summary>
    [Fact]
    public void SlicedAssetsSpecializeAndLinkWithoutMutation()
    {
        // Arrange: the slice starts at an intentionally unaligned offset inside guard bytes.
        fixture.EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        var assets = new SlicedShaderAssets(path => TestAssetSnapshot.Built.Copy("shaders/" + path));
        var contract = GpuShaderContracts.Registry.FindProgram("tests/GpuProgramLayoutBindingTests_1");
        var selected = new ShaderLoadPlan(new ShaderSettings(contract)).Stages.Single();
        GpuShaderModule? shader = null;
        int program = 0;
        try
        {
            // Act: specialize the borrowed span and link it through the real OpenGL driver.
            shader = PrecompiledStageFixture.Load(selected, assets.Read);
            program = GL.CreateProgram();
            GL.AttachShader(program, shader.ShaderId);
            GL.LinkProgram(program);

            // Assert: the driver accepted the module and loading preserved both guards and payload.
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            shader?.Dispose();
            assets.AssertUnchanged();
        }
    }

    /// <summary>Disposing an older compute façade after replacement leaves the newest generation usable.</summary>
    [Fact]
    public void ReDisposingPreviousAssetAfterReplacementPreservesCurrentGeneration()
    {
        // Arrange: repeated same-contract loads exercise native ownership without production shader dependencies.
        fixture.EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        const string identity = "tests/GpuUniformRingBufferIntegrationTests_1";
        string binaryPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", identity + ".csh.spv");
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(identity));
        var layout = new GpuProgramLayout();
        layout.RegisterContract(settings.Contract.Bindings);
        ComputeShaderAsset? previous = null;

        try
        {
            // Act and assert each transition: construct replacement, re-dispose the prior façade,
            // then prove the replacement's native program and prepared pipeline remain usable.
            for (int generation = 0; generation < 3; generation++)
            {
                Assert.True(ComputeShaderAsset.TryLoadFromSpirv(
                    binaryPath,
                    settings,
                    out var current,
                    out string log,
                    layout: layout,
                    fileSystem: ShaderAssetFileSystem.Built), log);

                using (current)
                {
                    Assert.NotNull(current);
                    previous?.Dispose();
                    Assert.True(GL.IsProgram(current.ProgramId));
                    current.Pipeline.RequirePipeline().RequireShader();
                    Assert.Equal(ErrorCode.NoError, GL.GetError());

                    // Release through the state-cache owner used by activation before deletion checks.
                    OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
                    int currentProgram = current.ProgramId;
                    current.Dispose();
                    Assert.False(GL.IsProgram(currentProgram));
                    Assert.Equal(ErrorCode.NoError, GL.GetError());
                    previous = current;
                }
            }
        }
        finally
        {
            previous?.Dispose();
        }
    }
    #endregion
}
