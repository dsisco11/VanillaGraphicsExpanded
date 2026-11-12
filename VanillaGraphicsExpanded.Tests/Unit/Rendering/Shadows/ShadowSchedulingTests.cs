using System.Collections.Immutable;
using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects whole-frame budget charging and stable light admission policy.</summary>
public sealed class ShadowSchedulingTests
{
    #region Public API
    /// <summary>Settings persist through the owning config and malformed work limits recover to finite values.</summary>
    [Fact]
    public void SchedulingSettingsPersistAndSanitize()
    {
        // Arrange: configure scheduler limits through the serialized owner, with one malformed work cap.
        var config = new VgeConfig();
        config.VirtualShadows.Scheduling = new ShadowSchedulingSettings
        {
            Enabled = true, MemoryMiB = 64, GpuTargetMilliseconds = 4,
            MaxShadowedLocalLights = 3, DirectionalMemoryShare = 0.6,
            ChallengerMargin = 0.35, DemandRequests = -1, RecoveryCompleteFrames = 5,
            PointCoverageHalfExtentMetres = new(3, 4, 5)
        };

        // Act: restore and sanitize using the same application configuration contract.
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        restored.Sanitize();
        var settings = restored.VirtualShadows.Scheduling.Freeze();

        // Assert: user values remain configurable while only the invalid field recovers.
        Assert.True(settings.Enabled);
        Assert.Equal(64, settings.MemoryMiB);
        Assert.Equal(4, settings.GpuTargetMilliseconds);
        Assert.Equal(3, settings.MaxShadowedLocalLights);
        Assert.Equal(0.6, settings.DirectionalMemoryShare);
        Assert.Equal(0.35, settings.ChallengerMargin);
        Assert.Equal(8192, settings.DemandRequests);
        Assert.Equal(5, settings.RecoveryCompleteFrames);
        Assert.Equal(new Vector3(3, 4, 5), settings.PointCoverageHalfExtentMetres);
        Assert.NotNull(settings.ValidationDiagnostic);
    }

    /// <summary>Point-source motion extents sanitize per axis and preserve a diagnostic for malformed inputs.</summary>
    [Fact]
    public void PointMotionEnvelopeRejectsInvalidAxisValuesWithDiagnostic()
    {
        // Arrange: make two axes invalid independently while retaining a valid third-axis envelope.
        var settings = new ShadowSchedulingSettings
        { PointCoverageHalfExtentMetres = new(float.NaN, 0, 6) };

        // Act: freeze the effective settings using the same boundary as light scheduling.
        var frozen = settings.Freeze();

        // Assert: the invalid components recover to finite defaults without discarding the user's valid Z extent.
        Assert.Equal(new Vector3(2, 2, 6), frozen.PointCoverageHalfExtentMetres);
        Assert.NotNull(frozen.ValidationDiagnostic);
    }

    /// <summary>Freezing a shadow generation isolates every nested persisted policy from later edits.</summary>
    [Fact]
    public void FrozenShadowPolicyDoesNotFollowMutableNestedSettings()
    {
        // Arrange: create one owner with values spread across the independently editable sub-policies.
        var requested = new ShadowSettings
        {
            Scheduling = new() { MemoryMiB = 48 },
            Inputs = new() { ChunkCapacity = 32 },
            Candidates = new() { HeaderCapacity = 4 },
            Pages = new() { TileColumns = 4 },
            Drawing = new() { CommandSlots = 64 },
            Temporal = new() { MaxRenderedAgeSeconds = 2, MaxStaleAgeSeconds = 0.75 }
        };

        // Act: freeze the generation, then mutate each source object that a live config editor can retain.
        var frozen = requested.Freeze();
        requested.Scheduling.MemoryMiB = 96;
        requested.Inputs.ChunkCapacity = 64;
        requested.Candidates.HeaderCapacity = 8;
        requested.Pages.TileColumns = 8;
        requested.Drawing.CommandSlots = 128;
        requested.Temporal.MaxStaleAgeSeconds = 1.5;

        // Assert: a queued or active generation retains its own copied policy graph.
        Assert.Equal(48, frozen.Scheduling.MemoryMiB);
        Assert.Equal(32, frozen.Inputs.ChunkCapacity);
        Assert.Equal(4, frozen.Candidates.HeaderCapacity);
        Assert.Equal(4, frozen.Pages.TileColumns);
        Assert.Equal(64, frozen.Drawing.CommandSlots);
        Assert.Equal(0.75, frozen.Temporal.MaxStaleAgeSeconds);
    }

