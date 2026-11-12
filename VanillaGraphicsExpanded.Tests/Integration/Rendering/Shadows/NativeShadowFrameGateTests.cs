using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises current-frame orchestration and retained fallback with a real bounded GPU page/candidate path.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowFrameGateTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    /// <summary>Retains the GPU fixture for initialization while the base class owns render assertions.</summary>
    public NativeShadowFrameGateTests(HeadlessGLFixture fixture) : base(fixture) => this.fixture = fixture;

    #region Public API
    /// <summary>Incomplete scene inputs keep complete prior sun and moon pages stale, then expire both together.</summary>
    [Fact]
    public void GatePublishesRetainedLightsWhenSceneIsUnavailableAndWithdrawsAtExpiry()
    {
        // Arrange: build two real current page certificates and the matching native world/candidate ownership.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings();
        using var pages = ShadowPagePool.Create(assets.Api, settings.Pages);
        using var worldBuffers = new ShadowWorldGpuBuffers();
        using var candidates = ShadowCandidatePool.Create(assets.Api, settings.Candidates);
        var world = World();
        Assert.True(worldBuffers.Upload(world));
        var changes = new ShadowSceneChanges(8);
        var sceneChanges = changes.Capture(0, 1);
        var candidateChanges = new ShadowCandidateChanges(1, 0, 0, true, []);
        var sun = Source(ShadowSourceKind.Sun, 81);
        var moon = Source(ShadowSourceKind.Moon, 82);
        var sunPage = DemandPage(sun, 5, 5, Vector3d.UnitY, Vector3d.UnitY);
        var moonPage = DemandPage(moon, 5, 5, Vector3d.UnitY, Vector3d.UnitY);
        Assert.True(pages.BeginBatch());
        Assert.True(pages.Execute(sunPage.Request with { Operation = ShadowPageOperation.Allocate }));
        long sunToken = Assignment(PageResult(pages));
        Assert.True(pages.Execute(moonPage.Request with { Operation = ShadowPageOperation.Allocate }));
        long moonToken = Assignment(PageResult(pages));
        Assert.True(sunToken > 0 && moonToken > 0);
        Assert.True(pages.FreezeWritableAssignments());
        Assert.True(pages.SelectReplacement(sunPage.Request));
        Assert.Equal(0u, PageResult(pages)[0]);
        Assert.True(pages.ClearReplacement());
        Assert.True(pages.SelectReplacement(moonPage.Request));
        Assert.Equal(0u, PageResult(pages)[0]);
        Assert.True(pages.ClearReplacement());
        Assert.True(pages.Execute(sunPage.Request with
        { Operation = ShadowPageOperation.Publish, Assignment = sunToken, Complete = true }));
        Assert.True(pages.Execute(moonPage.Request with
        { Operation = ShadowPageOperation.Publish, Assignment = moonToken, Complete = true }));
        Assert.Equal(0u, PageResult(pages)[0]);
        pages.SealBatch();

        // The gate only checks this linked capability contract; the test does not submit a surface draw.
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 10 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        using var gate = new NativeShadowFrameGate(assets.Api, settings, pages);
        var sourceLights = ImmutableArray.Create(
            FrameLight(sun, sunPage, retained: []), FrameLight(moon, moonPage, retained: []));

        // Act: publish a fully current frame, then feed a bounded source snapshot with the scene unavailable.
        var firstBudget = BeginBudget(gate, worldBuffers, candidates);
        Assert.True(candidates.BeginBatch(candidateChanges, world, worldBuffers, firstBudget));
        gate.Update(Input(Lights(1, sun, moon), world, worldBuffers, candidates, changes, sceneChanges,
            sourceLights, surface, currentInputsComplete: true), now: 0.1, firstBudget);
        var firstReceipt = Readiness(gate, 2);
        Assert.True(pages.CanSubmit);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);

        // A rejected current-source interval is published as unavailable without erasing source history.
        gate.RejectFrame("Controlled incomplete frame", Lights(2, sun, moon), now: 0.15);
        var rejectedReceipt = Readiness(gate, 2);
        Assert.Equal(0u, rejectedReceipt[12]);
        Assert.Equal(1u, rejectedReceipt[13]);

        var nearbySun = DemandPage(sun, 6, 6, new(0.001, 1, 0), new(0.001, 1, 0));
        var retainedSun = DemandPage(sun, 5, 6, Vector3d.UnitY, new(0.001, 1, 0));
        var nearbyMoon = DemandPage(moon, 6, 6, new(-0.001, 1, 0), new(-0.001, 1, 0));
        var retainedMoon = DemandPage(moon, 5, 6, Vector3d.UnitY, new(-0.001, 1, 0));
        var retainedLights = ImmutableArray.Create(
            FrameLight(sun, nearbySun, [retainedSun]), FrameLight(moon, nearbyMoon, [retainedMoon]));
        // Restore the same current projection after the explicit unavailable interval.
        var recoveryBudget = BeginBudget(gate, worldBuffers, candidates);
        Assert.True(candidates.BeginBatch(candidateChanges, world, worldBuffers, recoveryBudget));
        gate.Update(Input(Lights(3, sun, moon), world, worldBuffers, candidates, changes, sceneChanges,
            sourceLights, surface, currentInputsComplete: true), now: 0.18, recoveryBudget);
        var recoveredReceipt = Readiness(gate, 2);
        Assert.Equal(1u, recoveredReceipt[12]);
        Assert.Equal(1u, recoveredReceipt[32 + 12]);
        Assert.Equal(3u, recoveredReceipt[27]);
        Assert.Equal(2u, recoveredReceipt[28]);
        Assert.Equal(1u, recoveredReceipt[30]);
        gate.EndFrame();

        var secondBudget = BeginBudget(gate, worldBuffers, candidates);
        Assert.True(candidates.BeginBatch(candidateChanges, world, worldBuffers, secondBudget));
        gate.Update(Input(Lights(4, sun, moon), world, worldBuffers, candidates, changes, sceneChanges,
            retainedLights, surface, currentInputsComplete: false), now: 0.2, secondBudget);
        var staleReceipt = Readiness(gate, 2);
        Assert.True(pages.CanSubmit);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);

        // Act: leave both sources without current scene production until their common stale interval expires.
        var thirdBudget = BeginBudget(gate, worldBuffers, candidates);
        Assert.True(candidates.BeginBatch(candidateChanges, world, worldBuffers, thirdBudget));
        gate.Update(Input(Lights(5, sun, moon), world, worldBuffers, candidates, changes, sceneChanges,
            retainedLights, surface, currentInputsComplete: false), now: 0.31, thirdBudget);
        var expiredReceipt = Readiness(gate, 2);
        Assert.True(pages.CanSubmit);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);

        // Assert: the gate publishes both current lights, retains their whole prior mappings, then withdraws expiry.
        Assert.Equal(1u, firstReceipt[12]);
        Assert.Equal(1u, firstReceipt[32 + 12]);
        Assert.Equal(2u, staleReceipt[12]);
        Assert.Equal(2u, staleReceipt[32 + 12]);
        Assert.Equal(1u, staleReceipt[23]);
        Assert.Equal(1u, staleReceipt[32 + 23]);
        Assert.Equal(0u, expiredReceipt[12]);
        Assert.Equal(0u, expiredReceipt[32 + 12]);
    }
    #endregion

    #region Private
    /// <summary>Configures tiny real GPU owners while retaining nonzero recovery and stale-age checks.</summary>
    private static ShadowSettings Settings() => new()
    {
        Scheduling = new ShadowSchedulingSettings
        {
            Enabled = true, MemoryMiB = 32, LightCapacity = 2, DemandRequests = 16,
            CoverageChecks = 16384, RecoveryCompleteFrames = 1, GpuTargetMilliseconds = 100,
            DirectionalMemoryShare = 1, DirectionalWorkShare = 1, MaxShadowedLocalLights = 0
        },
        Candidates = new ShadowCandidateSettings
        {
            MemoryMiB = 4, HeaderCapacity = 1, SegmentsPerAssociation = 1,
            DedupCapacity = 128, ProbeLimit = 128, ChangeCapacity = 8
        },
        Pages = new ShadowPageSettings
        {
            MemoryMiB = 1, PageSize = 8, TileColumns = 4, TileRows = 1,
            ArrayLayers = 1, ProbeLimit = 128, OperationsPerBatch = 128
        },
        Temporal = new ShadowTemporalSettings
        { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 1 }
    };

    /// <summary>Creates a minimal valid power-of-two world table that the candidate owner can bind safely.</summary>
    private static ShadowWorldPublication World() => new(1, true, null,
        [default], [], ImmutableDictionary<int, ShadowBounds>.Empty, 1) { Owner = 901 };

    /// <summary>Builds one stable celestial identity associated with the test world's dimension.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long owner) => new(11, 2, kind, owner, owner + 100, 0);

    /// <summary>Creates one page key in a rendered projection and independently supplied current-light projection.</summary>
    private static ShadowDemandPage DemandPage(ShadowSourceKey source, long renderedGeneration, long currentGeneration,
        Vector3d renderedDirection, Vector3d currentDirection)
    {
        var bounds = new ShadowBounds(-100, -100, -100, 100, 100, 100);
        var renderedProjection = new DirectionalShadowProjection(source, renderedGeneration, renderedDirection,
            bounds, 1, pageSize: 8);
        var currentProjection = new DirectionalShadowProjection(source, currentGeneration, currentDirection,
            bounds, 1, pageSize: 8);
        var request = new ShadowPageRequest(ShadowPageOperation.Require,
            new(source, renderedGeneration, 0, 0, 0), new(-1, -1, 2, 1, 1, 3), 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(renderedProjection, 0));
        return new(request, ShadowProjectionSnapshot.Directional(currentProjection, 0));
    }

    /// <summary>Combines the command receipt's unsigned words into its native assignment token.</summary>
    private static long Assignment(uint[] words) => unchecked((long)(((ulong)words[3] << 32) | words[2]));

    /// <summary>Builds a no-scene light offer whose retained generation remains available to the readiness reducer.</summary>
    private static ShadowFrameLight FrameLight(ShadowSourceKey source, ShadowDemandPage current,
        ImmutableArray<ShadowDemandPage> retained)
    {
        var alternative = new ShadowFrameAlternative(0, true, [current], [], [], 0.1, retained);
        return new(source, 1, false, true, [alternative], current.Current);
    }

    /// <summary>Starts a new frame ledger with world and candidate resident bytes already included.</summary>
    private static ShadowFrameBudget BeginBudget(NativeShadowFrameGate gate, ShadowWorldGpuBuffers world,
        ShadowCandidatePool candidates) => gate.BeginFrame(checked(world.AllocatedBytes + candidates.AllocatedBytes));

    /// <summary>Creates consistent light snapshots without requiring native light collectors in this direct gate test.</summary>
    private static ShadowLightSnapshot Lights(long frame, ShadowSourceKey sun, ShadowSourceKey moon) => new(frame, 11, 2,
        [], new(sun, Vector3.UnitY, Vector3.One), new(moon, Vector3.UnitY, Vector3.One),
        ImmutableArray.CreateRange(new double[16]), 0, 0, 0, Vector3.Zero);

    /// <summary>Builds the controlled incomplete-scene frame while preserving all other gate prerequisites.</summary>
    private static NativeShadowFrameInput Input(ShadowLightSnapshot lights, ShadowWorldPublication world,
        ShadowWorldGpuBuffers worldBuffers, ShadowCandidatePool candidates, ShadowSceneChanges changes,
        ShadowChangeBatch sceneChanges, ImmutableArray<ShadowFrameLight> sources, ShaderProgram surface,
        bool currentInputsComplete) => new(lights, null, world, worldBuffers, candidates, changes, sceneChanges, sources,
        [surface], null!, (_, _) => null!, null!, null!, 0.016f, 0, 0, 0, currentInputsComplete);

    /// <summary>Reads only the fixed 32-word-per-light result receipt before the consumer boundary.</summary>
    private static uint[] Readiness(NativeShadowFrameGate gate, int lights)
    {
        using var mapped = gate.Readiness.MapRange<uint>(0, lights * 32, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }

    /// <summary>Reads the fixed eight-word command receipt from the RHI page table.</summary>
    private static uint[] PageResult(ShadowPagePool pages)
    {
        using var mapped = pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }
    #endregion
}
