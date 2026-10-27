using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Exercises registry identity, synchronous reload, retained compute inputs and terminal admission.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderRegistryLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Declarations and graphics reload
    /// <summary>Repeated declarations do not allocate owners, read binaries, reset edited settings or advance epochs.</summary>
    [Fact]
    public void IdenticalDeclarationPreservesOwnerAndPendingSettings()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        int factories = 0;
        var settings = new ShaderSettings(VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram.Contract);
        var descriptor = new GpuProgramDeclaration("vanillagraphicsexpanded", "test-composite", typeof(VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram), settings);
        var owner = registry.Declare(descriptor, () => { factories++; return new VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram(); });
        owner.EnableShortRangeAo = false;
        Assert.Same(owner, registry.Declare<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>(descriptor, () => { factories++; throw new InvalidOperationException(); }));
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

    /// <summary>Reload withdraws old readiness immediately and suppresses unchanged failures until another asset epoch.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadBlocksPreparationUntilCompletionAndRetainsFailureAllocation(bool failure)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        RasterDepthCopyShaderProgram.Register(assets.Api);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var owner = registry.Get<RasterDepthCopyShaderProgram>(RasterDepthCopyShaderProgram.Contract.Identity)!;
        Assert.True(owner.EnsureReady());
        int installed = owner.ProgramId;
        ulong revision = ((IGpuProgram)owner).ExecutableRevision;
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

    /// <summary>Borrow rejection validates all owners before withdrawing any registry readiness.</summary>
    [Fact]
    public void ReloadRejectsActiveBorrowWithoutPartialInvalidation()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        RasterDepthCopyShaderProgram.Register(assets.Api);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var owner = registry.Get<RasterDepthCopyShaderProgram>(RasterDepthCopyShaderProgram.Contract.Identity)!;
        using var depth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        owner.PrimaryDepth = depth;
        using (owner.UseScope())
        {
            Assert.Throws<InvalidOperationException>(registry.BeginReload);
            Assert.False(owner.RequiresPreparation);
            Assert.False(registry.IsReloading);
        }
    }

    #endregion
    #region Executable and client lifetime
    /// <summary>Feature compute replacement preserves CPU blocks and revisions through success, failure and retry.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputeReloadPreservesWrapperAndCpuInputs(bool failedReplacement)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var owner = new ComputeOwner(GpuComputePipeline.DeclareFromAssets(assets.Api, settings));
        Assert.True(owner.EnsureReady());
        var block = owner.Block;
        block.SetBytes(new byte[16]);
        int old = owner.ProgramId;
        ulong revision = ((IGpuProgram)owner).ExecutableRevision;
        using (owner.UseScope()) Assert.Throws<InvalidOperationException>(owner.InvalidateAssets);
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

    /// <summary>A closed registry retains its terminal identity and rejects late declarations or implicit recreation.</summary>
    [Fact]
    public void ClosedRegistryCannotReviveAfterWorldEpochChanges()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var context = RenderContextRegistry.Current();
        long before = registry.Epoch;
        registry.LeaveWorld(); Assert.True(registry.Epoch > before);
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
            var state = StateCache.Current;
            state.Invalidate(EPipelineState.Program);
            Assert.False(state.TryGetCachedCurrentProgram(out _));
            Assert.Throws<InvalidOperationException>(() => owner.UseScope());
            Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)owner).ReplayInputs());
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
        }
        finally
        {
            Vintagestory.Client.NoObf.ShaderRegistry.SupressShaderAndBufferReloads = suppressed;
            lifetime.Shutdown();
        }
    }
    /// <summary>A failed obsolete compute attempt cannot suppress changed assets encountered during its reads.</summary>
    [Fact]
    public void ComputeFailedSupersessionPermitsNewAssetAttempt()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var owner = new ComputeOwner(GpuComputePipeline.DeclareFromAssets(assets.Api, settings));
        Assert.True(owner.EnsureReady());
        owner.InvalidateAssets();
        bool changed = false;
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
        shader.InvalidateAssets();
        Assert.True(shader.EnsureReady());
        Assert.Same(parameters, ((IDepthHierarchyComputeBindings)shader).Parameters);
        Assert.Equal(revision + 1, ((IGpuProgram)shader).ExecutableRevision);
        Assert.False(GL.IsProgram(old));
        // Do not call Prepare again: extent, tail and image views must survive on the same generated wrapper.
        source.UploadDataImmediate(Enumerable.Repeat(.625f, 65 * 37).ToArray());
        hierarchy.Render(source);
        for (int level = 0; level < hierarchy.Texture!.MipLevels; level++)
            Assert.All(hierarchy.Texture.ReadPixels(level), value => Assert.Equal(.625f, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
    #region Context retirement and terminal admission
    /// <summary>Explicit retirement and liveness discovery consume existing scope borrows without restoring a retired context.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextRetirementClosesOutstandingScopes(bool discoveredDead)
    {
        EnsureContextValid();
        object contextOwner = new();
        bool alive = true;
        RenderContextRegistry.RegisterCurrent(contextOwner, _ => alive);
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var compute = new ComputeOwner(GpuComputePipeline.DeclareFromAssets(assets.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"))));
        IDisposable? scope = null; int id = 0;
        lifetime.Attach(() => { compute.Dispose(); registry.Dispose(); });
        try
        {
            scope = compute.UseScope(); id = compute.ProgramId;
            if (discoveredDead) { alive = false; Assert.Equal(default, RenderContextRegistry.Current()); }
            else RenderContextRegistry.Retire(contextOwner);
            Assert.True(lifetime.IsClosed); Assert.True(registry.IsClosed);
            Assert.True(((IGpuProgram)compute).Lifetime.IsRetired);
            Assert.Throws<InvalidOperationException>(scope.Dispose);
            lifetime.Shutdown();
        }
        finally
        {
            RenderContextRegistry.Retire(contextOwner);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            StateCache.Current.UseProgram(0);
            if (id != 0 && GL.IsProgram(id)) GL.DeleteProgram(id);
            lifetime.Shutdown();
        }
    }
    /// <summary>Irreversible retirement releases render-pass target borrows and boundary authority without restoring native state.</summary>
    [Fact]
    public void ContextRetirementAbandonsCompleteBoundaryAndPass()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateSingle(texture)!;
        lifetime.Attach(() => { target.Dispose(); texture.Dispose(); });
        var cache = StateCache.Current;
        var coverage = PipelineStateCoverage.FullGraphics.Union(new(clearColor: true));
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("RetirementPass", coverage), out var boundary));
        var pass = RenderPass.Begin(new(target, [new(0)]), boundary!);
        Assert.Throws<InvalidOperationException>(target.Dispose);
        try
        {
            RenderContextRegistry.Retire(fixture);
            Assert.True(lifetime.IsClosed); Assert.True(registry.IsClosed);
            Assert.False(target.IsValid); Assert.False(texture.IsValid);
            pass.Dispose(); boundary!.Dispose();
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("ReplacementPass", coverage), out var replacement));
            replacement!.Dispose();
        }
        finally
        {
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            lifetime.Shutdown();
        }
    }
    /// <summary>An empty active boundary rejects reload and ordinary shutdown before any readiness or epoch changes.</summary>
    [Fact]
    public void EmptyBoundaryRejectsReloadAndOrdinaryShutdownBeforeMutation()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        lifetime.Attach(registry.Dispose);
        long epoch = lifetime.Epoch;
        try
        {
            Assert.True(StateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("EmptyReloadBoundary",
                new PipelineStateCoverage(depth: DepthStateKnowledge.TestEnabled)), out var boundary));
            using (boundary!)
            {
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
        using var assets = new BinaryShaderApiFixture();
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        var lifetime = ClientRenderingLifetime.Find(assets.Api)!;
        using var compute = new ComputeOwner(GpuComputePipeline.DeclareFromAssets(assets.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"))));
        Assert.True(compute.EnsureReady());
        lifetime.Attach(() =>
        {
            var cache = StateCache.Current; cache.Invalidate(EPipelineState.Program);
            Assert.False(compute.EnsureReady());
            Assert.Throws<InvalidOperationException>(() => compute.UseScope());
            Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)compute).ReplayInputs());
            Assert.False(cache.TryGetCachedCurrentProgram(out _));
            compute.Dispose();
        });
        lifetime.Shutdown();
        Assert.True(((IGpuProgram)compute).Lifetime.IsRetired);
        Assert.True(registry.IsClosed);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Retains a CPU input block independently of replaceable native pipelines.</summary>
    private sealed class ComputeOwner : GpuComputeProgram
    {
        internal PackedUniformBuffer Block { get; }
        /// <summary>Adopts one feature-owned pipeline and CPU block.</summary>
        internal ComputeOwner(GpuComputePipeline pipeline) : base(pipeline) => Block = OwnUniformBuffer(new PackedUniformBuffer(16));
        /// <summary>Uses no resources during the lifecycle-only binding probe.</summary>
        protected override void Submit() { }
    }
    #endregion
}
