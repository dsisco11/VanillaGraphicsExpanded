using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;


using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks application-wide digest sharing and explicit asset generation boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderDigestIndexCacheTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private readonly CountingDiskCacheFileSystem storage = new();

    private const string ManifestPath = "shaders/" + ShaderBinaryDigest.FileName;

    #region Public API
    #region Shared program loading
    /// <summary>Graphics and compute reloads share one manifest until the engine reload boundary.</summary>
    [Fact]
    public void GraphicsAndComputeShareManifestAcrossProgramReloads()
    {
        // Arrange: Use an isolated digest cache and one graphics plus one compute contract over shared asset metadata.
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
            // Act: Reload both program kinds repeatedly, then invoke the engine asset-reload boundary.
            // Assert: One manifest read is shared until the reload boundary causes exactly one fresh read.
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
    #endregion

    #region Private
    #region Graphics fixture
    /// <summary>Uses the existing production fullscreen contract and engine stage owners.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Public API
        /// <summary>Supplies the engine wrappers required by the binary graphics loader.</summary>
        internal FixtureProgram()
        {
            PassName = "tests/render_infrastructure";
        }
        #endregion

        #region Private
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        #endregion
        /// <summary>Registers the same contract for cache hits and ordinary linking.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
        #endregion
    }
    #endregion
    #endregion
}
