using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Verifies physical-page allocation and coverage certificates through the GL 4.5 compute path.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowPagePoolTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    private static int pendingFencePolls;
    private static int observedPendingFencePolls;
    private static int arrayCreationAttempts;
    private static readonly ShadowBounds Small = new(-1, -1, 2, 1, 1, 3);
    private static readonly ShadowBounds Expanded = new(-1, -1, 8, 1, 1, 9);
    private static readonly ShadowBounds RequiredUnion = new(-1, -1, 2, 1, 1, 9);
    private static readonly ShadowBounds LateDepth = new(-1, -1, 12, 1, 1, 13);
    private static readonly ShadowBounds CompleteUnion = new(-1, -1, 2, 1, 1, 13);
    private static readonly ShadowSourceKey Sun = new(11, 2, ShadowSourceKind.Sun, 3, 7, 0);

    #region Public API
    #region Coverage and Lookup
    /// <summary>Incomplete and mismatched assignments cannot publish; coverage and current required unions are distinct.</summary>
    [Fact]
    public void PageCertificateRequiresMatchingDrawAssignmentAndCompleteRequiredUnion()
    {
        // Arrange: allocate a tiny GL 4.5 page table and keep every operation inside one controlled batch.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(0, 0, projection: 3);
        Assert.True(pool.BeginBatch());

        // Act: allocate, clear, and freeze a pending page before testing publication rejection conditions.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocated = Result(pool);
        Assert.Equal(0u, allocated[0]);
        long assignment = Assignment(allocated);
        Assert.True(assignment > 0);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        Assert.Equal(4u, Result(pool)[0]);
        Assert.True(pool.FreezeWritableAssignments());
        Assert.False(pool.Execute(Request(ShadowPageOperation.Allocate, Key(1, 0, projection: 3), Small)));
        Assert.False(pool.Execute(Request(ShadowPageOperation.CancelReplacement, key, Small, assignment)));
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: false)));
        Assert.Equal(4u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(5u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment + 1, complete: true)));
        Assert.Equal(4u, Result(pool)[0]);

        // Act: publish near-depth coverage, then require another receiver at the same XY page and a different Z interval.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Expanded)));
        Assert.Equal(2u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Require, key, Expanded)));
        Assert.Equal(2u, Result(pool)[0]);

        // Assert: a replacement must cover every region required in this batch before the old page is retired.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        foreach (var region in new[] { Small, Expanded })
        {
            Assert.True(pool.Execute(Request(ShadowPageOperation.Require, key, region)));
            Assert.Equal(region == Small ? 0u : 2u, Result(pool)[0]);
        }
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        Assert.Equal(6u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, RequiredUnion)));
        var replacement = Result(pool);
        Assert.Equal(0u, replacement[0]);
        long replacementAssignment = Assignment(replacement);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        // A receiver arrives after allocation, so the pending certificate no longer covers the current required union.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Require, key, LateDepth)));
        Assert.Equal(5u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, RequiredUnion,
            replacementAssignment, complete: true)));
        Assert.Equal(6u, Result(pool)[0]);

        // The old near-depth certificate stays valid while the incomplete replacement is safely canceled.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, LateDepth)));
        Assert.Equal(5u, Result(pool)[0]);
        pool.SealBatch();

        // Act: after the batch fence, the unfinished replacement has already been canceled by SealBatch.
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.CancelReplacement, key, RequiredUnion, replacementAssignment)));
        Assert.Equal(4u, Result(pool)[0]);

        // Assert: the stale token cannot cancel again; the prior small certificate remains available.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        foreach (var region in new[] { Small, Expanded, LateDepth })
        {
            Assert.True(pool.Execute(Request(ShadowPageOperation.Require, key, region)));
            Assert.Equal(region == Small ? 0u : 2u, Result(pool)[0]);
        }
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, CompleteUnion)));
        var complete = Result(pool); Assert.Equal(0u, complete[0]);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, CompleteUnion,
            Assignment(complete), complete: true)));
        Assert.Equal(0u, Result(pool)[0]);

        // Assert: the new assignment now serves every region that was requested during either frame interval.
        foreach (var region in new[] { Small, Expanded, LateDepth })
        {
            Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, region)));
            Assert.Equal(0u, Result(pool)[0]);
        }
        pool.SealBatch();
    }

    /// <summary>Tracks a directly published assignment through abort sealing and the next batch boundary.</summary>
    [Fact]
    public void CompletedLegacyPublicationSurvivesPendingBatchAbortAndNextBegin()
    {
        // Arrange: create one exact assignment through the legacy single-page publish path.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(0, 0, projection: 33);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocated = Result(pool);
        Assert.Equal(0u, allocated[0]);
        int slot = checked((int)allocated[1]);
        long assignment = Assignment(allocated);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        Assert.Equal(0u, Result(pool)[0]);

        // Act: map the same entry before sealing, after automatic cancellation, and after opening the next batch.
        long allocationBatch = pool.Batch;
        var beforeSeal = EntryLifecycle(pool, slot);
        pool.SealBatch();
        var afterSeal = EntryLifecycle(pool, slot);
        Assert.True(pool.BeginBatch());
        var afterBegin = EntryLifecycle(pool, slot);
        foreach (var state in new[] { beforeSeal, afterSeal, afterBegin })
            output.WriteLine($"state={state.State}, tokenLow={state.BatchLow}, tokenHigh={state.BatchHigh}, allocationBatch={allocationBatch}");

        // Assert: batch cancellation preserves a completed page and its original allocation epoch.
        Assert.Equal(2u, beforeSeal.State);
        Assert.Equal(2u, afterSeal.State);
        Assert.Equal(2u, afterBegin.State);
        Assert.Equal((uint)allocationBatch, beforeSeal.BatchLow);
        Assert.Equal((uint)(allocationBatch >> 32), beforeSeal.BatchHigh);
        Assert.Equal(beforeSeal.BatchLow, afterSeal.BatchLow);
        Assert.Equal(beforeSeal.BatchHigh, afterSeal.BatchHigh);
        Assert.Equal(beforeSeal.BatchLow, afterBegin.BatchLow);
        Assert.Equal(beforeSeal.BatchHigh, afterBegin.BatchHigh);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>Current dependency checks preserve a prior certificate and never restart its stale deadline.</summary>
    [Fact]
    public void CurrentDependencyValidationPreservesCoverageAndFirstInvalidationTime()
    {
        // Arrange: publish a complete page, then validate it against consecutive changed scene sequences.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(0, 0, projection: 35);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocation = Result(pool);
        Assert.Equal(0u, allocation[0]);
        long assignment = Assignment(allocation);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        int slot = checked((int)Result(pool)[1]);

        // Act: validate successive dependency changes with later timestamps while naming the current projection.
        var first = Request(ShadowPageOperation.ValidateCurrent, key, Small, complete: true) with
        { SceneSequence = 20, CurrentProjection = key.Projection, Timestamp = 2 };
        var second = first with { SceneSequence = 21, Timestamp = 9 };
        Assert.True(pool.Execute(first));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(second));
        var receipt = Result(pool);
        var deadline = EntryWords(pool, slot, 60, 2);
        var invalidated = EntryWords(pool, slot, 110, 1);
        var invalidationTime = BitConverter.Int64BitsToDouble(((long)deadline[1] << 32) | deadline[0]);

        // Assert: the compact GPU command receipt still identifies the old slot and its first stale timestamp.
        Assert.Equal((uint)slot, receipt[1]);
        Assert.Equal(1u, invalidated[0]);
        Assert.Equal(2, invalidationTime);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        // Changed dependencies make the old complete page non-current; temporal sampling may use it only as stale.
        Assert.Equal(2u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>Source-wide light or scene changes invalidate every retained page without restarting stale age.</summary>
    [Fact]
    public void SourceValidationPreservesFirstInvalidationAcrossLaterFrames()
    {
        // Arrange: publish an old directional mapping and construct a changed source projection generation.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(0, 0, projection: 45);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocation = Result(pool);
        long assignment = Assignment(allocation);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        int slot = checked((int)Result(pool)[1]);
        var currentKey = key with { Projection = key.Projection + 1 };
        var movedProjection = new DirectionalShadowProjection(currentKey.Source, currentKey.Projection, Vector3d.UnitY,
            new ShadowBounds(-100, -100, -100, 100, 100, 100), 1, pageSize: 8);
        var movedSnapshot = ShadowProjectionSnapshot.Directional(movedProjection, currentKey.FaceOrLevel);

        // Act: validate the new light mapping twice at later times while retaining the old page projection.
        var first = Request(ShadowPageOperation.ValidateSource, currentKey, Small, complete: true) with
        { Projection = movedSnapshot, Timestamp = 2 };
        var later = first with { Timestamp = 9 };
        Assert.True(pool.Execute(first));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(later));
        var invalidated = EntryWords(pool, slot, 110, 1);
        var deadline = EntryWords(pool, slot, 60, 2);
        var invalidationTime = BitConverter.Int64BitsToDouble(((long)deadline[1] << 32) | deadline[0]);

        // Assert: the source-level operation marks the prior complete page once and keeps its original stale deadline.
        Assert.Equal(1u, invalidated[0]);
        Assert.Equal(2, invalidationTime);
        pool.SealBatch();
    }

    /// <summary>A requested allocation reuses a still-current complete assignment without creating a replacement.</summary>
    [Fact]
    public void AllocationWithReuseCurrentKeepsTheExistingAssignment()
    {
        // Arrange: publish one page and retain its exact physical slot for a same-dependency allocation request.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(1, 0, projection: 36);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocation = Result(pool);
        long assignment = Assignment(allocation);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, assignment, complete: true)));
        var published = Result(pool);
        uint slot = published[1];

        // Act: ask the pool for a replacement while explicitly allowing a matching current certificate to satisfy it.
        pool.SealBatch(); RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small) with { ReuseCurrent = true }));
        var reused = Result(pool);

        // Assert: the same physical assignment satisfies the request and remains sampleable without a new write token.
        Assert.Equal(0u, reused[0]);
        Assert.Equal(slot, reused[1]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>Full 64-bit page keys distinguish sources, virtual coordinates and projection generations.</summary>
    [Fact]
    public void PageLookupDoesNotAliasDifferentProjectionOrPageIdentity()
    {
        // Arrange: publish one synthetic complete key and reserve its physical assignment.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        var key = Key(5, -4, projection: (1L << 40) + 19);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        var allocation = Result(pool); Assert.Equal(0u, allocation[0]);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, Small, Assignment(allocation), complete: true)));
        Assert.Equal(0u, Result(pool)[0]);

        // Act and assert: only the exact source/projection/page tuple resolves this certificate.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key with { X = key.X + 1 }, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key with { Projection = key.Projection + 1 }, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key with { Source = key.Source with { Lifetime = 8 } }, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>Generated commands cannot allocate or freeze CPU-owned writable assignments.</summary>
    [Fact]
    public void GeneratedCommandsCannotAllocateOrFreezeWritableAssignments()
    {
        // Arrange: provide valid fixed-size commands through the same real SSBO range accepted by production producers.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 1));
        using var generated = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
        {
            Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.DynamicDraw, Label = "Tests.Shadows.GeneratedPageCommands"
        }).RequireResource();
        var key = Key(0, 0, projection: 5);
        var range = new RHIBufferRange(generated, 0, 128 * sizeof(uint));
        Assert.True(pool.BeginBatch());

        // Act: submit generated allocation and freeze opcodes, then perform CPU-owned allocations in the same batch.
        generated.UploadData(Request(ShadowPageOperation.Allocate, key, Small).Pack());
        Assert.True(pool.ExecuteGenerated(range));
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        generated.UploadData(new ShadowPageRequest(ShadowPageOperation.FreezeWrites, default, default, 0, 0, 0).Pack());
        Assert.True(pool.ExecuteGenerated(range));
        var secondKey = Key(1, 0, projection: 5);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, secondKey, Small)));
        var allocation = Result(pool);

        // Assert: generated operations did not claim slots or freeze the CPU control plane; real freeze rejects writes.
        Assert.Equal(0u, allocation[0]);
        long assignment = Assignment(allocation);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.False(pool.Execute(Request(ShadowPageOperation.Allocate, Key(2, 0, projection: 5), Small)));
        Assert.False(pool.Execute(Request(ShadowPageOperation.CancelReplacement, secondKey, Small, assignment)));
        pool.SealBatch();
    }
    #endregion

    #region Bounded Allocation and Identity Retirement
    /// <summary>Key, source and dimension retirement commands have distinct identity scopes.</summary>
    [Fact]
    public void RetirementCommandsRemoveOnlyTheirDocumentedIdentityScope()
    {
        // Arrange: publish two pages for one source, the other celestial source, and another dimension.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 4, layers: 1));
        var sunA = Key(1, 2, source: Sun);
        var sunB = Key(2, 2, source: Sun);
        var moon = Key(1, 2, source: Sun with { Kind = ShadowSourceKind.Moon, Slot = 1 });
        var otherDimension = Key(1, 2, source: Sun with { Dimension = 3 });
        Assert.True(pool.BeginBatch());
        Publish(pool, sunA, Small); Publish(pool, sunB, Small); Publish(pool, moon, Small); Publish(pool, otherDimension, Small);

        // Act: retire one exact virtual key, then retire every remaining source in its world dimension.
        Assert.True(pool.Execute(Request(ShadowPageOperation.RetireKey, sunA, Small)));
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, sunB, Small)));
        uint siblingPage = Result(pool)[0];
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, moon, Small)));
        uint sameDimensionMoon = Result(pool)[0];
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, otherDimension, Small)));
        uint differentDimensionBefore = Result(pool)[0];
        Assert.True(pool.Execute(Request(ShadowPageOperation.RetireDimension, sunB, Small)));
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, sunB, Small)));
        uint sunAfterDimensionRemoval = Result(pool)[0];
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, moon, Small)));
        uint moonAfterDimensionRemoval = Result(pool)[0];
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, otherDimension, Small)));
        uint differentDimensionAfter = Result(pool)[0];

        // Assert: exact-key removal preserves siblings; dimension removal covers both sun and moon, not another dimension.
        Assert.Equal(0u, siblingPage);
        Assert.Equal(0u, sameDimensionMoon);
        Assert.Equal(0u, differentDimensionBefore);
        Assert.Equal(1u, sunAfterDimensionRemoval);
        Assert.Equal(1u, moonAfterDimensionRemoval);
        Assert.Equal(0u, differentDimensionAfter);
        pool.SealBatch();
    }

    /// <summary>Resident tile exhaustion rejects new allocation rather than aliasing an occupied assignment.</summary>
    [Fact]
    public void PageCapacityFailureDoesNotAliasExistingAssignment()
    {
        // Arrange: expose exactly one resident physical tile and begin one page-operation interval.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var fixedDefines = ShadowPageShader.Contract.Stages[0].FixedDefines
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.MacroLiteral);
        output.WriteLine("=== Runtime-preprocessed shadows/pages.csh ===");
        output.WriteLine(ShaderSourceCode.Load(assets.Api, "shadows/pages", "csh", fixedDefines).EmittedSource);
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 1, layers: 1));
        Assert.True(pool.BeginBatch());

        // Act: reserve the only slot, then request a distinct page before its assignment is retired.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, Key(0, 0), Small)));
        var first = Result(pool); Assert.Equal(0u, first[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, Key(1, 0), Small)));
        var overflow = Result(pool);

        // Assert: capacity is explicit and the rejected request never receives the existing slot's generation.
        Assert.Equal(3u, overflow[0]);
        Assert.Equal(0u, first[1]);
        Assert.NotEqual(first[2], overflow[2]);
        pool.SealBatch();
    }

    /// <summary>A retired tile is unavailable within its batch and a later reuse receives a new assignment generation.</summary>
    [Fact]
    public void RetiredLayerWaitsForFenceAndRejectsItsPriorGeneration()
    {
        // Arrange: use one physical tile so every successful allocation must reuse the same slot after retirement.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 1, layers: 1));
        var firstKey = Key(0, 0); var secondKey = Key(1, 0);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, firstKey, Small)));
        var original = Result(pool);
        Assert.Equal(0u, original[0]);
        int originalSlot = checked((int)original[1]);
        long originalAssignment = Assignment(original);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, firstKey, Small,
            originalAssignment, complete: true)));
        Assert.Equal(0u, Result(pool)[0]);
        pool.SealBatch();

        // Act: retire the first assignment, then request a different page before this batch's fence can authorize reuse.
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.RetireKey, firstKey, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, secondKey, Small)));
        var premature = Result(pool);
        pool.SealBatch();

        // Assert: retirement cannot be bypassed within the same submission interval despite the tile being idle.
        Assert.Equal(3u, premature[0]);
        RHICommandContext.Current.WaitIdle(); pool.Poll();

        // Act: after fence completion, allocate the new key and try to certify it with the stale assignment.
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, secondKey, Small)));
        var reused = Result(pool);
        Assert.Equal(0u, reused[0]);
        Assert.Equal(originalSlot, checked((int)reused[1]));
        Assert.NotEqual(originalAssignment, Assignment(reused));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, secondKey, Small, originalAssignment, complete: true)));
        var rejected = Result(pool);

        // Assert: the old generation cannot publish the new physical contents.
        Assert.Equal(4u, rejected[0]);
        pool.SealBatch();
    }

    /// <summary>A one-probe lookup rejects a collision even though other resident tiles remain free.</summary>
    [Fact]
    public void ProbeLimitExhaustionRejectsWithoutConsumingAnotherOwnersSlot()
    {
        // Arrange: find two serialized page keys with the same initial bucket under a deliberate one-probe policy.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(tilesPerLayer: 4, layers: 1); settings.ProbeLimit = 1;
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        var keys = CollidingKeys(pool.Capacity);
        Assert.NotEqual(keys.First, keys.Second);
        Assert.True(pool.BeginBatch());

        // Act: occupy the colliding bucket and submit the second distinct key with three physical tiles still free.
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, keys.First, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, keys.Second, Small)));
        var exhausted = Result(pool);

        // Assert: bounded lookup reports failure and never returns another page's physical identity.
        Assert.Equal(4, pool.Capacity);
        Assert.Equal(1, pool.ProbeLimit);
        Assert.Equal(3u, exhausted[0]);
        Assert.NotEqual(0u, exhausted[1]);
        Assert.Equal(0u, exhausted[2]);
        pool.SealBatch();
    }
    #endregion

    #region Resource Lifetime and Capacity
    /// <summary>Native reload and explicit disable cancel a queued replacement before fence completion.</summary>
    [Fact]
    public void ReloadAndWithdrawCancelPendingReconfiguration()
    {
        // Arrange: register the pool through the current client lifetime before requesting replacement storage.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var lifetime = ClientRenderingLifetime.Create(assets.Api, GpuShaderProgramLibraries.Require(assets.Api));
        lifetime.Attach(static () => { });
        using var pool = ShadowPagePool.Create(assets.Api, Settings(pageSize: 8, tilesPerLayer: 1, layers: 1));
        try
        {
            // Act: queue reconfiguration, then let the native reload owner withdraw the pool synchronously.
            pool.Reconfigure(Settings(pageSize: 16, tilesPerLayer: 1, layers: 1));
            Assert.False(pool.IsAvailable);
            ClientRenderingLifetime.BeginNativeReload();
            Assert.True(lifetime.Reloading);
            ClientRenderingLifetime.CompleteNativeReload();
            RHICommandContext.Current.WaitIdle(); pool.Poll();

            // Assert: reload withdrawal supersedes replacement and leaves the pool empty and unavailable.
            Assert.False(lifetime.Reloading);
            Assert.False(pool.IsAvailable);
            Assert.Equal(0, pool.AllocatedBytes);
        }
        finally
        {
            if (lifetime.Reloading) ClientRenderingLifetime.CompleteNativeReload();
            pool.Dispose();
            lifetime.Shutdown();
        }
    }

    /// <summary>Explicit withdrawal supersedes a queued pool replacement through fence completion.</summary>
    [Fact]
    public void ReconfigureThenWithdrawDoesNotRecreateResources()
    {
        // Arrange: start a new settings replacement while the original allocation is still owned.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(pageSize: 8, tilesPerLayer: 1, layers: 1));
        pool.Reconfigure(Settings(pageSize: 16, tilesPerLayer: 1, layers: 1));
        Assert.False(pool.IsAvailable);
        Assert.True(pool.AllocatedBytes > 0);

        // Act: disable the feature before the replacement retirement fence is consumed.
        pool.Withdraw();
        RHICommandContext.Current.WaitIdle(); pool.Poll();

        // Assert: the newer disable wins; polling cannot silently recreate the obsolete requested policy.
        Assert.False(pool.IsAvailable);
        Assert.Equal(0, pool.AllocatedBytes);
        Assert.False(pool.BeginBatch());
    }

    /// <summary>Actual render-context retirement invokes the pool owner callback before the context registration changes.</summary>
    [Fact]
    public void RenderContextRetirementWithdrawsPoolAndOwnedStorage()
    {
        // Arrange: capture the real harness registration so it can be restored exactly after its retirement callback runs.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(pageSize: 8, tilesPerLayer: 1, layers: 1));
        var original = RenderContextRegistry.Current();
        var registrations = (IDictionary)typeof(RenderContextRegistry).GetField("registrations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object registration = registrations[original.Handle]!;
        var replacement = new object();
        try
        {
            // Act: retire the harness-owned context through the existing registry contract.
            RenderContextRegistry.Retire(fixture);
            Assert.False(pool.IsAvailable);
            Assert.Equal(0, pool.AllocatedBytes);
            RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
        }
        finally
        {
            // Restore the original harness receipt so later tests retain the same actual current GL context identity.
            lock (registrations) registrations[original.Handle] = registration;
        }

        // Assert: the exact original owner generation was restored, while its pool remains retired.
        Assert.Equal(original, RenderContextRegistry.Current());
        Assert.False(pool.IsAvailable);
        Assert.Equal(0, pool.AllocatedBytes);
    }

    /// <summary>Page occupancy uses fixed storage, and explicit layout reconfiguration replaces its array after users retire.</summary>
    [Fact]
    public void RetirementAndReconfigurationRespectFenceAndSourceScopes()
    {
        // Arrange: allocate one complete array up front, then publish two celestial pages into its fixed capacity.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 2, layers: 2));
        Assert.Equal(2, pool.DepthArray.Depth);
        long expectedOldAllocation = ExpectedPoolBytes(pageSize: 8, tilesPerLayer: 2, layers: 2);
        Assert.Equal(expectedOldAllocation, pool.AllocatedBytes);
        var sun = Key(0, 0, 5, Sun);
        var moon = Key(0, 0, 5, Sun with { Kind = ShadowSourceKind.Moon, Slot = 1 });
        Assert.True(pool.BeginBatch());
        Publish(pool, sun, Small); Publish(pool, moon, Small);
        pool.SealBatch();

        // Act: remove the solar source and verify the distinct lunar page remains addressable.
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.RetireSource, sun, Small)));
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, moon, Small)));
        uint moonStillCurrent = Result(pool)[0];
        pool.SealBatch();

        // Assert: source removal is isolated and page occupancy changes do not resize the physical array.
        Assert.Equal(0u, moonStillCurrent);
        Assert.Equal(2, pool.ArrayLayers);
        Assert.Equal(0, pool.DrainingBytes);
        Assert.Equal(expectedOldAllocation, pool.AllocatedBytes);
        Assert.Equal(2, pool.DepthArray.Depth);

        // Act: request a different fixed layout; replacement waits until the original array's users retire.
        var oldArray = PhysicalArray(pool);
        int oldTextureId = oldArray.TextureId;
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        pool.Reconfigure(Settings(pageSize: 16, tilesPerLayer: 2, layers: 3));
        Assert.False(pool.IsAvailable);
        Assert.Equal(expectedOldAllocation, pool.AllocatedBytes);
        Assert.Same(oldArray, PhysicalArray(pool));
        Assert.Equal(oldTextureId, oldArray.TextureId);
        Interlocked.Exchange(ref pendingFencePolls, 1);
        Interlocked.Exchange(ref observedPendingFencePolls, 0);
        var harmony = new Harmony("Tests.ShadowArrayRetirementPendingFence");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "WaitCpu"),
                prefix: new HarmonyMethod(typeof(ShadowPagePoolTests), nameof(KeepNextFencePending)));
            pool.Poll();
        }
        finally { harmony.UnpatchAll(harmony.Id); }

        // Assert: a pending withdrawal fence keeps the full old allocation alive and blocks replacement.
        Assert.Equal(0, Interlocked.Exchange(ref pendingFencePolls, 0));
        Assert.Equal(1, Interlocked.Exchange(ref observedPendingFencePolls, 0));
        Assert.False(pool.IsAvailable);
        Assert.Equal(expectedOldAllocation, pool.AllocatedBytes);
        Assert.Same(oldArray, PhysicalArray(pool));
        Assert.Equal(oldTextureId, oldArray.TextureId);
        Assert.Equal(8, pool.PageSize);
        Assert.Equal(2, pool.ArrayLayers);

        // Act: complete the real fence, then allow Poll to release the old array and create the new layout.
        RHICommandContext.Current.WaitIdle(); pool.Poll();

        // Assert: the replacement owns exactly the requested new array and old page identities are gone.
        Assert.True(pool.IsAvailable);
        Assert.Equal(16, pool.PageSize);
        Assert.Equal(3, pool.ArrayLayers);
        Assert.Equal(3, pool.DepthArray.Depth);
        Assert.Equal(ExpectedPoolBytes(pageSize: 16, tilesPerLayer: 2, layers: 3), pool.AllocatedBytes);
        Assert.Equal(0, pool.DrainingBytes);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, moon, Small, pageSize: 16)));
        Assert.Equal(1u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>Invalid replacement layout withdraws the old pool instead of retaining stale pages or retrying invisibly.</summary>
    [Fact]
    public void InvalidArrayReconfigurationLeavesPoolUnavailable()
    {
        // Arrange: create a live pool with one allocated array, then request an unsupported effective layout.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 1, layers: 1));
        Assert.Equal(1, pool.DepthArray.Depth);

        // Act: unsupported replacement fails after its old publication has been withdrawn.
        Assert.Throws<InvalidOperationException>(() => pool.Reconfigure(new ShadowPageSettings
        {
            MemoryMiB = 1, PageSize = 1024, TileColumns = 128, TileRows = 16, ArrayLayers = 8
        }));
        RHICommandContext.Current.WaitIdle(); pool.Poll();

        // Assert: no cached page storage remains available and the pool does not retry an invalid layout.
        Assert.False(pool.IsAvailable);
        Assert.Equal(0, pool.AllocatedBytes);
        Assert.False(pool.BeginBatch());
    }

    /// <summary>A physical array with more than four layers is fully allocated and page demand never resizes it.</summary>
    [Fact]
    public void FixedArrayCapacitySupportsMoreThanFourLayers()
    {
        // Arrange: configure five one-tile layers, exceeding the former four independently bound image limit.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 1, layers: 5));
        Assert.Equal(TextureDimension.TwoArray, pool.DepthArray.TextureTarget);
        Assert.Equal(5, pool.DepthArray.Depth);
        long allocatedBytes = pool.AllocatedBytes;
        int textureId = pool.DepthArray.TextureId;
        Assert.True(pool.BeginBatch());

        // Act: allocate one distinct page per physical slot while retaining the same fixed texture object.
        var assignedLayers = new List<int>();
        for (int index = 0; index < 5; index++)
        {
            var key = Key(index, 0, projection: 100 + index);
            Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, Small)));
            uint[] result = Result(pool);
            Assert.Equal(0u, result[0]);
            int slot = checked((int)result[1]);
            using var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 1, BufferMapAccess.Read);
            Assert.True(entry.IsMapped);
            assignedLayers.Add(checked((int)entry.Span[0]));
        }

        // Assert: every configured layer is reachable and demand leaves allocation bytes unchanged.
        Assert.Equal(Enumerable.Range(0, 5), assignedLayers.Order());
        Assert.Equal(textureId, pool.DepthArray.TextureId);
        Assert.Equal(allocatedBytes, pool.AllocatedBytes);
        Assert.Equal(5, pool.DepthArray.Depth);
        pool.SealBatch();
    }

    /// <summary>Failed array allocation during reconfiguration retires partial resources and leaves the pool unavailable.</summary>
    [Fact]
    public void FailedArrayRecreationReleasesPartialResources()
    {
        // Arrange: queue a valid replacement after a live array, then fail the RHI texture factory used by the pool.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tilesPerLayer: 1, layers: 1));
        Assert.Equal(ExpectedPoolBytes(pageSize: 8, tilesPerLayer: 1, layers: 1), pool.AllocatedBytes);
        pool.Reconfigure(Settings(tilesPerLayer: 1, layers: 2));
        Assert.False(pool.IsAvailable);
        Interlocked.Exchange(ref arrayCreationAttempts, 0);
        var harmony = new Harmony("Tests.ShadowArrayRecreationFailure");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "LowerTexture"),
                prefix: new HarmonyMethod(typeof(ShadowPagePoolTests), nameof(RejectArrayCreation)));

            // Act: the queued recreation reaches RHI allocation only after the old storage's fence is complete.
            RHICommandContext.Current.WaitIdle();
            Assert.Throws<ApplicationException>(() => pool.Poll());

            // Assert: the failed replacement exposes neither a partial array nor the old withdrawn allocation.
            Assert.Equal(1, Interlocked.Exchange(ref arrayCreationAttempts, 0));
            Assert.False(pool.IsAvailable);
            Assert.Equal(0, pool.AllocatedBytes);
            Assert.False(pool.BeginBatch());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Unsupported static sampler capacity and over-budget complete layouts fail before claiming allocation.</summary>
    [Fact]
    public void PagePoolRejectsUnsupportedSamplerAndMemoryLimits()
    {
        // Arrange: initialize the actual context so effective device limits are available to validation.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();

        // Act and assert: one array sampler is required and all configured capacity is charged before allocation.
        Assert.Throws<InvalidOperationException>(() => ShadowPagePool.Create(assets.Api,
            Settings(pageSize: 16, tilesPerLayer: 1, layers: 1), availableSamplerSlots: 0));
        Assert.Throws<InvalidOperationException>(() => ShadowPagePool.Create(assets.Api,
            new ShadowPageSettings { MemoryMiB = 1, PageSize = 1024, TileColumns = 128, TileRows = 16, ArrayLayers = 4 }));
        int beyondHardwareLayers = GpuSupport.Graphics.MaxArrayTextureLayers + 1;
        if (beyondHardwareLayers <= 8192)
            Assert.Throws<InvalidOperationException>(() => ShadowPagePool.Create(assets.Api,
                new ShadowPageSettings { MemoryMiB = 4096, PageSize = 1, TileColumns = 1, TileRows = 1,
                    ArrayLayers = beyondHardwareLayers }));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Returns Pending for one pool fence poll without changing the native fence or its eventual completion.</summary>
    private static bool KeepNextFencePending(ref CompletionStatus __result)
    {
        if (Interlocked.Exchange(ref pendingFencePolls, 0) == 0) return true;
        Interlocked.Increment(ref observedPendingFencePolls);
        __result = CompletionStatus.Pending;
        return false;
    }

    /// <summary>Injects failure at the non-generic RHI lowering boundary reached by physical array allocation.</summary>
    private static void RejectArrayCreation() { Interlocked.Increment(ref arrayCreationAttempts); throw new ApplicationException("Injected shadow array allocation failure."); }

    /// <summary>Creates a small fixed physical array policy for deterministic page tests.</summary>
    private static ShadowPageSettings Settings(int pageSize = 8, int tilesPerLayer = 2, int layers = 1) => new()
    { MemoryMiB = 16, PageSize = pageSize, TileColumns = tilesPerLayer, TileRows = 1, ArrayLayers = layers,
        ProbeLimit = 128, OperationsPerBatch = 256 };
    /// <summary>Builds a stable absolute source/projection/page tuple for synthetic page operations.</summary>
    private static ShadowPageKey Key(int x, int y, long projection = 5, ShadowSourceKey? source = null) =>
        new(source ?? Sun, projection, 0, x, y);
    /// <summary>Creates a page command with one controlled world and scene dependency stamp.</summary>
    private static ShadowPageRequest Request(ShadowPageOperation operation, ShadowPageKey key, ShadowBounds region,
        long assignment = 0, bool complete = false, int pageSize = 8) => new(operation, key, region, 13, 17, 19,
            assignment, complete, Projection(key, pageSize));
    /// <summary>Builds a matching stable directional snapshot for synthetic page-table certificate commands.</summary>
    private static ShadowProjectionSnapshot Projection(ShadowPageKey key, int pageSize = 8)
    {
        var projection = new DirectionalShadowProjection(key.Source, key.Projection, Vector3d.UnitZ,
            new ShadowBounds(-100, -100, -100, 100, 100, 100), 1, Math.Max(1, key.FaceOrLevel + 1), pageSize: pageSize);
        return ShadowProjectionSnapshot.Directional(projection, key.FaceOrLevel);
    }
    /// <summary>Reads only the test-visible result words after the preceding ordered dispatch.</summary>
    private static uint[] Result(ShadowPagePool pool)
    {
        using var mapped = pool.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }
    /// <summary>Observes the retained owned array during withdrawal when public consumers are intentionally blocked.</summary>
    private static GpuTexture PhysicalArray(ShadowPagePool pool) => (GpuTexture)typeof(ShadowPagePool)
        .GetField("depthArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
    /// <summary>Computes complete array and per-page metadata accounting independently from pool-reported bytes.</summary>
    private static long ExpectedPoolBytes(int pageSize, int tilesPerLayer, int layers)
    {
        long capacity = checked((long)tilesPerLayer * layers);
        return checked(capacity * (528 + (long)pageSize * pageSize * sizeof(uint)) + 608);
    }
    /// <summary>Reads state and the allocation-batch token from their exact fields in one bounded page-table slot.</summary>
    private static (uint State, uint BatchLow, uint BatchHigh) EntryLifecycle(ShadowPagePool pool, int slot)
    {
        uint state;
        using (var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + 20 * sizeof(uint)), 1, BufferMapAccess.Read))
        {
            Assert.True(mapped.IsMapped); state = mapped.Span[0];
        }
        using var batch = pool.Entries.MapRange<uint>(checked(slot * 512 + 112 * sizeof(uint)), 2, BufferMapAccess.Read);
        Assert.True(batch.IsMapped);
        return (state, batch.Span[0], batch.Span[1]);
    }
    /// <summary>Reads only a selected compact set of GPU certificate words needed by a page-state assertion.</summary>
    private static uint[] EntryWords(ShadowPagePool pool, int slot, int firstWord, int count)
    {
        using var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + firstWord * sizeof(uint)), count, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }
    /// <summary>Finds distinct serialized keys sharing a bounded linear-probe start bucket.</summary>
    private static (ShadowPageKey First, ShadowPageKey Second) CollidingKeys(int capacity)
    {
        var seen = new Dictionary<uint, ShadowPageKey>();
        for (int x = -10000; x <= 10000; x++)
        {
            var key = Key(x, 17);
            var words = Request(ShadowPageOperation.Allocate, key, Small).Pack();
            uint hash = 2166136261u;
            unchecked { for (int i = 0; i < 17; i++) hash = (hash ^ words[i]) * 16777619u; }
            uint bucket = hash % (uint)capacity;
            if (seen.TryGetValue(bucket, out var prior) && prior != key) return (prior, key);
            seen[bucket] = key;
        }
        throw new Xunit.Sdk.XunitException("Could not find a deterministic page-key probe collision.");
    }
    /// <summary>Combines GPU low/high assignment words without losing signed bit patterns.</summary>
    private static long Assignment(uint[] result) => unchecked((long)(((ulong)result[3] << 32) | result[2]));
    /// <summary>Allocates and publishes a synthetic page certificate inside the caller's open batch.</summary>
    private static long Publish(ShadowPagePool pool, ShadowPageKey key, ShadowBounds region)
    {
        var allocationRequest = Request(ShadowPageOperation.Allocate, key, region);
        if (!pool.Execute(allocationRequest))
        {
            pool.SealBatch();
            Assert.True(pool.BeginBatch());
            Assert.True(pool.Execute(allocationRequest));
        }
        var result = Result(pool); Assert.Equal(0u, result[0]);
        long assignment = Assignment(result);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Publish, key, region, assignment, complete: true)));
        Assert.Equal(0u, Result(pool)[0]); return assignment;
    }
    #endregion
}
