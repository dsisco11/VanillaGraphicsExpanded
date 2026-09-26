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
[Trait("Category", "GPU")]
public sealed class ShaderDigestIndexCacheTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
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
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
            for (int generation = 0; generation < 3; generation++)
            {
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, settings, out var pipeline, out string log,
                    ct: TestContext.Current.CancellationToken), log);
                pipeline!.Dispose();
            }
            Assert.Single(assets.Reads.Where(path => path == ManifestPath));
            // Refreshing metadata belongs to asset reload, not disposal or recompilation of a program.
            ShaderDigestReloadHook.Prefix();
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.Equal(2, assets.Reads.Count(path => path == ManifestPath));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            ShaderDigestIndexCache.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    #endregion

    #region Manifest lifetime
    /// <summary>Owners and domains have independent indexes while repeated lookups retain one parsed object.</summary>
    [Fact]
    public void AssetOwnersAndDomainsAreIsolated()
    {
        using var first = new BinaryShaderApiFixture();
        using var second = new BinaryShaderApiFixture();
        var a = ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded");
        Assert.NotNull(a);
        Assert.Same(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded"));
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "otherdomain"));
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(second.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, first.Reads.Count(path => path == ManifestPath));
        Assert.Single(second.Reads.Where(path => path == ManifestPath));
    }

    /// <summary>Invalid metadata is read once and newly supplied metadata is observed after reload.</summary>
    [Fact]
    public void UnavailableMetadataIsCachedUntilReload()
    {
        using var assets = new BinaryShaderApiFixture();
        assets.Overrides[ManifestPath] = [];
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        assets.Overrides.Remove(ManifestPath);
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Single(assets.Reads.Where(path => path == ManifestPath));
        ShaderDigestReloadHook.Prefix();
        Assert.NotNull(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, assets.Reads.Count(path => path == ManifestPath));
    }

    /// <summary>Canonical file paths share parsed metadata and refresh only at the same reload boundary.</summary>
    [Fact]
    public void FileMetadataAndMissingFilesAreCachedUntilReload()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-digests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Null(ShaderDigestIndexCache.ForFile(path));
            File.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["a.spv"] = new(20, new string('A', 64)) }));
            Assert.Null(ShaderDigestIndexCache.ForFile(path));
            ShaderDigestReloadHook.Prefix();
            var first = ShaderDigestIndexCache.ForFile(path);
            Assert.NotNull(first);
            File.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["b.spv"] = new(24, new string('B', 64)) }));
            Assert.Same(first, ShaderDigestIndexCache.ForFile(Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path))));
            ShaderDigestReloadHook.Prefix();
            var replacement = ShaderDigestIndexCache.ForFile(path);
            Assert.NotNull(replacement);
            Assert.NotSame(first, replacement);
            Assert.Contains("b.spv", replacement.Binaries.Keys);
        }
        finally { ShaderDigestIndexCache.Clear(); File.Delete(path); }
    }
    #endregion

    #region Graphics fixture
    /// <summary>Uses the existing production fullscreen contract and engine stage owners.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        /// <summary>Supplies the engine wrappers required by the binary graphics loader.</summary>
        internal FixtureProgram()
        {
            PassName = "tests/render_infrastructure";
            VertexShader = new Vintagestory.Client.NoObf.Shader();
            FragmentShader = new Vintagestory.Client.NoObf.Shader();
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