    /// <summary>Whole-generation resource preflight includes both fixed storage and the independent drawing reserve.</summary>
    [Fact]
    public void ResourcePreflightChargesTheFullFixedAndDrawingReservationsBeforeAllocation()
    {
        // Arrange: constrain every capacity to a small valid layout with a one-MiB drawing reservation.
        var settings = TinyLayoutSettings(totalMiB: 2);

        // Act: resolve the effective layout, then reduce the aggregate cap below the declared reservation.
        var layout = ShadowResourceLayout.Resolve(settings);
        settings.Scheduling.MemoryMiB = 1;
        var underfunded = settings.Freeze();

        // Assert: a valid layout accounts both storage and draw scratch, and an insufficient budget fails preflight.
        Assert.Equal(2L * 1024 * 1024, layout.RequestedBytes);
        Assert.Equal(1024L * 1024, layout.DrawingReserveBytes);
        Assert.True(layout.FixedBytes > 0);
        Assert.Equal(layout.RequestedBytes - layout.FixedBytes, layout.AvailableTransientBytes);
        Assert.Throws<InvalidOperationException>(() => ShadowResourceLayout.Resolve(underfunded));
    }

    /// <summary>Every work dimension shares one monotonic frame ledger, including failed attempts.</summary>
    [Fact]
    public void FrameBudgetChargesAggregateWorkAndNeverRefundsRejectedAttempts()
    {
        // Arrange: set independent request, retry, and byte ceilings for one frame.
        var budget = new ShadowFrameBudget(new(Requests: 3, Retries: 2, Bytes: 10), residentBytes: 6);

        // Act: spend request capacity, reject an over-budget reservation, then charge its retry cost.
        bool first = budget.TryCharge(new(Requests: 2, Retries: 1, Bytes: 2));
        bool overBudget = budget.TryCharge(new(Requests: 2, Bytes: 1));
        bool lastRetry = budget.TryCharge(new(Retries: 1));

        // Assert: the failed reservation cannot consume work, while prior live and retired bytes stay charged.
        Assert.True(first);
        Assert.False(overBudget);
        Assert.True(lastRetry);
        Assert.Equal(1, budget.Rejections);
        Assert.Equal(2, budget.Used.Requests);
        Assert.Equal(2, budget.Used.Retries);
        Assert.Equal(8, budget.Used.Bytes);
    }

    /// <summary>Stable identity ties select the same local source when offers arrive in a different order.</summary>
    [Fact]
    public void EqualPriorityOffersUseStableIdentityInsteadOfInputOrder()
    {
        // Arrange: create equally scored local offers and a work limit that admits exactly one.
        var settings = new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1,
            DirectionalMemoryShare = 0.5, DirectionalWorkShare = 0.5
        };
        var firstKey = Source(ShadowSourceKind.Entity, 20);
        var secondKey = Source(ShadowSourceKind.Entity, 10);
        var first = Offer(firstKey, contribution: 2, milliseconds: 1, bytes: 1);
        var second = Offer(secondKey, contribution: 2, milliseconds: 1, bytes: 1);
        var forward = new ShadowScheduler(settings).Select(1, 0, [first, second], Budget());
        var reverse = new ShadowScheduler(settings).Select(1, 0, [second, first], Budget());

