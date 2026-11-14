using System.Collections.Immutable;
using System.Numerics;
using System.Threading;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises real RHI generation owners while controlling only the native frame-source boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowFrameLifecycleGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static int pendingFencePolls;

    #region Public API
    /// <summary>Reconfiguration waits for the final owner fence, while disable withdraws readiness without replacement.</summary>
    [Fact]
    public void PolicyReplacementAndDisableDrainRealGpuGenerationsBeforeRelease()
    {
        // Arrange: create genuine RHI page, readiness, world, and candidate owners behind one controlled source seam.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 101 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var events = new Mock<IClientEventAPI>();
        var client = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        client.SetupGet(value => value.Event).Returns(events.Object);
        var generations = new List<ShadowFrameInstance>();
        int creations = 0;
        long currentFrame = 0;
        ShadowFrameInstance Create(ShadowSettings requested)
        {
            creations++;
            var pages = ShadowPagePool.Create(assets.Api, requested.Pages);
            var gate = new NativeShadowFrameGate(assets.Api, requested, pages);
            var source = new ControlledFrameSource(assets.Api, requested, surface);
            var generation = new ShadowFrameInstance(source, gate, pages);
            generations.Add(generation);
            return generation;
        }
        using var renderer = new ShadowFrameRenderer(client.Object, Create, Settings(), () => 0.1,
            () => Snapshot(checked(++currentFrame)));
        var harmony = new Harmony("Tests.ShadowFrameLifecycle.PendingFence");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "WaitCpu"),
                prefix: new HarmonyMethod(typeof(ShadowFrameLifecycleGpuTests), nameof(KeepNextFencePending)));

            // Act: publish a usable first generation, then request a changed physical page capacity.
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            Assert.Equal(1, creations);
            var first = Assert.Single(generations);
            Assert.Equal(2, first.Pages.Capacity);
            Assert.NotNull(renderer.Current);
            var firstPublication = Assert.IsType<ShadowFramePublication>(first.Gate.Publication);
            using var retainedSurfaceInputs = new SurfaceShadowInputSet(firstPublication.Lights, firstPublication, null, first.Pages.Settings);
            Assert.True(retainedSurfaceInputs.IsCurrent);
            var changed = Settings();
            changed.Pages.TileColumns = 3;
            renderer.ApplyPolicy(changed);
            // A consumer may still hold the exact old publication, so invalidate its eligibility synchronously
            // while keeping the page owner available until the normal frame-end/drain sequence advances.
            Assert.False(retainedSurfaceInputs.IsCurrent);
            Assert.True(first.Pages.IsAvailable);
            renderer.OnRenderFrame(0, EnumRenderStage.Done);
            Interlocked.Exchange(ref pendingFencePolls, 100);
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);

            // Assert: publication withdraws immediately, but a pending real RHI fence retains the old owner.
            Assert.Null(renderer.Current);
            Assert.False(first.Pages.IsAvailable);
            Assert.False(((ControlledFrameSource)first.Source).IsDisposed);
            Assert.Equal(1, creations);

            // Act: let the real RHI complete the queued owners and let the next controlled frame install the replacement.
            Interlocked.Exchange(ref pendingFencePolls, -1);
            RHICommandContext.Current.WaitIdle();
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var second = Assert.Single(generations.Skip(1));

            // Assert: the retired source is disposed only after its final fence and the replacement becomes current.
            Assert.Equal(2, creations);
            Assert.Equal(3, second.Pages.Capacity);
            Assert.True(((ControlledFrameSource)first.Source).IsDisposed);
            Assert.NotNull(renderer.Current);
            renderer.OnRenderFrame(0, EnumRenderStage.Done);

            // Act: disabling the same system drains the second generation without constructing another one.
            var disabled = Settings();
            disabled.Pages.TileColumns = 3;
            disabled.Scheduling.Enabled = false;
            renderer.ApplyPolicy(disabled);
            Interlocked.Exchange(ref pendingFencePolls, 100);
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);

            // Assert: disable is immediately unavailable and keeps resources until RHI retirement completes.
            Assert.Null(renderer.Current);
            Assert.False(second.Pages.IsAvailable);
            Assert.False(((ControlledFrameSource)second.Source).IsDisposed);
            Assert.Throws<InvalidOperationException>(() => _ = second.Gate.Readiness);
            Assert.Equal(2, creations);
            Interlocked.Exchange(ref pendingFencePolls, -1);
            RHICommandContext.Current.WaitIdle();
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            Assert.True(((ControlledFrameSource)second.Source).IsDisposed);
            Assert.Null(renderer.Current);
            Assert.Equal(2, creations);
        }
        finally
        {
            Interlocked.Exchange(ref pendingFencePolls, -1);
            harmony.UnpatchAll(harmony.Id);
            renderer.Dispose();
            ShaderCapabilities.Forget(surface);
        }
    }

    /// <summary>LeaveWorld withdraws the retained publication, releases its generation, and permits a fresh world owner.</summary>
    [Fact]
    public void LeaveWorldEventWithdrawsRetainedPublicationAndAllowsFreshRenderer()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 101 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var events = new Mock<IClientEventAPI>();
        var client = CreateLifecycleClient(events, assets);
        var generations = new List<ShadowFrameInstance>();
        int creations = 0;
        long frame = 0;
        ShadowFrameInstance Create(ShadowSettings requested)
        {
            creations++;
            var generation = CreateGeneration(assets, requested, surface);
            generations.Add(generation);
            return generation;
        }

        ShadowFrameRenderer? renderer = null;
        ShadowFrameRenderer? nextWorld = null;
        try
        {
            renderer = new(client.Object, Create, Settings(), () => 0.1, () => Snapshot(checked(++frame)));
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var first = Assert.Single(generations);
            Assert.NotNull(renderer.Current);
            using var retainedInputs = CreateRetainedInputs(first);
            Assert.True(retainedInputs.IsCurrent);

            // Act: deliver the actual client LeaveWorld event registered by the frame owner.
            events.Raise(value => value.LeaveWorld += null);

            // Assert: withdrawal and owner disposal happen synchronously at the world boundary.
            Assert.Null(renderer.Current);
            Assert.False(retainedInputs.IsCurrent);
            Assert.False(first.Pages.IsAvailable);
            Assert.True(((ControlledFrameSource)first.Source).IsDisposed);
            Assert.Throws<InvalidOperationException>(() => _ = first.Gate.Readiness);
            events.Verify(value => value.UnregisterRenderer(renderer, EnumRenderStage.Opaque), Times.Once);
            DrainDeferredResources();

            // A new world installs a fresh renderer and allocates fresh page/readiness owners.
            nextWorld = new(client.Object, Create, Settings(), () => 0.1, () => Snapshot(checked(++frame)));
            nextWorld.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var replacement = Assert.Single(generations.Skip(1));
            Assert.NotSame(first, replacement);
            Assert.NotSame(first.Pages, replacement.Pages);
            Assert.NotNull(nextWorld.Current);
            Assert.True(replacement.Pages.IsAvailable);
            Assert.Equal(2, creations);
        }
        finally
        {
            nextWorld?.Dispose();
            renderer?.Dispose();
            ShaderCapabilities.Forget(surface);
        }
    }

    /// <summary>Render-context retirement withdraws retained inputs and a replacement context admits fresh owners.</summary>
    [Fact]
    public void ContextRetirementWithdrawsAndReplacementContextRecreatesGeneration()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 101 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var events = new Mock<IClientEventAPI>();
        var client = CreateLifecycleClient(events, assets);
        var generations = new List<ShadowFrameInstance>();
        int creations = 0;
        long frame = 0;
        ShadowFrameInstance Create(ShadowSettings requested)
        {
            creations++;
            var generation = CreateGeneration(assets, requested, surface);
            generations.Add(generation);
            return generation;
        }

        var originalContext = RenderContextRegistry.Current();
        ShadowFrameRenderer? renderer = null;
        ShadowFrameRenderer? replacementContext = null;
        try
        {
            renderer = new(client.Object, Create, Settings(), () => 0.1, () => Snapshot(checked(++frame)));
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var first = Assert.Single(generations);
            Assert.NotNull(renderer.Current);
            using var retainedInputs = CreateRetainedInputs(first);
            Assert.True(retainedInputs.IsCurrent);

            // Act: retire the actual fixture context registration; the renderer and its gate receive that event.
            RenderContextRegistry.Retire(fixture);

            // Assert: prior context publications and their physical resources are no longer usable.
            Assert.Equal(default, RenderContextRegistry.Current());
            Assert.Null(renderer.Current);
            Assert.False(retainedInputs.IsCurrent);
            Assert.False(first.Pages.IsAvailable);
            Assert.True(((ControlledFrameSource)first.Source).IsDisposed);
            Assert.Throws<InvalidOperationException>(() => _ = first.Gate.Readiness);

            fixture.RestoreContextRegistration();
            fixture.InitializeResourceDisposal();
            var newContext = RenderContextRegistry.Current();
            Assert.Equal(originalContext.Handle, newContext.Handle);
            Assert.NotEqual(originalContext.Generation, newContext.Generation);

            replacementContext = new(client.Object, Create, Settings(), () => 0.1, () => Snapshot(checked(++frame)));
            replacementContext.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var replacement = Assert.Single(generations.Skip(1));
            Assert.NotSame(first.Pages, replacement.Pages);
            Assert.True(replacement.Pages.IsAvailable);
            Assert.NotNull(replacementContext.Current);
            Assert.Equal(2, creations);
        }
        finally
        {
            if (RenderContextRegistry.Current() == default)
            {
                fixture.RestoreContextRegistration();
                fixture.InitializeResourceDisposal();
            }
            replacementContext?.Dispose();
            renderer?.Dispose();
            ShaderCapabilities.Forget(surface);
        }
    }

    /// <summary>Client reload callbacks withdraw an admitted publication before fenced generation recreation.</summary>
    [Fact]
    public void ClientRenderingReloadWithdrawsRetainedPublicationBeforeRecreation()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 101 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var events = new Mock<IClientEventAPI>();
        var client = CreateLifecycleClient(events, assets);
        var registry = GpuShaderProgramLibraries.Create(client.Object);
        var lifetime = ClientRenderingLifetime.Find(client.Object)!;
        var generations = new List<ShadowFrameInstance>();
        int creations = 0;
        long frame = 0;
        ShadowFrameInstance Create(ShadowSettings requested)
        {
            creations++;
            var generation = CreateGeneration(assets, requested, surface);
            generations.Add(generation);
            return generation;
        }

        ShadowFrameRenderer? renderer = null;
        lifetime.Attach(() => renderer?.Dispose());
        var harmony = new Harmony("Tests.ShadowFrameLifecycle.NativeReload");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "WaitCpu"),
                prefix: new HarmonyMethod(typeof(ShadowFrameLifecycleGpuTests), nameof(KeepNextFencePending)));
            renderer = new(client.Object, Create, Settings(), () => 0.1, () => Snapshot(checked(++frame)));
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            var first = Assert.Single(generations);
            Assert.NotNull(renderer.Current);
            using var retainedInputs = CreateRetainedInputs(first);
            Assert.True(retainedInputs.IsCurrent);

            // Act: invoke the client rendering lifetime's synchronous native-reload admission callbacks.
            ClientRenderingLifetime.BeginNativeReload();

            // Assert: eligibility is withdrawn before native reload completion, while owners remain until frame drain.
            Assert.True(lifetime.Reloading);
            Assert.Null(renderer.Current);
            Assert.Null(first.Gate.Publication);
            Assert.False(retainedInputs.IsCurrent);
            Assert.True(first.Pages.IsAvailable);
            Assert.False(((ControlledFrameSource)first.Source).IsDisposed);

            ClientRenderingLifetime.CompleteNativeReload();
            Assert.False(lifetime.Reloading);
            Interlocked.Exchange(ref pendingFencePolls, 100);
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);
            Assert.Null(renderer.Current);
            Assert.False(first.Pages.IsAvailable);
            Assert.False(((ControlledFrameSource)first.Source).IsDisposed);
            Interlocked.Exchange(ref pendingFencePolls, -1);
            RHICommandContext.Current.WaitIdle();
            renderer.OnRenderFrame(0.016f, EnumRenderStage.Opaque);

            // The renderer installs a distinct complete generation after the old one has drained.
            var replacement = Assert.Single(generations.Skip(1));
            Assert.NotSame(first.Pages, replacement.Pages);
            Assert.True(((ControlledFrameSource)first.Source).IsDisposed);
            Assert.True(replacement.Pages.IsAvailable);
            Assert.NotNull(renderer.Current);
            Assert.Equal(2, creations);
            DrainDeferredResources();
        }
        finally
        {
            Interlocked.Exchange(ref pendingFencePolls, -1);
            harmony.UnpatchAll(harmony.Id);
            lifetime.Shutdown();
            registry.Dispose();
            ShaderCapabilities.Forget(surface);
        }
    }
    #endregion

    #region Private
    #region Retirement helpers
    /// <summary>Waits for submitted lifecycle work, then drains resources retired by their owning generation.</summary>
    private static void DrainDeferredResources()
    {
        RHICommandContext.Current.WaitIdle();
        VanillaGraphicsExpanded.Rendering.RHIResourceManagerSystem.CurrentManager?.ResourceDisposals?.DrainPending();
    }
    #endregion

    #region Fixture construction and retained publication
    /// <summary>Connects the event mock to the real fixture asset and logger services used by the frame owner.</summary>
    private static Mock<ICoreClientAPI> CreateLifecycleClient(Mock<IClientEventAPI> events, BinaryShaderApiFixture assets)
    {
        var client = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        client.SetupGet(value => value.Event).Returns(events.Object);
        client.SetupGet(value => value.Assets).Returns(assets.Api.Assets);
        client.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        return client;
    }

    /// <summary>Creates real page, gate, and controlled source owners for an isolated lifecycle generation.</summary>
    private static ShadowFrameInstance CreateGeneration(BinaryShaderApiFixture assets, ShadowSettings settings,
        ShaderProgram surface)
    {
        var pages = ShadowPagePool.Create(assets.Api, settings.Pages);
        var gate = new NativeShadowFrameGate(assets.Api, settings, pages);
        var source = new ControlledFrameSource(assets.Api, settings, surface);
        return new(source, gate, pages);
    }

    /// <summary>Retains a borrowed input set for checking synchronous invalidation at a producer lifecycle boundary.</summary>
    private static SurfaceShadowInputSet CreateRetainedInputs(ShadowFrameInstance generation)
    {
        var publication = Assert.IsType<ShadowFramePublication>(generation.Gate.Publication);
        return new(publication.Lights, publication, null, generation.Pages.Settings);
    }

    /// <summary>Builds a compact valid allocation policy with enough memory for two non-overlapping generations.</summary>
    private static ShadowSettings Settings() => new()
    {
        Scheduling = new ShadowSchedulingSettings
        {
            Enabled = true, MemoryMiB = 16, LightCapacity = 2, DemandRequests = 16,
            CoverageChecks = 16384, RecoveryCompleteFrames = 1, GpuTargetMilliseconds = 100,
            DirectionalMemoryShare = 1, DirectionalWorkShare = 1, MaxShadowedLocalLights = 0,
            ProjectionHistoryCapacity = 2
        },
        Inputs = new ShadowInputSettings { ChunkCapacity = 1, RangeCapacity = 1, ChangeCapacity = 1 },
        Candidates = new ShadowCandidateSettings
        {
            MemoryMiB = 1, HeaderCapacity = 1, SegmentsPerAssociation = 1,
            DedupCapacity = 128, ProbeLimit = 128, ChangeCapacity = 8
        },
        Pages = new ShadowPageSettings
        {
            MemoryMiB = 1, PageSize = 8, TileColumns = 2, TileRows = 1,
            ArrayLayers = 1, ProbeLimit = 128, OperationsPerBatch = 128
        },
        Drawing = new ShadowDrawSettings { MemoryMiB = 1, CommandSlots = 64, ViewCapacity = 8 },
        Temporal = new ShadowTemporalSettings { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.1 }
    };

    /// <summary>Creates an immutable no-light snapshot so the controlled source can exercise real gate admission without drawing.</summary>
    private static ShadowLightSnapshot Snapshot(long frame) => new(frame, 11, 2, [], default, default,
        ImmutableArray.CreateRange(new double[16]), 0, 0, 0, Vector3.Zero);
    #endregion

    #region Fence retirement helper
    /// <summary>Returns a pending completion to exercise nonblocking retirement without sleeping or replacing the fence.</summary>
    private static bool KeepNextFencePending(ref CompletionStatus __result)
    {
        if (Interlocked.Decrement(ref pendingFencePolls) < 0) return true;
        __result = CompletionStatus.Pending;
        return false;
    }
    #endregion

    #region Controlled frame source
    /// <summary>Captures no native scene while still publishing exact current world/candidate GPU owners.</summary>
    private sealed class ControlledFrameSource : IShadowFrameSource
    {
        private readonly ShadowWorldGpuBuffers worldBuffers = new();
        private readonly ShadowCandidatePool candidates;
        private readonly ShadowSceneChanges changes = new(8);
        private readonly ShadowCandidateChanges candidateChanges = new(1, 0, 0, true, []);
        private readonly ShadowWorldPublication world = new(1, true, null, [default], [],
            ImmutableDictionary<int, ShadowBounds>.Empty, 1) { Owner = 901 };
        private readonly ShaderProgram surface;
        private bool disposed;
        internal bool IsDisposed => disposed;
        public long LiveAndRetiringBytes => checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes);

        /// <summary>Creates real bounded source owners, with native geometry deliberately outside this lifecycle test.</summary>
        internal ControlledFrameSource(Vintagestory.API.Common.ICoreAPI api, ShadowSettings settings,
            ShaderProgram surface)
        {
            this.surface = surface;
            candidates = ShadowCandidatePool.Create(api, settings.Candidates);
            if (!worldBuffers.Upload(world)) throw new InvalidOperationException("The test world publication was rejected.");
        }

        /// <summary>Opens the exact frame budgeted candidate owner and supplies a bounded no-scene gate input.</summary>
        public NativeShadowFrameInput Capture(ShadowLightSnapshot lights, float deltaTime, ShadowFrameBudget budget)
        {
            if (!candidates.BeginBatch(candidateChanges, world, worldBuffers, budget))
                throw new InvalidOperationException("The controlled candidate batch could not begin.");
            var sceneChanges = changes.Capture(0, 1);
            return new(lights, null, world, worldBuffers, candidates, changes, sceneChanges, [], [surface], null!,
                (_, _) => null!, null!, null!, deltaTime, 0, 0, 0, CurrentInputsComplete: false);
        }

        /// <summary>Releases the independently owned real candidate and world buffers after renderer fence retirement.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; candidates.Dispose(); worldBuffers.Dispose();
        }
    }
    #endregion
    #endregion
}
