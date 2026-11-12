using System;
using System.Linq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks complete replacement publication and cancellation through the production page-table compute shader.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowPageCompletionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static readonly ShadowBounds Small = new(-4, -4, -4, 4, 4, 4);
    private static readonly ShadowBounds Expanded = new(-8, -8, -8, 8, 8, 8);
    private static readonly ShadowSourceKey Sun = new(7, 0, ShadowSourceKind.Sun, 11, 13, 0);

    #region Public API
    /// <summary>Publishes every frozen replacement in one GPU pass and exposes each only after batch finalization.</summary>
    [Fact]
    public void FinishPublishesAllFrozenReplacementPagesAsOneBatch()
    {
        // Arrange: reserve two resident slots and allocate two distinct, cleared pages under one dependency stamp.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 2));
        var first = Key(0, 0);
        var second = Key(1, 0);
        Assert.True(pool.BeginBatch());
        AllocateAndClear(pool, first, Small);
        AllocateAndClear(pool, second, Expanded);
        Assert.True(pool.FreezeWritableAssignments());

        // Act: finish the shared replacement interval with the dependencies captured by both allocations.
        pool.FinishReplacements(complete: true, worldRevision: 13, sceneEpoch: 17, sceneSequence: 19, timestamp: 21);
        var finished = Result(pool);

        // Assert: one success result is returned and both full-key lookups now resolve ready assignments.
        Assert.Equal(0u, finished[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, first, Small)));
        var firstResult = Result(pool);
        Assert.Equal(0u, firstResult[0]);
        Assert.True(Assignment(firstResult) > 0);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, second, Expanded)));
        var secondResult = Result(pool);
        Assert.Equal(0u, secondResult[0]);
        Assert.True(Assignment(secondResult) > 0);
        pool.SealBatch();
    }

    /// <summary>Freezes native assignment tokens before selection and performs the exact selected clear afterward.</summary>
    [Fact]
    public void FrozenPendingSelectionCanBeClearedAndCompleted()
    {
        // Arrange: allocate a projected page and retain its unpublished assignment through the freeze boundary.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 1));
        var key = Key(0, 0);
        var allocationRequest = Request(ShadowPageOperation.Allocate, key, Small);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(allocationRequest));
        uint[] allocation = Result(pool);
        Assert.Equal(0u, allocation[0]);
        long assignment = Assignment(allocation);
        Assert.True(assignment > 0);

        // Act: freeze every admitted token first, then retrieve and clear only the exact current replacement.
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.SelectReplacement(allocationRequest));
        uint[] selected = Result(pool);
        Assert.Equal(0u, selected[0]);
        Assert.Equal(assignment, Assignment(selected));
        Assert.True(pool.ClearReplacement());
        pool.FinishReplacements(complete: true, worldRevision: 13, sceneEpoch: 17,
            sceneSequence: 19, timestamp: 21);

        // Assert: the replacement becomes sampleable only after the ordered post-freeze clear and finalization.
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        uint[] lookup = Result(pool);
        Assert.Equal(0u, lookup[0]);
        Assert.Equal(assignment, Assignment(lookup));
        pool.SealBatch();
    }

    /// <summary>A frozen selected token without a completed clear cannot publish or resolve through page lookup.</summary>
    [Fact]
    public void FrozenUnclearedSelectionCannotComplete()
    {
        // Arrange: allocate a pending page but deliberately stop after exact token selection.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 1));
        var key = Key(0, 0);
        var request = Request(ShadowPageOperation.Allocate, key, Small);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(request));
        long assignment = Assignment(Result(pool));
        Assert.True(assignment > 0);
        Assert.True(pool.FreezeWritableAssignments());
        Assert.True(pool.SelectReplacement(request));
        Assert.Equal(assignment, Assignment(Result(pool)));

        // Act: completion is requested without initializing the selected physical tile.
        pool.FinishReplacements(complete: true, worldRevision: 13, sceneEpoch: 17,
            sceneSequence: 19, timestamp: 21);

        // Assert: whole-batch validation rejects the uninitialized tile and lookup remains a miss.
        Assert.Equal(4u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        pool.SealBatch();
    }

    /// <summary>A dependency or widened-demand failure abandons replacements while the old sampled page remains exact.</summary>
    [Theory]
    [InlineData(FinalizationFailure.Dependency)]
    [InlineData(FinalizationFailure.WidenedDemand)]
    public void FailedFinishPreservesOldSampledCertificate(FinalizationFailure failure)
    {
        // Arrange: publish one old page in a completed batch, then require a replacement that cannot finish correctly.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 3));
        var key = Key(0, 0);
        Assert.True(pool.BeginBatch());
        long oldAssignment = FinishInitialPage(pool, key, Small);
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle(); pool.Poll();

        // Act: start the replacement interval and widen old demand or supply a stale scene dependency at finalization.
        Assert.True(pool.BeginBatch());
        AllocateAndClear(pool, key, Small);
        Assert.True(pool.FreezeWritableAssignments());
        if (failure == FinalizationFailure.WidenedDemand)
        {
            Assert.True(pool.Execute(Request(ShadowPageOperation.Require, key, Expanded)));
            Assert.Equal(5u, Result(pool)[0]);
        }
        pool.FinishReplacements(complete: true, worldRevision: failure == FinalizationFailure.Dependency ? 14 : 13,
            sceneEpoch: 17, sceneSequence: 19, timestamp: 21);
        var rejected = Result(pool);

        // Assert: failure leaves the old near region sampleable with its original assignment and never publishes the new one.
        Assert.Equal(4u, rejected[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, key, Small)));
        var retained = Result(pool);
        Assert.Equal(0u, retained[0]);
        Assert.Equal(oldAssignment, Assignment(retained));
        pool.SealBatch();
    }

    /// <summary>Sealing without explicit completion aborts pending pages and releases their slots only after the fence.</summary>
    [Fact]
    public void SealAbortsPendingReplacementBeforeFenceRetirement()
    {
        // Arrange: allocate and clear the only physical page but leave its writable assignment unfinished.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 1));
        var abandoned = Key(0, 0);
        Assert.True(pool.BeginBatch());
        AllocateAndClear(pool, abandoned, Small);
        Assert.True(pool.FreezeWritableAssignments());

        // Act: sealing implicitly dispatches the reserved failure completion before inserting the lifetime fence.
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, abandoned, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        var replacement = Key(1, 0);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, replacement, Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(Assignment(Result(pool)) > 0);

        // Assert: the canceled page does not remain pending or block allocation after its fence signals.
        pool.SealBatch();
    }

    /// <summary>Sealing immediately after allocation aborts even an uncleared and unfrozen pending assignment.</summary>
    [Fact]
    public void SealAbortsUnclearedPendingReplacement()
    {
        // Arrange: reserve the only slot but stop before selective clear or writable-token freeze.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, Settings(tiles: 1));
        var abandoned = Key(0, 0);
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, abandoned, Small)));
        var allocation = Result(pool);
        Assert.Equal(0u, allocation[0]);
        Assert.True(Assignment(allocation) > 0);

        // Act: seal must spend the allocation-reserved finalization operation to retire this uncleared assignment.
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(Request(ShadowPageOperation.Lookup, abandoned, Small)));
        Assert.Equal(1u, Result(pool)[0]);
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, Key(1, 0), Small)));
        Assert.Equal(0u, Result(pool)[0]);
        Assert.True(Assignment(Result(pool)) > 0);

        // Assert: the abandoned uninitialized page is neither sampleable nor stranded after its fence completes.
        pool.SealBatch();
    }

    /// <summary>Allocation admission reserves enough operation capacity to run its guaranteed abort completion.</summary>
    [Fact]
    public void AllocationRejectsWhenCompletionReservationCannotFit()
    {
        // Arrange: expose only one page operation, less than the allocation plus its required finalization slot.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(tiles: 1);
        settings.OperationsPerBatch = 1;
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        Assert.True(pool.BeginBatch());

        // Act: attempt allocation under the insufficient operation budget.
        bool admitted = pool.Execute(Request(ShadowPageOperation.Allocate, Key(0, 0), Small));

        // Assert: reservation rejects before dispatch and leaves the sole operation slot untouched.
        Assert.False(admitted);
        Assert.Equal(1, pool.OperationsRemaining);
        pool.SealBatch();
    }
    #endregion

    #region Private
    /// <summary>Creates a compact pool with a configurable resident tile count and operation allowance.</summary>
    private static ShadowPageSettings Settings(int tiles) => new()
    {
        MemoryMiB = 8, PageSize = 8, TileColumns = tiles, TileRows = 1, BankCapacity = 1,
        ProbeLimit = 64, OperationsPerBatch = 64
    };

    /// <summary>Builds one source/projection/page identity used by controlled GPU allocation commands.</summary>
    private static ShadowPageKey Key(int x, int y, long projection = 5) => new(Sun, projection, 0, x, y);

    /// <summary>Creates a projected page command with stable current dependencies for the completion pass.</summary>
    private static ShadowPageRequest Request(ShadowPageOperation operation, ShadowPageKey key, ShadowBounds region,
        long assignment = 0, bool complete = false) => new(operation, key, region, 13, 17, 19,
            assignment, complete, Projection(key));

    /// <summary>Creates the frozen directional projection copied into each allocation and completion certificate.</summary>
    private static ShadowProjectionSnapshot Projection(ShadowPageKey key)
    {
        var projection = new DirectionalShadowProjection(key.Source, key.Projection, Vector3d.UnitZ,
            new ShadowBounds(-100, -100, -100, 100, 100, 100), 1, 1, 8);
        return ShadowProjectionSnapshot.Directional(projection, key.FaceOrLevel);
    }

    /// <summary>Allocates and clears one exact replacement before another command replaces the result words.</summary>
    private static long AllocateAndClear(ShadowPagePool pool, ShadowPageKey key, ShadowBounds region)
    {
        Assert.True(pool.Execute(Request(ShadowPageOperation.Allocate, key, region)));
        var allocation = Result(pool);
        Assert.Equal(0u, allocation[0]);
        long assignment = Assignment(allocation);
        Assert.True(assignment > 0);
        Assert.True(pool.ClearReplacement());
        Assert.Equal(0u, Result(pool)[0]);
        return assignment;
    }

    /// <summary>Publishes the initial retained page through the same whole-batch completion path used for replacements.</summary>
    private static long FinishInitialPage(ShadowPagePool pool, ShadowPageKey key, ShadowBounds region)
    {
        long assignment = AllocateAndClear(pool, key, region);
        Assert.True(pool.FreezeWritableAssignments());
        pool.FinishReplacements(complete: true, worldRevision: 13, sceneEpoch: 17, sceneSequence: 19, timestamp: 21);
        Assert.Equal(0u, Result(pool)[0]);
        return assignment;
    }

    /// <summary>Maps only the eight result words after a preceding page operation has completed in order.</summary>
    private static uint[] Result(ShadowPagePool pool)
    {
        using var mapped = pool.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Combines the GPU's low/high assignment words without narrowing the generation.</summary>
    private static long Assignment(uint[] result) => unchecked((long)(((ulong)result[3] << 32) | result[2]));

    /// <summary>Selects which independent completion condition the retained-page test violates.</summary>
    /// <summary>Selects the currentness condition that invalidates an otherwise complete replacement batch.</summary>
    public enum FinalizationFailure { Dependency, WidenedDemand }
    #endregion
}
