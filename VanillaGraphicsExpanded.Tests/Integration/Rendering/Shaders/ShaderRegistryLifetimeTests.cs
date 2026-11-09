using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;


namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Verifies shader registry reload against native executable generations and retained compute inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderRegistryLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Native executable reload
    /// <summary>Reload withdraws old readiness immediately and suppresses unchanged failures until another asset epoch.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadBlocksPreparationUntilCompletionAndRetainsFailureAllocation(bool failure)
    {
        // Arrange: Register and prepare the owner, then select a successful reload or corrupted candidate asset.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        RasterDepthCopyShaderProgram.Register(assets.Api);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var owner = registry.Get<RasterDepthCopyShaderProgram>(RasterDepthCopyShaderProgram.Contract.Identity)!;
        Assert.True(owner.EnsureReady());
        int installed = owner.ProgramId;
        ulong revision = ((IGpuProgram)owner).ExecutableRevision;
        // Act: Begin reload, request readiness before completion, and then complete the selected asset epoch.
        // Assert: Reload blocks early preparation and unchanged failure retries until a new epoch begins.
        registry.BeginReload();
        int reads = assets.Reads.Count;
        Assert.True(owner.RequiresPreparation);
        Assert.False(owner.EnsureReady());
        Assert.Equal(reads, assets.Reads.Count);
        if (failure) assets.BeforeRead = path => { if (path.EndsWith(".spv")) assets.Overrides[path] = new byte[20]; };
        registry.CompleteReload();
        Assert.Equal(!failure, owner.EnsureReady());
        if (failure)
        {
            Assert.Equal(installed, owner.ProgramId);
            Assert.Equal(revision, ((IGpuProgram)owner).ExecutableRevision);
            reads = assets.Reads.Count;
            Assert.False(owner.EnsureReady()); Assert.Equal(reads, assets.Reads.Count);
            assets.BeforeRead = null; assets.Overrides.Clear();
            registry.BeginReload(); registry.CompleteReload();
            Assert.True(owner.EnsureReady());
        }
        Assert.True(((IGpuProgram)owner).ExecutableRevision > revision);
        Assert.False(GL.IsProgram(installed));
    }


    /// <summary>Feature compute replacement preserves CPU blocks and revisions through success, failure and retry.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputeReloadPreservesWrapperAndCpuInputs(bool failedReplacement)
    {
        // Arrange: Prepare a compute owner and retain its wrapper, CPU block, program identity, and revision.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var owner = new ShaderRegistryComputeOwner(ComputeShaderAsset.DeclareFromAssets(assets.Api, settings));
        Assert.True(owner.EnsureReady());
        var block = owner.Block;
        block.SetBytes(new byte[16]);
        int old = owner.ProgramId;
        ulong revision = ((IGpuProgram)owner).ExecutableRevision;
        // Act: Invalidate assets, attempt the selected replacement, and retry after clearing an injected failure.
        // Assert: The wrapper and CPU block survive; revision advances only after a successful replacement.
        owner.InvalidateAssets();
        Assert.False(owner.IsValid);
        if (failedReplacement) assets.BeforeRead = path => { if (path.EndsWith(".spv")) assets.Overrides[path] = new byte[20]; };
        Assert.Equal(!failedReplacement, owner.EnsureReady());
        if (failedReplacement)
        {
            Assert.Equal(old, owner.ProgramId); Assert.True(GL.IsProgram(old));
            int reads = assets.Reads.Count; Assert.False(owner.EnsureReady()); Assert.Equal(reads, assets.Reads.Count);
            assets.BeforeRead = null; assets.Overrides.Clear(); owner.InvalidateAssets();
            Assert.True(owner.EnsureReady());
        }
        Assert.Same(block, owner.Block);
        block.SetBytes(new byte[16]);
        Assert.Equal(revision + 1, ((IGpuProgram)owner).ExecutableRevision);
        Assert.False(GL.IsProgram(old));
        owner.Dispose(); owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => block.SetBytes(new byte[16]));
    }


    /// <summary>A failed obsolete compute attempt cannot suppress changed assets encountered during its reads.</summary>
    [Fact]
    public void ComputeFailedSupersessionPermitsNewAssetAttempt()
    {
        // Arrange: Prepare a compute owner and supersede its asset epoch from inside a pending SPIR-V read.
        EnsureContextValid();

        using var cache = DriverProgramCache.UseStoreForTesting(null);

        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));

        using var owner = new ShaderRegistryComputeOwner(ComputeShaderAsset.DeclareFromAssets(assets.Api, settings));
        Assert.True(owner.EnsureReady());
        owner.InvalidateAssets();
        bool changed = false;
        // Act: Fail the obsolete attempt, clear the injected bytes, and request readiness again.
        // Assert: Supersession permits a new asset read and the latest generation can prepare.
        assets.BeforeRead = path =>
        {
            if (changed || !path.EndsWith(".spv")) return;
            changed = true;
            owner.InvalidateAssets();
            assets.Overrides[path] = new byte[20];
        };
        Assert.False(owner.EnsureReady()); Assert.True(changed);
        assets.BeforeRead = null; assets.Overrides.Clear();
        int reads = assets.Reads.Count;
        Assert.True(owner.EnsureReady()); Assert.True(assets.Reads.Count > reads);
    }

    /// <summary>Generated compute bindings replay nondefault retained inputs into a replacement executable and produce real mip output.</summary>
    [Fact]
    public void GeneratedComputeReloadReplaysRetainedInputs()
    {
        // Arrange: Prepare the generated depth-hierarchy owner with a nondefault extent and input image.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var shader = DepthHierarchyComputeShader.Create(assets.Api);
        using var hierarchy = new DepthHierarchyPass();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 65), Height = System.Math.Max(1, 37), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        source.UploadDataImmediate(Enumerable.Repeat(.375f, 65 * 37).ToArray());
        hierarchy.Prepare(65, 37, shader);
        hierarchy.Render(source);
        var parameters = ((IDepthHierarchyComputeBindings)shader).Parameters;
        ulong revision = ((IGpuProgram)shader).ExecutableRevision;
        int old = shader.ProgramId;
        // Act: Reload the generated executable and render a new source value without preparing the hierarchy again.
        // Assert: Wrapper parameters and revision are retained, and every existing mip reflects the new source value.
        shader.InvalidateAssets();
        Assert.True(shader.EnsureReady());
        Assert.Same(parameters, ((IDepthHierarchyComputeBindings)shader).Parameters);
        Assert.Equal(revision + 1, ((IGpuProgram)shader).ExecutableRevision);
        Assert.False(GL.IsProgram(old));
        // Do not call Prepare again: extent, tail and image views must survive on the same generated wrapper.
        source.UploadDataImmediate(Enumerable.Repeat(.625f, 65 * 37).ToArray());
        hierarchy.Render(source);
        for (int level = 0; level < hierarchy.Texture!.MipLevels; level++)
            ImageAssertions.Comparisons.CompareConstant(hierarchy.Texture, .625f, GpuImageSnapshot.Mip(hierarchy.Texture, level)).AssertEqual();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
    #endregion
    #region Private
    /// <summary>Prepares the comparison program used by the generated compute output assertions.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
