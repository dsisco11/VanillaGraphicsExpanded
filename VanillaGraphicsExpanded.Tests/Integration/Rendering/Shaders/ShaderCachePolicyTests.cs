using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Cache;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

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
        // Arrange: Install a controlled default cache store and load one fresh asset to establish cached bytes.
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
            // Act: Reuse the cached asset, throw inside a temporary null-store lease, then prepare again after unwinding.
            Assert.Throws<IOException>((Action)(() =>
            {
                using var disabled = DriverProgramCache.UseStoreForTesting(null);
                fixture.EnsureContextValid();
                Assert.Null(DriverProgramCache.CaptureStore().Store);
                PrepareAsset(assets, fresh: true);
                throw new IOException("injected suite failure");
            }));
            // Assert: The configured default store is restored and the subsequent preparation can reuse it.
            Assert.Same(store, DriverProgramCache.CaptureStore().Store);
            PrepareAsset(assets, fresh: false);
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Fresh precompiled checker assets and declared numerical helpers reuse bytes without sharing native input state.</summary>
    [Fact]
    public void KernelAndDeclaredHelperCacheHitsHaveFreshUniformState()
    {
        // Arrange: Use fixture-default binary caching and prepare one checker program whose sampler can be mutated.
        Start();
        using var baseline = DriverProgramCache.UseDefaultStoreForTesting(
            new ProgramBinaryStore("policy-kernels", fileSystem: new CountingDiskCacheFileSystem()));
        try
        {
            using var assets = new BinaryShaderApiFixture();
            using var first = PrepareChecker(assets);
            int firstLocation = first.ProgramLayout.GetUniformLocation(first.ProgramId, "sourceImage");
            Assert.True(firstLocation >= 0);
            // Act: Mutate one program's sampler, then create cached checker and helper owners.
            // Sampler assignments are native per-program state. An independent cached-binary owner
            // must retain its declared unit rather than inherit the first owner's deliberate mutation.
            GL.ProgramUniform1(first.ProgramId, firstLocation, 37);
            // Assert: Cached peers retain their declared uniform state and add no shader-stage or link work.
            using (var capture = new NativeShaderAllocationCapture())
            {
                using var second = PrepareChecker(assets);
                Assert.NotEqual(first.ProgramId, second.ProgramId);
                int secondLocation = second.ProgramLayout.GetUniformLocation(second.ProgramId, "sourceImage");
                GL.GetUniform(second.ProgramId, secondLocation, out int value);
                Assert.Equal(0, value);
                Assert.Empty(capture.Stages);
                Assert.Equal(0, capture.Links);
            }
            using (var program = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/render_infrastructure")))))
                Assert.True(program.Program.ProgramId > 0);
            using (var capture = new NativeShaderAllocationCapture())
            using (var program = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/render_infrastructure")))))
            {
                Assert.True(program.Program.ProgramId > 0);
                Assert.Empty(capture.Stages);
                Assert.Equal(0, capture.Links);
                Assert.NotNull(program.Interface);
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
        // Arrange: Nest two default-store leases and select whether the worker inherits a disabled cache policy.
        Start();
        var original = DriverProgramCache.CaptureStore().Store;
        var firstStore = new ProgramBinaryStore("policy-first", fileSystem: new CountingDiskCacheFileSystem());
        var secondStore = new ProgramBinaryStore("policy-second", fileSystem: new CountingDiskCacheFileSystem());
        using var first = DriverProgramCache.UseDefaultStoreForTesting(firstStore);
        using var second = DriverProgramCache.UseDefaultStoreForTesting(secondStore);
        // Act: Dispose leases out of order, move the GL context to a worker, and observe each effective store there.
        first.Dispose();
        Assert.Same(secondStore, DriverProgramCache.CaptureStore().Store);
        // Manager metadata belongs to its original thread. Only the physical context and byte-store
        // policy move here; no live resource manager or native resource owner is transferred.
        fixture.RetireResourceServices();
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
                try { fixture.RetireResourceServices(); }
                finally { GLFW.MakeContextCurrent(null); }
            }
        });
        try { worker.Start(); worker.Join(); }
        finally { inheritedPolicy?.Dispose(); fixture.EnsureContextValid(); fixture.CleanupGpuResources(); }
        // Assert: Worker operations complete and teardown restores the original default store.
        Assert.Null(failure);
        Assert.Same(original, DriverProgramCache.CaptureStore().Store);
    }
    #endregion

    #region Private
    /// <summary>Creates an independent production checker asset for explicit binary-cache ownership assertions.</summary>
    private static ComputeShaderAsset PrepareChecker(BinaryShaderApiFixture assets)
    {
        var contract = ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/assertion-layer-0-0"];
        bool created = ComputeShaderAsset.TryCreateFromAssets(assets.Api, new ShaderSettings(contract),
            out var program, out string error);
        Assert.True(created, error); Assert.NotNull(program);
        return program;
    }

    /// <summary>Initializes the shared context and explicitly requires the cache capability being exercised.</summary>
    private void Start()
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(GpuSupport.Graphics.ProgramBinaryFormats.Length == 0, "Driver executable binaries are unavailable.");
        fixture.InitializeResourceDisposal();
        // Backend admission reloads core bindings. Complete it before installing native decorators,
        // otherwise the first cold compile would replace the observer rather than measure its work.
        _ = VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current;
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
        #region Public API
        /// <summary>Uses the same asset namespace and identity as the packaged contract.</summary>
        public PolicyProgram() { PassName = "tests/render_infrastructure"; AssetDomain = "vanillagraphicsexpanded"; }
        #endregion

        #region Private
        /// <summary>Selects packaged fullscreen test stages.</summary>
        internal override VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => RenderInfrastructureShaderProgram.Contract;
        /// <summary>Captures the contract's empty resource table for real shader preparation.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        /// <summary>Supplies the same authored interface on fresh and cached executable preparation.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create(ShaderName));
            return layout;
        }
        #endregion
    }
    #endregion
}
