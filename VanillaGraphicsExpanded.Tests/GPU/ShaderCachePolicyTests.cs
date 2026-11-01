using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies fixture-default executable reuse while every preparation retains independent native ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderCachePolicyTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Cached assets omit native stages/linking; explicit fresh scopes still execute both operations.</summary>
    [Fact]
    public void DefaultAssetsReuseBytesAndFreshScopesRestoreAfterFailure()
    {
        Start();
        var storage = new CountingDiskCacheFileSystem();
        var store = new ProgramBinaryStore("policy-assets", fileSystem: storage);
        using var baseline = DriverProgramCache.UseDefaultStoreForTesting(store);
        try
        {
            using var assets = new BinaryShaderApiFixture();
            PrepareAsset(assets, fresh: true);
            Assert.NotEmpty(storage.EnumerateFiles("policy-assets", "*.bin"));
            PrepareAsset(assets, fresh: false);
            Assert.Throws<IOException>((Action)(() =>
            {
                using var disabled = DriverProgramCache.UseStoreForTesting(null);
                fixture.EnsureContextValid();
                Assert.Null(DriverProgramCache.CaptureStore().Store);
                PrepareAsset(assets, fresh: true);
                throw new IOException("injected suite failure");
            }));
            Assert.Same(store, DriverProgramCache.CaptureStore().Store);
            PrepareAsset(assets, fresh: false);
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Generated kernels and declared numerical helpers avoid repeated compile/link without sharing native objects.</summary>
    [Fact]
    public void KernelAndDeclaredHelperCacheHitsHaveFreshUniformState()
    {
        Start();
        using var baseline = DriverProgramCache.UseDefaultStoreForTesting(
            new ProgramBinaryStore("policy-kernels", fileSystem: new CountingDiskCacheFileSystem()));
        try
        {
            const string source = "#version 450\nlayout(local_size_x=1)in;layout(location=0)uniform int value;layout(r32ui,binding=0)uniform uimage2D result;void main(){imageStore(result,ivec2(0),uvec4(value));}";
            using var first = new GpuFixtureProgram(source);
            GL.ProgramUniform1(first.Id, 0, 37);
            using (var capture = new NativeShaderAllocationCapture())
            {
                using var second = new GpuFixtureProgram(source);
                Assert.NotEqual(first.Id, second.Id);
                GL.GetUniform(second.Id, 0, out int value);
                Assert.Equal(0, value);
                Assert.Empty(capture.Stages);
                Assert.Equal(0, capture.Links);
            }
            using (var helper = new ShaderTestHelper())
                Assert.True(helper.CompileProgram("tests/render_infrastructure").IsSuccess);
            using (var capture = new NativeShaderAllocationCapture())
            using (var helper = new ShaderTestHelper())
            {
                var result = helper.CompileProgram("tests/render_infrastructure");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Empty(capture.Stages);
                Assert.Equal(0, capture.Links);
                Assert.NotNull(TestShaderInterfaces.BuildLayout(result.ProgramId));
            }
            using (var states = new GraphicsStateShaders()) states.Get();
            using (var capture = new NativeShaderAllocationCapture())
            using (var states = new GraphicsStateShaders())
            {
                states.Get();
                Assert.Empty(capture.Stages);
                Assert.Equal(0, capture.Links);
            }
            Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Fixture defaults survive nested setup and runner-thread changes; explicit store scopes retain precedence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void DefaultLeaseRestoresAcrossThreadsAndOutOfOrderTeardown(bool disable)
    {
        Start();
        var original = DriverProgramCache.CaptureStore().Store;
        var firstStore = new ProgramBinaryStore("policy-first", fileSystem: new CountingDiskCacheFileSystem());
        var secondStore = new ProgramBinaryStore("policy-second", fileSystem: new CountingDiskCacheFileSystem());
        using var first = DriverProgramCache.UseDefaultStoreForTesting(firstStore);
        using var second = DriverProgramCache.UseDefaultStoreForTesting(secondStore);
        first.Dispose();
        Assert.Same(secondStore, DriverProgramCache.CaptureStore().Store);
        // Manager metadata belongs to its original thread. Only the physical context and byte-store
        // policy move here; no live resource manager or native resource owner is transferred.
        fixture.UseContextLocalResources();
        var inheritedPolicy = disable ? DriverProgramCache.UseStoreForTesting(null) : null;
        Exception? failure = null;
        GLFW.MakeContextCurrent(null);
        var worker = new Thread(() =>
        {
            try
            {
                fixture.EnsureContextValid();
                Assert.Same(disable ? null : secondStore, DriverProgramCache.CaptureStore().Store);
                using (DriverProgramCache.UseStoreForTesting(null))
                {
                    fixture.EnsureContextValid();
                    Assert.Null(DriverProgramCache.CaptureStore().Store);
                }
                Assert.Same(disable ? null : secondStore, DriverProgramCache.CaptureStore().Store);
                second.Dispose();
                Assert.Same(disable ? null : original, DriverProgramCache.CaptureStore().Store);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { fixture.CleanupGpuResources(); }
                finally { GLFW.MakeContextCurrent(null); }
            }
        });
        try { worker.Start(); worker.Join(); }
        finally { inheritedPolicy?.Dispose(); fixture.EnsureContextValid(); fixture.CleanupGpuResources(); }
        Assert.Null(failure);
        Assert.Same(original, DriverProgramCache.CaptureStore().Store);
    }
    #endregion

    #region Private
    /// <summary>Initializes the shared context and explicitly requires the cache capability being exercised.</summary>
    private void Start()
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(GpuSupport.Graphics.ProgramBinaryFormats.Length == 0, "Driver executable binaries are unavailable.");
        fixture.InitializeResourceDisposal();
        // Backend admission reloads core bindings. Complete it before installing native decorators,
        // otherwise the first cold compile would replace the observer rather than measure its work.
        _ = Rendering.RHI.RHICommandContext.Current;
    }

    /// <summary>Checks actual native operations for one production asset preparation without relying solely on a hit flag.</summary>
    private static void PrepareAsset(BinaryShaderApiFixture assets, bool fresh)
    {
        using var program = new PolicyProgram();
        program.Initialize(assets.Api);
        // Keep asset-owner initialization outside the measured compile/link interval.
        using var capture = new NativeShaderAllocationCapture();
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.True(DriverProgramCache.LastLoadWasHit == !fresh,
            $"Expected fresh={fresh}; cache hit={DriverProgramCache.LastLoadWasHit}; native stages={capture.Stages.Length}; native links={capture.Links}.");
        Assert.Equal(fresh, capture.Stages.Length > 0);
        Assert.Equal(fresh ? 1 : 0, capture.Links);
    }

    /// <summary>Uses the existing minimal graphics contract without a parallel shader fixture catalog.</summary>
    private sealed class PolicyProgram : GpuProgram
    {
        /// <summary>Uses the same asset namespace and identity as the packaged contract.</summary>
        public PolicyProgram() { PassName = "tests/render_infrastructure"; AssetDomain = "vanillagraphicsexpanded"; }
        /// <summary>Selects packaged fullscreen test stages.</summary>
        internal override Rendering.Contracts.GpuShaderContract ProgramContract => RenderInfrastructureShaderProgram.Contract;
        /// <summary>Captures the contract's empty resource table for real shader preparation.</summary>
        protected override Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        /// <summary>Supplies the same authored interface on fresh and cached executable preparation.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(Rendering.Contracts.GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }
    #endregion
}