        // Assert: the stable source key, rather than collector enumeration, wins both independent decisions.
        Assert.Equal(secondKey, Assert.Single(forward, decision => decision.Selected).Source);
        Assert.Equal(secondKey, Assert.Single(reverse, decision => decision.Selected).Source);
    }

    /// <summary>Affordable minimum detail is selected before refinement and costs remain in one shared ledger.</summary>
    [Fact]
    public void SchedulerAdmitsCompleteMinimumThenRefinesWithinAggregateBudget()
    {
        // Arrange: two sources offer minimum and refined complete alternatives under one request ceiling.
        var settings = new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 2,
            DirectionalMemoryShare = 1, DirectionalWorkShare = 1,
            DemandRequests = 12, MaximumDetailReductions = 2
        };
        var light = new ShadowScheduler(settings);
        var sun = Offer(Source(ShadowSourceKind.Sun, 1), 4,
            new ShadowDetailOffer(0, new(Requests: 3, Bytes: 1), 1),
            new ShadowDetailOffer(1, new(Requests: 7, Bytes: 2), 2));
        var moon = Offer(Source(ShadowSourceKind.Moon, 2), 3,
            new ShadowDetailOffer(0, new(Requests: 3, Bytes: 1), 1),
            new ShadowDetailOffer(1, new(Requests: 7, Bytes: 2), 2));
        var budget = new ShadowFrameBudget(new(Requests: 12, Retries: 20, Bytes: 20), 0);

        // Act: offer both independent celestial sources to a single frame-level selector.
        var result = light.Select(1, 1, [sun, moon], budget);

        // Assert: both minimum sets fit; only the affordable refinement fits the remaining aggregate requests.
        Assert.All(result, decision => Assert.True(decision.Selected));
        Assert.Contains(result, decision => decision.Detail == 1);
        Assert.Equal(10, budget.Used.Requests);
    }

    /// <summary>Refinement charges only positive increases and never refunds a previously admitted minimum.</summary>
    [Fact]
    public void MixedRefinementCostsKeepPriorChargesAndAdmitIndependentIncreases()
    {
        // Arrange: the higher detail needs fewer requests but more bytes and physical tiles than its minimum.
        var source = Source(ShadowSourceKind.Moon, 11);
        var offer = new ShadowLightOffer(source, 1, false, false, ImmutableArray.Create(
            new ShadowDetailOffer(0, new(Requests: 2, Bytes: 4, Tiles: 2), 0.1),
            new ShadowDetailOffer(1, new(Requests: 1, Bytes: 7, Tiles: 5), 0.2)));
        var budget = new ShadowFrameBudget(new(Requests: 2, Retries: 4, Bytes: 7, Tiles: 5), 0);
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        { GpuTargetMilliseconds = 100, DirectionalWorkShare = 1, DirectionalMemoryShare = 1 });

        // Act: admit the minimum and refine when only its byte and tile requirements increase.
        var decision = Assert.Single(scheduler.Select(1, 0, [offer], budget));

        // Assert: incremental resource growth fits, while the earlier request charge remains spent.
        Assert.True(decision.Selected);
        Assert.Equal(1, decision.Detail);
        Assert.Equal(2, budget.Used.Requests);
        Assert.Equal(7, budget.Used.Bytes);
        Assert.Equal(5, budget.Used.Tiles);
    }

    /// <summary>Both directional sources retain reserved admission when the local-light allowance is zero.</summary>
    [Fact]
    public void SunAndMoonCanBeSelectedWithoutSpendingLocalLightAllowance()
    {
        // Arrange: make local admission unavailable while reserving enough aggregate work for both celestial lights.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 0,
            DirectionalWorkShare = 0.5, DirectionalMemoryShare = 0.5
        });
        var sun = Offer(Source(ShadowSourceKind.Sun, 41), 1, 2, 8);
        var moon = Offer(Source(ShadowSourceKind.Moon, 42), 1, 2, 8);
        var local = Offer(Source(ShadowSourceKind.Entity, 43), 100, 1, 1);

        // Act: arbitrate all source categories under one finite frame ledger.
        var result = scheduler.Select(1, 0, [sun, moon, local], Budget());

        // Assert: simultaneous directional eligibility is independent of the local count limit.
        Assert.Contains(result, decision => decision.Source == sun.Source && decision.Selected);
        Assert.Contains(result, decision => decision.Source == moon.Source && decision.Selected);
        Assert.Contains(result, decision => decision.Source == local.Source && !decision.Selected);
    }

    /// <summary>Physical tile reservations keep a celestial minimum ahead of a higher-scored oversized local source.</summary>
    [Fact]
    public void DirectionalTileReservationProtectsMinimumBeforeLocalBorrowing()
    {
        // Arrange: directional lights own four reserved tiles while a higher-scored local offer cannot fit its share.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 2,
            DirectionalMemoryShare = 0.5, DirectionalWorkShare = 0.5
        });
        var sun = TileOffer(Source(ShadowSourceKind.Sun, 44), 1, 4);
        var local = TileOffer(Source(ShadowSourceKind.Entity, 45), 100, 6);

        // Act: arbitrate under a finite physical tile allowance shared by all source kinds.
        var result = scheduler.Select(1, 0, [local, sun], TileBudget(8));

        // Assert: local priority cannot spend the directional reservation or exceed the aggregate tile capacity.
        Assert.Contains(result, decision => decision.Source == sun.Source && decision.Selected);
        Assert.Contains(result, decision => decision.Source == local.Source && !decision.Selected);
    }

    /// <summary>A source can borrow unused directional tiles after every reserved category has received its opportunity.</summary>
    [Fact]
    public void LocalSourceBorrowsUnusedDirectionalTileReservationWhenAggregateCapacityFits()
    {
        // Arrange: one celestial source leaves reserved tiles unused while a local source exceeds only its local share.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 2,
            DirectionalMemoryShare = 0.6, DirectionalWorkShare = 0.6
        });
        var sun = TileOffer(Source(ShadowSourceKind.Sun, 46), 1, 3);
        var local = TileOffer(Source(ShadowSourceKind.Entity, 47), 100, 5);
        var budget = TileBudget(10);

        // Act: give the celestial reservation first opportunity, then allow aggregate borrowing.
        var result = scheduler.Select(1, 0, [local, sun], budget);

        // Assert: both complete lights fit after local work borrows unused reserved capacity.
        Assert.All(result, decision => Assert.True(decision.Selected));
        Assert.Equal(8, budget.Used.Tiles);
    }

    /// <summary>When fresh detail no longer fits, an eligible retained light can spend only its smaller coverage cost.</summary>
    [Fact]
    public void RetainedOnlyAdmissionSurvivesFreshTileCostPressure()
    {
        // Arrange: select one source with a three-tile refresh, then reduce the next-frame allowance to two tiles.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        { GpuTargetMilliseconds = 100, DirectionalWorkShare = 1, DirectionalMemoryShare = 1 });
        var source = Source(ShadowSourceKind.Sun, 48);
        var currentCost = new ShadowWork(Tiles: 3, Coverage: 1);
        var offer = new ShadowLightOffer(source, 1, false, true,
            ImmutableArray.Create(new ShadowDetailOffer(0, currentCost, 1)),
            RetainedCost: new ShadowWork(Tiles: 1, Coverage: 1));
        var firstBudget = new ShadowFrameBudget(new(Tiles: 3, Coverage: 2, Retries: 4, Bytes: 16), 0);
        Assert.Contains(scheduler.Select(1, 0, [offer], firstBudget), decision => decision.Selected && decision.RefreshSelected);
        var constrainedBudget = new ShadowFrameBudget(new(Tiles: 2, Coverage: 2, Retries: 4, Bytes: 16), 0);

        // Act: refresh cannot fit, but the previous whole-light result remains eligible under its smaller cost.
        var retained = Assert.Single(scheduler.Select(2, 0.1, [offer], constrainedBudget));

        // Assert: the scheduler preserves the source without selecting a refresh and charges only retained work.
        Assert.True(retained.Selected);
        Assert.False(retained.RefreshSelected);
        Assert.Equal("RetainedRefreshDeferred", retained.Reason);
        Assert.Equal(1, constrainedBudget.Used.Tiles);
    }

    /// <summary>Every source gets minimum coverage first, ordered by the same scheduler history used for admission.</summary>
    [Fact]
    public void DemandQueueCompletesAllPrioritizedMinimumsBeforeRefinement()
    {
        // Arrange: seed one retained incumbent, then present a much stronger challenger in reverse source order.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1, MinimumHoldSeconds = 2,
            ChallengerMargin = 0, DirectionalMemoryShare = 0, DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 61);
        var challengerKey = Source(ShadowSourceKind.Entity, 62);
        var incumbent = Offer(incumbentKey, 1, 1, 1, eligible: true, incumbentReady: true);
        Assert.Contains(scheduler.Select(1, 0, [incumbent], Budget()), decision => decision.Selected);
        var callbacks = new List<string>();
        var queue = new ShadowDemandWorkQueue(capacity: 2);
        queue.Add(new(challengerKey, 100, false), () => callbacks.Add("challenger minimum"),
            () => callbacks.Add("challenger refinement"));
        queue.Add(new(incumbentKey, 0.1, false), () => callbacks.Add("incumbent minimum"),
            () => callbacks.Add("incumbent refinement"));

        // Act: prepare both source minima and refinements through the same scheduler's retained priority history.
        queue.Execute(scheduler, now: 0.25);

        // Assert: the retained incumbent's minimum runs first, every minimum precedes either refinement,
        // and source order follows scheduler stability instead of registration order or raw contribution.
        Assert.Equal(new[]
        {
            "incumbent minimum", "challenger minimum", "incumbent refinement", "challenger refinement"
        }, callbacks);
    }

    /// <summary>A still-eligible incumbent keeps its stable allocation during its configured hold interval.</summary>
    [Fact]
    public void EligibleIncumbentSurvivesAHighPriorityChallengerDuringHoldInterval()
    {
        // Arrange: admit one local source and make the challenger much stronger while retention remains valid.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1,
            MinimumHoldSeconds = 5, ChallengerMargin = 0, DirectionalMemoryShare = 0,
            DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 51);
        var challengerKey = Source(ShadowSourceKind.Entity, 52);
        var incumbent = Offer(incumbentKey, 1, 1, 1, eligible: true, incumbentReady: true);
        var initialChallenger = Offer(challengerKey, 0.5, 1, 1);
        Assert.Contains(scheduler.Select(1, 0, [incumbent, initialChallenger], Budget()),
            decision => decision.Source == incumbentKey && decision.Selected);

        // Act: evaluate shortly after the incumbent was selected, while its retained temporal result is still valid.
        var challenger = Offer(challengerKey, 4, 1, 1);
        var result = scheduler.Select(2, 0.25, [challenger, incumbent], Budget());

        // Assert: bounded hysteresis prevents a camera-facing priority fluctuation from replacing valid current work.
        Assert.Contains(result, decision => decision.Source == incumbentKey && decision.Selected);
        Assert.Contains(result, decision => decision.Source == challengerKey && !decision.Selected);
    }

    /// <summary>Incomplete temporal eligibility cannot preserve selection through the hysteresis hold interval.</summary>
    [Fact]
    public void IneligibleIncumbentDoesNotHoldSelectionAfterItsRetentionBecomesInvalid()
    {
        // Arrange: an incumbent initially wins, then a stronger challenger appears while the incumbent is ineligible.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1,
            MinimumHoldSeconds = 10, ChallengerMargin = 100, DirectionalMemoryShare = 0,
            DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 1);
        var challengerKey = Source(ShadowSourceKind.Entity, 2);
        var incumbent = Offer(incumbentKey, 2, 1, 1, eligible: true, incumbentReady: true);
        var challenger = Offer(challengerKey, 1, 1, 1);
        Assert.Contains(scheduler.Select(1, 0, [incumbent, challenger], Budget()), d => d.Source == incumbentKey && d.Selected);
        var expiredIncumbent = Offer(incumbentKey, 0.5, 1, 1, eligible: false);
        var strongChallenger = Offer(challengerKey, 8, 1, 1);

        // Act: reevaluate during the nominal hold interval after the incumbent's retained result is ineligible.
        var result = scheduler.Select(2, 0.1, [expiredIncumbent, strongChallenger], Budget());

        // Assert: invalid temporal coverage cannot be used to extend the incumbent's selection hold.
        Assert.Contains(result, decision => decision.Source == challengerKey && decision.Selected);
        Assert.Contains(result, decision => decision.Source == incumbentKey && !decision.Selected);
    }

    /// <summary>A selected but unready source cannot hold a slot or survive at zero contribution through retained fallback.</summary>
    [Fact]
    public void SelectedUnreadyZeroContributionSourceDoesNotHoldOrRetainItsSlot()
    {
        // Arrange: select one source without a ready-depth observation, then give a challenger more current contribution.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1,
            MinimumHoldSeconds = 10, ChallengerMargin = 100,
            DirectionalMemoryShare = 0, DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 71);
        var challengerKey = Source(ShadowSourceKind.Entity, 72);
        var selectedButUnready = Offer(incumbentKey, 2, 1, 1, eligible: true);
        Assert.Contains(scheduler.Select(1, 0, [selectedButUnready], Budget()), d => d.Selected);
        var zeroContributionRetained = new ShadowLightOffer(incumbentKey, 0, false, true,
            ImmutableArray.Create(new ShadowDetailOffer(0, new(Bytes: 1), 1)),
            RetainedCost: new ShadowWork(Bytes: 1), IncumbentReady: false);
        var challenger = Offer(challengerKey, 4, 1, 1);

        // Act: reevaluate while the stale selection hint still exists but no current GPU observation certifies it.
        var result = scheduler.Select(2, 0.1, [zeroContributionRetained, challenger], Budget());

        // Assert: only current positive contribution wins; an unready prior selection cannot consume retained capacity.
        Assert.Contains(result, d => d.Source == challengerKey && d.Selected);
        Assert.Contains(result, d => d.Source == incumbentKey && !d.Selected);
    }

    /// <summary>Carried sources receive their configured preference when contribution and work cost are comparable.</summary>
    [Fact]
    public void CarriedSourcePreferenceCanWinWithinTheConfiguredPriorityMargin()
    {
        // Arrange: give a carried source a lower visible contribution but the configured twofold priority preference.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1, MinimumHoldSeconds = 0,
            ChallengerMargin = 0, CarriedPreference = 2, DirectionalMemoryShare = 0, DirectionalWorkShare = 0
        });
        var nearby = new ShadowLightOffer(Source(ShadowSourceKind.Entity, 81), 1.2, false, false,
            ImmutableArray.Create(new ShadowDetailOffer(0, new(Bytes: 1), 1)));
        var carried = new ShadowLightOffer(Source(ShadowSourceKind.Entity, 82), 0.7, true, false,
            ImmutableArray.Create(new ShadowDetailOffer(0, new(Bytes: 1), 1)));

        // Act: arbitrate equal-work sources for one local-light slot.
        var result = scheduler.Select(1, 0, [nearby, carried], Budget());

        // Assert: the configured carried preference reverses the raw-contribution ordering.
        Assert.Contains(result, decision => decision.Source == carried.Source && decision.Selected);
        Assert.Contains(result, decision => decision.Source == nearby.Source && !decision.Selected);
    }

    /// <summary>Accumulated wait can eventually overcome a comparable ready incumbent after its hold has elapsed.</summary>
    [Fact]
    public void BoundedWaitingEventuallyPromotesAComparableChallenger()
    {
        // Arrange: establish a ready incumbent and a slightly weaker source that remains continuously eligible.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1, MinimumHoldSeconds = 0.1,
            MaximumWaitingSeconds = 1, ChallengerMargin = 0.25, DirectionalMemoryShare = 0,
            DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 83);
        var challengerKey = Source(ShadowSourceKind.Entity, 84);
        var incumbent = Offer(incumbentKey, 1, 1, 1, eligible: true, incumbentReady: true);
        var challenger = Offer(challengerKey, 0.8, 1, 1);
        Assert.Contains(scheduler.Select(1, 0, [incumbent, challenger], Budget()),
            decision => decision.Source == incumbentKey && decision.Selected);

        // Act: compare before accumulated wait crosses the incumbent multiplier, then after it does.
        var beforeWaitWins = scheduler.Select(2, 0.4, [incumbent, challenger], Budget());
        var afterWaitWins = scheduler.Select(3, 1.0, [incumbent, challenger], Budget());

        // Assert: waiting is bounded and eventually changes arbitration after the hold interval ends.
        Assert.Contains(beforeWaitWins, decision => decision.Source == incumbentKey && decision.Selected);
        Assert.Contains(afterWaitWins, decision => decision.Source == challengerKey && decision.Selected);
        Assert.Contains(afterWaitWins, decision => decision.Source == challengerKey && decision.WaitingSeconds <= 1);
    }

    /// <summary>A ready incumbent margin rejects a below-margin challenger and admits one above it after the hold.</summary>
    [Fact]
    public void ChallengerMarginSeparatesBelowAndAboveThresholdScores()
    {
        // Arrange: use isolated scheduler histories so only the configured 35-percent margin distinguishes cases.
        var scheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1, MinimumHoldSeconds = 0.1,
            ChallengerMargin = 0.35, DirectionalMemoryShare = 0, DirectionalWorkShare = 0
        });
        var incumbentKey = Source(ShadowSourceKind.Entity, 85);
        var incumbent = Offer(incumbentKey, 1, 1, 1, eligible: true, incumbentReady: true);
        Assert.Contains(scheduler.Select(1, 0, [incumbent], Budget()), d => d.Selected);
        var below = Offer(Source(ShadowSourceKind.Entity, 86), 1.2, 1, 1);
        var above = Offer(Source(ShadowSourceKind.Entity, 87), 1.4, 1, 1);

        // Act: submit one candidate just below the effective margin and another just above it.
        var belowResult = scheduler.Select(2, 0.2, [incumbent, below], Budget());
        // Re-seed a distinct scheduler so waiting history from the first comparison cannot affect the second.
        var secondScheduler = new ShadowScheduler(new ShadowSchedulingSettings
        {
            GpuTargetMilliseconds = 100, MaxShadowedLocalLights = 1, MinimumHoldSeconds = 0.1,
            ChallengerMargin = 0.35, DirectionalMemoryShare = 0, DirectionalWorkShare = 0
        });
        secondScheduler.Select(1, 0, [incumbent], Budget());
        var aboveResult = secondScheduler.Select(2, 0.2, [incumbent, above], Budget());

        // Assert: the below-margin candidate stays queued; the above-margin candidate wins.
        Assert.Contains(belowResult, d => d.Source == incumbentKey && d.Selected);
        Assert.Contains(belowResult, d => d.Source == below.Source && !d.Selected);
        Assert.Contains(aboveResult, d => d.Source == above.Source && d.Selected);
    }

    /// <summary>Directional receiver bounds include filter and normal-bias support and union all layers on shared pages.</summary>
    [Fact]
    public void DirectionalDemandUnionsDepthInclusiveBoundsForReceiversSharingPages()
    {
        // Arrange: place two receiver layers in one page while retaining their independent depth extents.
        var pages = new ShadowPageSettings { PageSize = 128, NormalBiasTexels = 0.5 };
        var source = Source(ShadowSourceKind.Sun, 30);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitY,
            new(-1000, -1000, -1000, 1000, 1000, 1000), baseTexelWidth: 0.01, pageSize: 128);
        var budget = new ShadowFrameBudget(new(Requests: 64, Coverage: 64, Inputs: 8, Bytes: 65536), 0);
        var demand = new ShadowSurfaceDemand(budget, pages, capacity: 64);
        demand.AddDirectional(new(ShadowSurfaceCategory.Opaque, new(-1, 10, -1, 1, 12, 1)), projection, projection, 0, 4, 5, 6);
        demand.AddDirectional(new(ShadowSurfaceCategory.Transparent, new(-1, 30, -1, 1, 32, 1)), projection, projection, 0, 4, 5, 6);

        // Act: complete every participating category after its conservative requests have been supplied.
        demand.CompleteCategory(ShadowSurfaceCategory.All);
        var pagesRequested = demand.Pages;

        // Assert: the published page covers both depth-separated receivers and their filter/bias footprint.
        Assert.True(demand.Complete);
        Assert.NotEmpty(pagesRequested);
        Assert.All(pagesRequested, page =>
        {
            Assert.True(page.Request.Region.MinY < 10);
            Assert.True(page.Request.Region.MaxY > 32);
            Assert.Equal(4, page.Request.WorldRevision);
            Assert.Equal(5, page.Request.SceneEpoch);
            Assert.Equal(6, page.Request.SceneSequence);
        });
        Assert.Equal(2, budget.Used.Inputs);
        Assert.Equal(2L * pagesRequested.Length, budget.Used.Requests);
    }

    /// <summary>Point-light demand is clipped to the same influence radius used by direct illumination.</summary>
    [Fact]
    public void PointDemandIgnoresReceiversOutsideCurrentIlluminationInfluence()
    {
        // Arrange: provide receivers both inside and beyond the source's spherical influence bound.
        var pages = new ShadowPageSettings { PageSize = 128, NormalBiasTexels = 0 };
        var source = Source(ShadowSourceKind.Entity, 31);
        var projection = new PointShadowProjection(source, 1, Vector3d.Zero,
            near: 0.05, far: 20, faceResolution: 128, pageSize: 128);
        var budget = new ShadowFrameBudget(new(Requests: 64, Coverage: 64, Inputs: 8, Bytes: 65536), 0);
        var demand = new ShadowSurfaceDemand(budget, pages, capacity: 64);
        demand.AddPoint(new(ShadowSurfaceCategory.Opaque, new(1, 1, 1, 2, 2, 2)), projection, projection,
            influenceRadius: 5, 4, 5, 6);
        long admittedRequests = budget.Used.Requests;
        demand.AddPoint(new(ShadowSurfaceCategory.Foliage, new(8, 8, 8, 9, 9, 9)), projection, projection,
            influenceRadius: 5, 4, 5, 6);
        demand.CompleteCategory(ShadowSurfaceCategory.All);

        // Assert: the outside region still costs receiver inspection but adds no virtual page work.
        Assert.True(demand.Complete);
        Assert.NotEmpty(demand.Pages);
        Assert.Equal(admittedRequests, budget.Used.Requests);
        Assert.Equal(2, budget.Used.Inputs);
    }

    /// <summary>Missing surface categories, duplicate category production, or request overflow cannot appear complete.</summary>
    [Fact]
    public void DemandRejectsMissingCategoriesDuplicateProvidersAndOverflow()
    {
        // Arrange: create a one-page demand with deliberately insufficient global request capacity.
        var settings = new ShadowPageSettings { PageSize = 128 };
        var source = Source(ShadowSourceKind.Sun, 32);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitY,
            new(-1000, -1000, -1000, 1000, 1000, 1000), baseTexelWidth: 0.01, pageSize: 128);
        var limited = new ShadowFrameBudget(new(Requests: 0, Coverage: 0, Inputs: 4, Bytes: 4096), 0);
        var overflow = new ShadowSurfaceDemand(limited, settings, capacity: 1);

        // Act: request required surface pages under an exhausted aggregate allowance, then mark categories complete.
        overflow.AddDirectional(new(ShadowSurfaceCategory.Opaque, new(0, 0, 0, 1, 1, 1)), projection, projection, 0, 4, 5, 6);
        overflow.CompleteCategory(ShadowSurfaceCategory.All);
        var incomplete = new ShadowSurfaceDemand(new ShadowFrameBudget(
            new(Requests: 16, Coverage: 16, Inputs: 8, Bytes: 8192), 0), settings, capacity: 16);
        incomplete.CompleteCategory(ShadowSurfaceCategory.Opaque);
        incomplete.AddDirectional(new(ShadowSurfaceCategory.Opaque, new(0, 0, 0, 1, 1, 1)), projection, projection, 0, 4, 5, 6);

        // Assert: category declarations do not erase either failed requests or evidence of duplicate production.
        Assert.False(overflow.Complete);
        Assert.Equal("RequestOverflow", overflow.Failure);
        Assert.False(incomplete.Complete);
        Assert.Equal("ReceiverInput", incomplete.Failure);
    }
    #endregion

    #region Private
    /// <summary>Builds one known source key with deterministic ownership and lifetime fields.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long owner) =>
        new(1, 0, kind, owner, owner + 100, 0);

    /// <summary>Builds a single complete quality alternative for priority-only scenarios.</summary>
    private static ShadowLightOffer Offer(ShadowSourceKey source, double contribution,
        double milliseconds, long bytes, bool eligible = false, bool incumbentReady = false) =>
        new(source, contribution, false, eligible,
            ImmutableArray.Create(new ShadowDetailOffer(0, new(Bytes: bytes), milliseconds)),
            IncumbentReady: incumbentReady);

    /// <summary>Builds a one-detail offer with an explicit number of physical page tiles.</summary>
    private static ShadowLightOffer TileOffer(ShadowSourceKey source, double contribution, long tiles) =>
        new(source, contribution, false, false,
            ImmutableArray.Create(new ShadowDetailOffer(0, new(Tiles: tiles), 1)));

    /// <summary>Builds explicit monotonic complete alternatives for detail admission scenarios.</summary>
    private static ShadowLightOffer Offer(ShadowSourceKey source, double contribution,
        ShadowDetailOffer first, ShadowDetailOffer second, bool eligible = false) =>
        new(source, contribution, false, eligible, ImmutableArray.Create(first, second));

    /// <summary>Builds a scheduler frame budget with enough independent work dimensions for selection fixtures.</summary>
    private static ShadowFrameBudget Budget() => new(new(Requests: 100, Retries: 100, Bytes: 100), 0);
    /// <summary>Provides one bounded tile pool and enough independent non-tile work for category arbitration.</summary>
    private static ShadowFrameBudget TileBudget(long tiles) => new(new(Tiles: tiles, Retries: 100, Bytes: 100), 0);

    /// <summary>Builds the smallest valid aggregate layout for checking pre-allocation rejection behavior.</summary>
    private static ShadowSettings TinyLayoutSettings(int totalMiB) => new()
    {
        Scheduling = new() { MemoryMiB = totalMiB, LightCapacity = 1, DemandRequests = 1, ProjectionHistoryCapacity = 1 },
        Inputs = new() { ChunkCapacity = 1, RangeCapacity = 1, ChangeCapacity = 1 },
        Candidates = new() { MemoryMiB = 1, HeaderCapacity = 1, SegmentsPerAssociation = 1,
            DedupCapacity = 128, ChangeCapacity = 1 },
        Pages = new() { MemoryMiB = 1, PageSize = 8, TileColumns = 1, TileRows = 1, ArrayLayers = 1 },
        Drawing = new() { MemoryMiB = 1, GroupVisits = 1, RangeVisits = 1, EntityVisits = 1,
            ViewCapacity = 1, PairCapacity = 128, ExpansionVisits = 1, CommandSlots = 1,
            ViewInstances = 1, RasterPixels = 1, EstimatedTriangles = 1, EstimatedRasterSamples = 1 },
        Temporal = new()
    };
    #endregion
}
