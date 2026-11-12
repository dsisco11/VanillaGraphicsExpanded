using System.Collections.Immutable;
using System.Numerics;
using System.Threading;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
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
            renderer.OnRenderFrame(0, EnumRenderStage.Done);
            var changed = Settings();
            changed.Pages.TileColumns = 3;
            renderer.ApplyPolicy(changed);
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
    #endregion

    #region Private
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

    /// <summary>Returns a pending completion to exercise nonblocking retirement without sleeping or replacing the fence.</summary>
    private static bool KeepNextFencePending(ref CompletionStatus __result)
    {
        if (Interlocked.Decrement(ref pendingFencePolls) < 0) return true;
        __result = CompletionStatus.Pending;
        return false;
    }

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
}
