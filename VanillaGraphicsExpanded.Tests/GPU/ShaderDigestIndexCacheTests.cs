using VanillaGraphicsExpanded.Tests.TestSupport;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks application-wide digest sharing and explicit asset generation boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderDigestIndexCacheTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private readonly CountingDiskCacheFileSystem storage = new();

    private const string ManifestPath = "shaders/" + ShaderBinaryDigest.FileName;

    #region Shared program loading
    /// <summary>Graphics and compute reloads share one manifest until the engine reload boundary.</summary>
    [Fact]
    public void GraphicsAndComputeShareManifestAcrossProgramReloads()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DigestCacheTests", Guid.NewGuid().ToString("N"));
        ShaderDigestIndexCache.Clear();
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: storage));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
            for (int generation = 0; generation < 3; generation++)
            {
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, settings, out var pipeline, out string log,
                    ct: TestContext.Current.CancellationToken), log);
                pipeline!.Dispose();
            }
            Assert.Single(assets.Reads.Where(path => path == ManifestPath));
            // Refreshing metadata belongs to asset reload, not disposal or recompilation of a program.
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.Equal(2, assets.Reads.Count(path => path == ManifestPath));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            ShaderDigestIndexCache.Clear();
            if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true);
        }
    }
    #endregion

    #region Graphics fixture
    /// <summary>Uses the existing production fullscreen contract and engine stage owners.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        #endregion

        /// <summary>Supplies the engine wrappers required by the binary graphics loader.</summary>
        internal FixtureProgram()
        {
            PassName = "tests/render_infrastructure";
        }

        /// <summary>Registers the same contract for cache hits and ordinary linking.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }
    #endregion
}
