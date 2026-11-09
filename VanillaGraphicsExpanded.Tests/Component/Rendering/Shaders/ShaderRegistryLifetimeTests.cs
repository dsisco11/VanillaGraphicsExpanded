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
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Verifies managed registry identity, admission and context retirement through the renderer component.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderRegistryLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Registry declaration and owner refresh
    /// <summary>Repeated declarations do not allocate owners, read binaries, reset edited settings or advance epochs.</summary>
    [Fact]
    public void IdenticalDeclarationPreservesOwnerAndPendingSettings()
    {
        EnsureContextValid();
        // Arrange one declared owner and a second declaration with the same identity.
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        int factories = 0;
        var settings = new ShaderSettings(VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram.Contract);
        var descriptor = new GpuProgramDeclaration("vanillagraphicsexpanded", "test-composite", typeof(VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram), settings);
        var owner = registry.Declare(descriptor, () => { factories++; return new VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram(); });
        // Act by redeclaring the identity, trying conflicting settings/type, then disposing and removing the owner.
        owner.EnableShortRangeAo = false;
        Assert.Same(owner, registry.Declare<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>(descriptor, () => { factories++; throw new InvalidOperationException(); }));
        // Assert redeclaration reuses the original owner without factory or asset reads and preserves its edited setting.
        Assert.Equal(1, factories);
        Assert.False(owner.EnableShortRangeAo);
        Assert.Empty(assets.Reads);
        Assert.Throws<InvalidOperationException>(() => registry.Declare(descriptor with { Settings = settings.With("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "0") }, () => new VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram()));
        Assert.Throws<InvalidOperationException>(() => registry.Get<RasterDepthCopyShaderProgram>("test-composite"));
        owner.Dispose();
        Assert.Throws<InvalidOperationException>(() => registry.Declare(descriptor, () => new VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram()));
        registry.Remove("test-composite");
        Assert.NotSame(owner, registry.Declare(descriptor, () => new VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram()));
    }


    /// <summary>Reload withdraws prepared owners before replacing their executable generations.</summary>
    [Fact]
    public void ReloadWithdrawsReadinessAtOwnerBoundary()
    {
        EnsureContextValid();
        // Arrange a registered raster owner with its required depth input and prepared executable.
        using var assets = new BinaryShaderApiFixture();
        RasterDepthCopyShaderProgram.Register(assets.Api);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var owner = registry.Get<RasterDepthCopyShaderProgram>(RasterDepthCopyShaderProgram.Contract.Identity)!;
        using var depth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        owner.PrimaryDepth = depth;
        Assert.True(owner.EnsureReady());
        // Act by beginning reload after preparation and checking readiness before completing the replacement boundary.
        registry.BeginReload();
        // Assert the prepared owner requires preparation while registry reload is active.
        Assert.True(owner.RequiresPreparation);
        Assert.True(registry.IsReloading);
        registry.CompleteReload();
    }

    #endregion
    #region Closed registry and client callbacks
    /// <summary>A closed registry retains its terminal identity and rejects late declarations or implicit recreation.</summary>
    [Fact]
    public void ClosedRegistryCannotReviveAfterWorldEpochChanges()
    {
        EnsureContextValid();
        // Arrange a live registry and capture its context and epoch before world teardown.
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var context = RenderContextRegistry.Current();
        long before = registry.Epoch;
        // Act by leaving the world and disposing the registry twice, then attempting factory and owner registration again.
        registry.LeaveWorld(); Assert.True(registry.Epoch > before);
        // Assert the context identity survives while terminal registry admission rejects recreation without reading assets.
        Assert.Equal(context, RenderContextRegistry.Current());
        registry.Dispose(); registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => GpuShaderProgramLibraries.Create(assets.Api));
        Assert.Throws<ObjectDisposedException>(() => RasterDepthCopyShaderProgram.Register(assets.Api));
        Assert.Empty(assets.Reads);
    }

    /// <summary>Native suppression, exception completion and world callbacks have independent admission and readiness results.</summary>
    [Fact]
    public void NativeReloadAndWorldCallbacksRespectLifetimeAdmission()
    {
        EnsureContextValid();
        // Arrange a ready shader owner plus ordered shutdown, withdrawal and preload callbacks.
        using var assets = new BinaryShaderApiFixture();
        RasterDepthCopyShaderProgram.Register(assets.Api);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        var owner = registry.Get<RasterDepthCopyShaderProgram>(RasterDepthCopyShaderProgram.Contract.Identity)!;
        Assert.True(owner.EnsureReady());
        List<string> order = [];
        lifetime.Attach(() =>
        {
            order.Add("shutdown"); Assert.False(owner.EnsureReady());
            long closedEpoch = registry.Epoch;
            Assert.Throws<ObjectDisposedException>(registry.BeginReload);
            Assert.Equal(closedEpoch, registry.Epoch);
            Assert.False(registry.IsReloading);
            var state = OpenGlStateCache.Current;
            state.Invalidate(EPipelineState.Program);
            Assert.False(state.TryGetCachedCurrentProgram(out _));
            Assert.Throws<InvalidOperationException>(() => owner.CaptureBindings());
            // Scope capture would resolve the unknown native binding; closed admission must leave it unknown.
            Assert.False(state.TryGetCachedCurrentProgram(out _));
            registry.Dispose();
        });
        using var withdraw = lifetime.RegisterPublication(() => { order.Add("withdraw"); Assert.False(owner.EnsureReady()); });
        using var preload = lifetime.RegisterPreload(() => { order.Add("preload"); Assert.True(owner.EnsureReady()); });
        bool suppressed = Vintagestory.Client.NoObf.ShaderRegistry.SupressShaderAndBufferReloads;
        try
        {
            int callbacks = 0;
            var stale = lifetime.GuardWorldCallback(() => callbacks++);
            // Act stale/current callbacks, then run suppressed and admitted native reload hooks before shutdown.
            lifetime.LeaveWorld(); stale(); Assert.Equal(0, callbacks);
            var current = lifetime.GuardWorldCallback(() => callbacks++); current(); Assert.Equal(1, callbacks);
            Vintagestory.Client.NoObf.ShaderRegistry.SupressShaderAndBufferReloads = true;
            VanillaGraphicsExpanded.HarmonyPatches.ShaderDigestReloadHook.Prefix(out bool started);
            Assert.False(started); Assert.False(owner.RequiresPreparation); Assert.Empty(order);
            VanillaGraphicsExpanded.HarmonyPatches.ShaderDigestReloadHook.Finalizer(started);
            Vintagestory.Client.NoObf.ShaderRegistry.SupressShaderAndBufferReloads = false;
            VanillaGraphicsExpanded.HarmonyPatches.ShaderDigestReloadHook.Prefix(out started);
            Assert.True(started); Assert.True(lifetime.Reloading); Assert.True(owner.RequiresPreparation);
            order.Add("native-failed");
            // Harmony finalizers run after either return or exception; native status is never replaced by VGE readiness.
            VanillaGraphicsExpanded.HarmonyPatches.ShaderDigestReloadHook.Finalizer(started);
            Assert.False(lifetime.Reloading); Assert.False(owner.RequiresPreparation);
            Assert.Equal(new[] { "withdraw", "native-failed", "preload" }, order);
            current(); Assert.Equal(1, callbacks);
            var late = lifetime.GuardWorldCallback(() => callbacks++);
            lifetime.Shutdown(); late(); Assert.Equal(1, callbacks);
            lifetime.Shutdown(); Assert.Equal(1, order.Count(value => value == "shutdown"));
            // Assert the collected callback order and terminal shutdown results throughout this sequence.
        }
        finally
        {
            Vintagestory.Client.NoObf.ShaderRegistry.SupressShaderAndBufferReloads = suppressed;
            lifetime.Shutdown();
        }
    }
    #endregion
    #region Context retirement
    /// <summary>Explicit retirement and liveness discovery consume existing scope borrows without restoring a retired context.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextRetirementClosesOutstandingScopes(bool discoveredDead)
    {
        EnsureContextValid();
        // Arrange a live context registration, compute owner and open boundary scope.
        object contextOwner = new();
        bool alive = true;
        RenderContextRegistry.RegisterCurrent(contextOwner, _ => alive);
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var compute = new ShaderRegistryComputeOwner(ComputeShaderAsset.DeclareFromAssets(assets.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"))));
        IDisposable? scope = null; int id = 0;
        lifetime.Attach(() => { compute.Dispose(); registry.Dispose(); });
        try
        {
            Assert.True(compute.EnsureReady());
            scope = RHICommandContext.Current.EnterBoundary(new("Tests.Compute.ContextRetirement", true, compute:[compute.Pipeline])); id = compute.ProgramId;
            // Act through explicit retirement or failed liveness discovery while the compute boundary remains open.
            if (discoveredDead) { alive = false; Assert.Equal(default, RenderContextRegistry.Current()); }
            else RenderContextRegistry.Retire(contextOwner);
            // Assert lifetime, registry and compute owner retire together and the outstanding scope cannot restore state.
            Assert.True(lifetime.IsClosed); Assert.True(registry.IsClosed);
            Assert.True(((IGpuProgram)compute).Lifetime.IsRetired);
            Assert.Throws<InvalidOperationException>(scope.Dispose);
            lifetime.Shutdown();
        }
        finally
        {
            RenderContextRegistry.Retire(contextOwner);
            fixture.RestoreContextRegistration();
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            if (id != 0 && GL.IsProgram(id)) GL.DeleteProgram(id);
            lifetime.Shutdown();
        }
    }

    /// <summary>Irreversible retirement releases render-pass target borrows and boundary authority without restoring native state.</summary>
    [Fact]
    public void ContextRetirementAbandonsCompleteBoundaryAndPass()
    {
        EnsureContextValid();
        // Arrange an active render pass holding texture/framebuffer borrows and boundary authority.
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateSingle(texture)!;
        lifetime.Attach(() => { target.Dispose(); texture.Dispose(); });
        var cache = OpenGlStateCache.Current;
        var coverage = PipelineStateCoverage.FullGraphics.Union(new(clearColor: true));
        var commands=RHICommandContext.Current;
        var boundary=commands.EnterBoundary(new("RetirementPass",true,operations:BoundaryOperations.RenderPasses));
        commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target));
        try
        {
            // The owner keeps attachments alive until irreversible context retirement abandons the pass.
            Assert.True(texture.IsValid);
            // Act by retiring the context while its render pass and boundary are still active.
            RenderContextRegistry.Retire(fixture);
            // Assert target borrows and boundary authority are abandoned; a replacement engine boundary can then begin.
            Assert.True(lifetime.IsClosed); Assert.True(registry.IsClosed);
            Assert.False(target.IsValid); Assert.False(texture.IsValid);
            Assert.Throws<InvalidOperationException>(boundary.Dispose);
            fixture.RestoreContextRegistration();
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("ReplacementPass", coverage), out var replacement));
            replacement!.Dispose();
        }
        finally
        {
            fixture.RestoreContextRegistration();
            lifetime.Shutdown();
        }
    }

    /// <summary>An empty active boundary rejects reload and ordinary shutdown before any readiness or epoch changes.</summary>
    [Fact]
    public void EmptyBoundaryRejectsReloadAndOrdinaryShutdownBeforeMutation()
    {
        EnsureContextValid();
        // Arrange a live client lifetime with a disposal callback and record its starting epoch.
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        lifetime.Attach(registry.Dispose);
        long epoch = lifetime.Epoch;
        try
        {
            // Act by attempting both native reload and ordinary shutdown inside an empty active engine boundary.
            Assert.True(OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("EmptyReloadBoundary",
                new PipelineStateCoverage(depth: DepthStateKnowledge.TestEnabled)), out var boundary));
            using (boundary!)
            {
                // Assert both operations reject before changing admission, closed state or the lifetime epoch.
                Assert.Throws<InvalidOperationException>(ClientRenderingLifetime.BeginNativeReload);
                Assert.Throws<InvalidOperationException>(lifetime.Shutdown);
                Assert.False(lifetime.Reloading); Assert.False(lifetime.IsClosed); Assert.Equal(epoch, lifetime.Epoch);
            }
        }
        finally { lifetime.Shutdown(); }
    }

    /// <summary>Feature compute scopes and replay reject closed client admission before resolving any native binding.</summary>
    [Fact]
    public void ClosedComputeAdmissionRejectsScopeCaptureAndReplay()
    {
        EnsureContextValid();
        // Arrange a ready compute owner and invalidate cached program state during shutdown.
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var compute = new ShaderRegistryComputeOwner(ComputeShaderAsset.DeclareFromAssets(assets.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"))));
        Assert.True(compute.EnsureReady());
        lifetime.Attach(() =>
        {
            var cache = OpenGlStateCache.Current; cache.Invalidate(EPipelineState.Program);
            Assert.False(compute.EnsureReady());
            Assert.Throws<InvalidOperationException>(() => compute.CaptureBindings());
            Assert.False(cache.TryGetCachedCurrentProgram(out _));
            compute.Dispose();
        });
        // Act by closing lifetime admission and then probing compute readiness and binding capture.
        lifetime.Shutdown();
        // Assert retirement is recorded and the closed registry stays closed after both rejected probes.
        Assert.True(((IGpuProgram)compute).Lifetime.IsRetired);
        Assert.True(registry.IsClosed);
    }
    #endregion
    #endregion
}
