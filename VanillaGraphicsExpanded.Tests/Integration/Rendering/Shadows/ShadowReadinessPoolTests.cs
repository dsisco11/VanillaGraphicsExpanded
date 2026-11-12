using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks whole-light readiness and independent source outcomes through compact GPU result records.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowReadinessPoolTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    /// <summary>Retains the GPU fixture for its cleanup seam while the base class owns render assertions.</summary>
    public ShadowReadinessPoolTests(HeadlessGLFixture fixture) : base(fixture) => this.fixture = fixture;

    #region Public API
    /// <summary>Missing moon coverage does not suppress a simultaneously current sun shadow.</summary>
    [Fact]
    public void SunAndMoonReadinessAreReducedIndependently()
    {
        // Arrange: publish one small current sun page and leave the moon's otherwise complete request unmapped.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var policy = new ShadowSchedulingSettings
        { Enabled = true, MemoryMiB = 16, LightCapacity = 2, DemandRequests = 8, RecoveryCompleteFrames = 2 };
        var temporal = new ShadowTemporalSettings
        { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.25, MaxDirectionalAngleDegrees = 1 };
        using var pages = ShadowPagePool.Create(assets.Api, PageSettings());
        using var readiness = new ShadowReadinessPool(assets.Api, policy, temporal);
        var sun = Source(ShadowSourceKind.Sun, 61);
        var moon = Source(ShadowSourceKind.Moon, 62);
        var sunPage = DemandPage(sun);
        var moonPage = DemandPage(moon);
        Assert.True(pages.BeginBatch());
        Publish(pages, sunPage.Request);
        var lights = new[]
        {
            new ShadowLightDemand(sun, 0, Selected: true, Complete: true, Pages: [sunPage]),
            new ShadowLightDemand(moon, 0, Selected: true, Complete: true, Pages: [moonPage])
        };

        // Act: reduce both source-specific page sets in one ordered readiness dispatch.
        readiness.Publish(pages, frame: 1, now: 0.1, lights, capabilitiesCurrent: true);
        var receipt = Results(readiness, lightCount: lights.Length);

        // Assert: a missing moon page is independent, while the current sun begins its configured recovery streak.
        Assert.Equal(0u, receipt[12]);
        Assert.Equal(3u, receipt[13]);
        Assert.Equal(0u, receipt[32 + 12]);
        Assert.Equal(4u, receipt[32 + 13]);

        // Act: swap collector order on the second consecutive frame before capability withdrawal.
        var reorderedLights = lights.Reverse().ToArray();
        readiness.Publish(pages, frame: 2, now: 0.2, reorderedLights, capabilitiesCurrent: true);
        receipt = Results(readiness, lightCount: lights.Length);

        // Assert: source-keyed recovery follows the sun across collector reorder; the missing moon remains unavailable.
        Assert.Equal(0u, receipt[12]);
        Assert.Equal(1u, receipt[32 + 12]);
        Assert.Equal(4u, receipt[13]);

        // Act: withdraw capabilities on a subsequent frame while retaining the same otherwise-current pages.
        readiness.Publish(pages, frame: 3, now: 0.3, lights, capabilitiesCurrent: false);
        receipt = Results(readiness, lightCount: lights.Length);

        // Assert: capability loss withdraws both publications before consumers can use prior readiness.
        Assert.Equal(0u, receipt[12]);
        Assert.Equal(13u, receipt[13]);
        Assert.Equal(0u, receipt[32 + 12]);
        Assert.Equal(13u, receipt[32 + 13]);
        pages.SealBatch();
    }

    /// <summary>Only completed, current GPU readiness feedback creates a bounded unchanged-input scheduler hint.</summary>
    [Fact]
    public void PriorityFeedbackRequiresObservedReadyStatusUnchangedFingerprintAndLiveDeadline()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var policy = new ShadowSchedulingSettings
        { Enabled = true, MemoryMiB = 16, LightCapacity = 1, DemandRequests = 8, RecoveryCompleteFrames = 1 };
        var temporal = new ShadowTemporalSettings { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.25 };
        using var pages = ShadowPagePool.Create(assets.Api, PageSettings());
        using var readiness = new ShadowReadinessPool(assets.Api, policy, temporal);
        using var feedback = new ShadowPriorityFeedback(capacity: 1, temporal: temporal);
        var sun = Source(ShadowSourceKind.Sun, 67);
        var page = DemandPage(sun);
        var frameLight = FrameLight(sun, page);
        string signature = ShadowPriorityFeedback.Fingerprint(frameLight, FingerprintBudget());
        string reconstructedSignature = ShadowPriorityFeedback.Fingerprint(FrameLight(sun, DemandPage(sun)), FingerprintBudget());
        string changedReceiver = ShadowPriorityFeedback.Fingerprint(FrameLight(sun, page with
            { Request = page.Request with { Region = new ShadowBounds(-2, -1, 2, 2, 1, 3) } }), FingerprintBudget());
        string changedDependency = ShadowPriorityFeedback.Fingerprint(FrameLight(sun, page with
            { Request = page.Request with { SceneSequence = page.Request.SceneSequence + 1 } }), FingerprintBudget());
        var changedProjectionPage = DemandPage(sun, currentGeneration: 6);
        string changedProjection = ShadowPriorityFeedback.Fingerprint(FrameLight(sun, changedProjectionPage), FingerprintBudget());
        Assert.Equal(signature, reconstructedSignature);
        Assert.NotEqual(signature, changedReceiver);
        Assert.NotEqual(signature, changedDependency);
        Assert.NotEqual(signature, changedProjection);
        Assert.False(feedback.IsEligible(sun, signature, now: 0.1));
        Assert.True(pages.BeginBatch());
        Publish(pages, page.Request);
        readiness.Publish(pages, frame: 1, now: 0.1,
            [new ShadowLightDemand(sun, 0, true, true, [page])], capabilitiesCurrent: true);

        // A pending observation captured before an input epoch change must not revive eligibility when it completes.
        feedback.Capture(readiness.Priority, [(sun, signature)], now: 0.1);
        feedback.Invalidate();
        RHICommandContext.Current.WaitIdle();
        feedback.Poll(now: 0.1);
        Assert.False(feedback.IsEligible(sun, signature, now: 0.1));

        // A fresh GPU observation permits only its exact receiver/dependency/projection fingerprint.
        readiness.Publish(pages, frame: 2, now: 0.2,
            [new ShadowLightDemand(sun, 0, true, true, [page])], capabilitiesCurrent: true);
        feedback.Capture(readiness.Priority, [(sun, signature)], now: 0.2);
        RHICommandContext.Current.WaitIdle();
        feedback.Poll(now: 0.2);
        Assert.True(feedback.IsEligible(sun, signature, now: 0.2));
        Assert.False(feedback.IsEligible(sun, changedReceiver, now: 0.2));
        Assert.False(feedback.IsEligible(sun, changedDependency, now: 0.2));
        Assert.False(feedback.IsEligible(sun, changedProjection, now: 0.2));

        // A later observation measures the original page as aged; it cannot restart the rendered-age deadline.
        readiness.Publish(pages, frame: 3, now: 0.8,
            [new ShadowLightDemand(sun, 0, true, true, [page])], capabilitiesCurrent: true);
        for (int sample = 0; sample < 4; sample++)
            feedback.Capture(readiness.Priority, [(sun, signature)], now: 0.8);
        RHICommandContext.Current.WaitIdle();
        feedback.Poll(now: 0.8);
        Assert.Equal(1, feedback.MissingSamples);
        Assert.True(feedback.IsEligible(sun, signature, now: 0.8));
        feedback.Poll(now: 1.01);
        Assert.False(feedback.IsEligible(sun, signature, now: 1.01));
        pages.SealBatch();
    }

    /// <summary>GPU diagnostics separate expired/missing containment, receiver overflow, and scheduler nonselection.</summary>
    [Fact]
    public void ReadinessReasonsDistinguishReceiverOverflowMissingAssignmentAndUnselectedDemand()
    {
        // Arrange: publish three complete page assignments and offer a fourth missing request to the GPU reducer.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var policy = new ShadowSchedulingSettings
        { Enabled = true, MemoryMiB = 16, LightCapacity = 4, DemandRequests = 8, RecoveryCompleteFrames = 1 };
        var temporal = new ShadowTemporalSettings { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.25 };
        var pagePolicy = PageSettings(); pagePolicy.TileColumns = 4;
        using var pages = ShadowPagePool.Create(assets.Api, pagePolicy);
        using var readiness = new ShadowReadinessPool(assets.Api, policy, temporal);
        var sun = Source(ShadowSourceKind.Sun, 63);
        var moon = Source(ShadowSourceKind.Moon, 64);
        // Directional projection helpers require celestial source identities; these are distinct valid sun/moon owners.
        var missingSource = Source(ShadowSourceKind.Sun, 65);
        var rejectedSource = Source(ShadowSourceKind.Moon, 66);
        var sunPage = DemandPage(sun);
        var moonPage = DemandPage(moon);
        var missingPage = DemandPage(missingSource);
        var rejectedPage = DemandPage(rejectedSource);
        Assert.True(pages.BeginBatch());
        PublishSet(pages, [sunPage.Request, moonPage.Request, rejectedPage.Request]);
        var outsideReceiver = moonPage with { Request = moonPage.Request with
            { Region = new ShadowBounds(-2, -1, 2, 2, 1, 3) } };
        var lights = new[]
        {
            new ShadowLightDemand(sun, 0, true, true, [sunPage]),
            new ShadowLightDemand(moon, 0, true, true, [outsideReceiver]),
            new ShadowLightDemand(missingSource, 0, true, true, [missingPage]),
            new ShadowLightDemand(rejectedSource, 0, false, true, [rejectedPage])
        };

        // Act: reduce all four bounded outcomes in one GPU dispatch and read only compact per-light receipts.
        readiness.Publish(pages, frame: 1, now: 0.1, lights, capabilitiesCurrent: true);
        var receipt = Results(readiness, lights.Length);

        // Assert: current coverage is ready; containment, absent assignment, and scheduler rejection retain separate causes.
        Assert.Equal(1u, receipt[12]); Assert.Equal(0u, receipt[13]);
        Assert.Equal(0u, receipt[32 + 12]); Assert.Equal(5u, receipt[32 + 13]);
        Assert.Equal(0u, receipt[64 + 12]); Assert.Equal(4u, receipt[64 + 13]);
        Assert.Equal(0u, receipt[96 + 12]); Assert.Equal(12u, receipt[96 + 13]);
        pages.SealBatch();
    }

    /// <summary>A complete old generation can serve a nearby moving sun only within its original stale deadline.</summary>
    [Fact]
    public void RetainedGenerationRemainsUsableUntilBoundedExpiry()
    {
        // Arrange: publish one current sun page, then retain it when the next projection generation is unavailable.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var policy = new ShadowSchedulingSettings
        { Enabled = true, MemoryMiB = 16, LightCapacity = 1, DemandRequests = 8, RecoveryCompleteFrames = 1 };
        var temporal = new ShadowTemporalSettings
        { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 1 };
        using var pages = ShadowPagePool.Create(assets.Api, PageSettings());
        using var readiness = new ShadowReadinessPool(assets.Api, policy, temporal);
        var sun = Source(ShadowSourceKind.Sun, 71);
        var initial = DemandPage(sun);
        Assert.True(pages.BeginBatch());
        Publish(pages, initial.Request);
        readiness.Publish(pages, 1, 0.1, [new(sun, 0, true, true, [initial])], capabilitiesCurrent: true);
        Assert.Equal(1u, Results(readiness, 1)[12]);

        // A mapping-generation change records its first invalidation time without replacing the usable old depth.
        var invalidate = initial.Request with
        { Operation = ShadowPageOperation.ValidateCurrent, CurrentProjection = 6, Complete = true, Timestamp = 0.15 };
        Assert.True(pages.Execute(invalidate));
        Assert.Equal(0u, Results(pages)[0]);
        var nearbyDirection = new Vector3d(0.001, 1, 0);
        var current = DemandPage(sun, renderedGeneration: 6, currentGeneration: 6,
            renderedTowardLight: nearbyDirection, currentTowardLight: nearbyDirection);
        var retained = DemandPage(sun, renderedGeneration: 5, currentGeneration: 6,
            renderedTowardLight: Vector3d.UnitY, currentTowardLight: nearbyDirection);
        var demand = new ShadowLightDemand(sun, 0, true, true, [current], [retained]);

        // Act: evaluate the retained page inside the configured stale interval, then after that interval expires.
        readiness.Publish(pages, 2, 0.2, [demand], capabilitiesCurrent: true);
        var stale = Results(readiness, 1);
        readiness.Publish(pages, 3, 0.27, [demand], capabilitiesCurrent: true);
        var expired = Results(readiness, 1);

        // Assert: only the old whole generation is selected as stale, then withdrawn at its original deadline.
        Assert.Equal(2u, stale[12]);
        Assert.Equal(1u, stale[16]);
        Assert.Equal(1u, stale[21]);
        Assert.Equal(1u, stale[23]);
        Assert.Equal(0u, expired[12]);
        Assert.Equal(9u, expired[13]);
        pages.SealBatch();
    }

    /// <summary>Whole coarse coverage stays current while a missing fine generation recovers without an availability gap.</summary>
    [Fact]
    public void CoarseGenerationRemainsReadyUntilFineGenerationCompletesRecovery()
    {
        // Arrange: publish a recovered coarse page, then request a distinct finer mapping with coarse as fallback.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var policy = new ShadowSchedulingSettings
        { Enabled = true, MemoryMiB = 16, LightCapacity = 1, DemandRequests = 8, RecoveryCompleteFrames = 3 };
        var temporal = new ShadowTemporalSettings
        { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0.25, MaxDirectionalAngleDegrees = 1 };
        using var pages = ShadowPagePool.Create(assets.Api, PageSettings());
        using var readiness = new ShadowReadinessPool(assets.Api, policy, temporal);
        var sun = Source(ShadowSourceKind.Sun, 91);
        var coarse = DemandPage(sun, renderedGeneration: 5, currentGeneration: 5, detail: 0);
        var fine = DemandPage(sun, renderedGeneration: 6, currentGeneration: 6, detail: 1);

        // Act and assert: recover the complete coarse certificate before asking the scheduler for fine detail.
        Assert.True(pages.BeginBatch());
        Publish(pages, coarse.Request);
        readiness.Publish(pages, 1, 0.1, [new(sun, 0, true, true, [coarse])], capabilitiesCurrent: true);
        Assert.Equal(0u, Results(readiness, 1)[12]);
        NextBatch(pages);
        readiness.Publish(pages, 2, 0.2, [new(sun, 0, true, true, [coarse])], capabilitiesCurrent: true);
        Assert.Equal(0u, Results(readiness, 1)[12]);
        NextBatch(pages);
        readiness.Publish(pages, 3, 0.3, [new(sun, 0, true, true, [coarse])], capabilitiesCurrent: true);
        var coarseReady = Results(readiness, 1);
        Assert.Equal(1u, coarseReady[12]);
        Assert.Equal(0u, coarseReady[14]);

        // An unavailable fine page must select the already-current whole coarse set without withdrawing readiness.
        NextBatch(pages);
        readiness.Publish(pages, 4, 0.4,
            [new ShadowLightDemand(sun, 1, true, true, [fine], [coarse])], capabilitiesCurrent: true);
        var missingFine = Results(readiness, 1);
        Assert.Equal(1u, missingFine[12]);
        Assert.Equal(0u, missingFine[14]);

        // Act: publish fine coverage and keep presenting complete fine plus coarse alternatives during recovery.
        NextBatch(pages);
        Publish(pages, fine.Request);
        var alternatives = new[] { new ShadowLightDemand(sun, 1, true, true, [fine], [coarse]) };
        readiness.Publish(pages, 5, 0.5, alternatives, capabilitiesCurrent: true);
        var firstFineFrame = Results(readiness, 1);
        NextBatch(pages);
        readiness.Publish(pages, 6, 0.6, alternatives, capabilitiesCurrent: true);
        var secondFineFrame = Results(readiness, 1);
        NextBatch(pages);
        readiness.Publish(pages, 7, 0.7, alternatives, capabilitiesCurrent: true);
        var recoveredFine = Results(readiness, 1);

        // Assert: coarse remains current through every recovery frame, then fine replaces it without a missing frame.
        Assert.Equal(1u, firstFineFrame[12]);
        Assert.Equal(0u, firstFineFrame[14]);
        Assert.Equal(1u, secondFineFrame[12]);
        Assert.Equal(0u, secondFineFrame[14]);
        Assert.Equal(1u, recoveredFine[12]);
        Assert.Equal(1u, recoveredFine[14]);
        pages.SealBatch();
    }
    #endregion

    #region Private
    /// <summary>Uses a bounded array and page table while retaining real GL 4.5 compute coverage logic.</summary>
    private static ShadowPageSettings PageSettings() => new()
    { MemoryMiB = 1, PageSize = 8, TileColumns = 2, TileRows = 1, ArrayLayers = 1, ProbeLimit = 128, OperationsPerBatch = 32 };

    /// <summary>Creates independent celestial identities with a common stable world lifetime.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long owner) => new(11, 2, kind, owner, owner + 100, 0);

    /// <summary>Builds a single depth-inclusive virtual page in the matching rendered and current projection.</summary>
    private static ShadowDemandPage DemandPage(ShadowSourceKey source, long renderedGeneration = 5,
        long currentGeneration = 5, Vector3d? renderedTowardLight = null, Vector3d? currentTowardLight = null,
        int detail = 0)
    {
        var key = new ShadowPageKey(source, renderedGeneration, 0, 0, 0);
        var bounds = new ShadowBounds(-100, -100, -100, 100, 100, 100);
        var projection = new DirectionalShadowProjection(source, renderedGeneration, renderedTowardLight ?? Vector3d.UnitY,
            bounds, 1, pageSize: 8);
        var currentProjection = new DirectionalShadowProjection(source, currentGeneration,
            currentTowardLight ?? renderedTowardLight ?? Vector3d.UnitY, bounds, 1, pageSize: 8);
        var snapshot = ShadowProjectionSnapshot.Directional(projection, 0);
        var current = ShadowProjectionSnapshot.Directional(currentProjection, 0);
        var region = new ShadowBounds(-1, -1, 2, 1, 1, 3);
        var request = new ShadowPageRequest(ShadowPageOperation.Require, key, region, 13, 17, 19,
            Projection: snapshot);
        return new(request, current, detail);
    }

    /// <summary>Builds the production fingerprint input from one complete current receiver demand.</summary>
    private static ShadowFrameLight FrameLight(ShadowSourceKey source, ShadowDemandPage page) => new(source, 10,
        false, true, [new ShadowFrameAlternative(0, true, [page], [], [], 1)]);

    /// <summary>Provides enough bounded input visits for one exact production fingerprint computation.</summary>
    private static ShadowFrameBudget FingerprintBudget() => new(new(Inputs: 1024), 0);

    /// <summary>Seals one consumer interval and starts the next after all compact RHI fence owners are retired.</summary>
    private static void NextBatch(ShadowPagePool pages)
    {
        pages.SealBatch();
        RHICommandContext.Current.WaitIdle();
        pages.Poll();
        Assert.True(pages.BeginBatch());
    }

    /// <summary>Creates one complete cleared assignment without any host image readback.</summary>
    private static void Publish(ShadowPagePool pages, ShadowPageRequest required)
    {
        var allocation = required with { Operation = ShadowPageOperation.Allocate, Timestamp = 0 };
        Assert.True(pages.Execute(allocation));
        var receipt = Results(pages);
        Assert.Equal(0u, receipt[0]);
        long assignment = unchecked((long)(((ulong)receipt[3] << 32) | receipt[2]));
        Assert.True(assignment > 0);
        Assert.True(pages.ClearReplacement());
        Assert.True(pages.FreezeWritableAssignments());
        Assert.True(pages.Execute(required with
        { Operation = ShadowPageOperation.Publish, Assignment = assignment, Complete = true, Timestamp = 0 }));
        Assert.Equal(0u, Results(pages)[0]);
    }

    /// <summary>Allocates and clears a small page set before publishing all assignments under one frozen write interval.</summary>
    private static void PublishSet(ShadowPagePool pages, ImmutableArray<ShadowPageRequest> required)
    {
        var assignments = ImmutableArray.CreateBuilder<long>(required.Length);
        foreach (var page in required)
        {
            Assert.True(pages.Execute(page with { Operation = ShadowPageOperation.Allocate, Timestamp = 0 }));
            var receipt = Results(pages);
            Assert.Equal(0u, receipt[0]);
            assignments.Add(unchecked((long)(((ulong)receipt[3] << 32) | receipt[2])));
            Assert.True(assignments[^1] > 0);
            Assert.True(pages.ClearReplacement());
        }
        Assert.True(pages.FreezeWritableAssignments());
        for (int index = 0; index < required.Length; index++)
        {
            Assert.True(pages.Execute(required[index] with
            { Operation = ShadowPageOperation.Publish, Assignment = assignments[index], Complete = true, Timestamp = 0 }));
            Assert.Equal(0u, Results(pages)[0]);
        }
    }

    /// <summary>Reads only the documented compact per-light status receipt, not the page pool or depth texture.</summary>
    private static uint[] Results(ShadowReadinessPool readiness, int lightCount)
    {
        using var mapped = readiness.Results.MapRange<uint>(0, lightCount * 32, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }

    /// <summary>Reads only the eight-word command receipt from the preceding page-table dispatch.</summary>
    private static uint[] Results(ShadowPagePool pages)
    {
        using var mapped = pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }
    #endregion
}
