using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Compares baseline GL compute associations with an exhaustive geometric reference over tiny chunk domains.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowCandidateTraversalTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Directional and finite point searches include every reference cell while preserving bounded continuation.</summary>
    [Fact]
    public void DirectionalAndPointAssociationsMatchIndependentBoxReferences()
    {
        // Arrange: use a small negative-coordinate domain, native GL resources and deliberately small work slices.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 8, 1, 1);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var empty = new ShadowChunkIdentity(0, 0, 0, 0);
        var publication = Publication(domain, generation: 1, owner: 19, omitted: null, cachedEmpty: empty);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        var candidateSettings = Settings(headerCapacity: 11);
        candidateSettings.SegmentsPerAssociation = 2;
        using var pool = ShadowCandidatePool.Create(assets.Api, candidateSettings);
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));

        // Act: dispatch a narrow sun cap, then a point source outside the loaded domain.
        var sun = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 1), 19, 1, 1,
            domain, receiver, new Vector3d(1, 0, 0), 8);
        var sunCurrent = Current(sun, receiver, new Vector3d(1, 0, 0), sequence: 0);
        Assert.True(pool.TryAllocate(sun, out var sunHandle));
        var (sunHeader, sunDispatches) = Complete(pool, sunHandle, sunCurrent, changes, publication, world);
        var sunCells = ReadCells(pool, sunHandle.Slot, sunHeader.Count);
        var sunReference = ShadowCandidateOracle.Directional(domain, receiver, 1, 0, 0, 8);

        var sourceBox = new ShadowBounds(6.2, 0.2, 0.2, 6.8, 0.8, 0.8);
        var point = ShadowCandidateRequest.Point(Source(ShadowSourceKind.Entity, 2), 19, 1, 1,
            domain, receiver, sourceBox);
        var pointCurrent = Current(point, receiver, new Vector3d(6.5, 0.5, 0.5), sequence: 0, projection: 4);
        Assert.True(pool.TryAllocate(point, out var pointHandle));
        var (pointHeader, pointDispatches) = Complete(pool, pointHandle, pointCurrent, changes, publication, world);
        var pointCells = ReadCells(pool, pointHandle.Slot, pointHeader.Count);
        var pointReference = ShadowCandidateOracle.Point(domain, receiver, sourceBox);

        // Assert: full GPU results contain independent swept-volume requirements, deduplicate output and resolve known chunks.
        Assert.Equal(3u, sunHeader.Status);
        Assert.Equal(0u, sunHeader.Unresolved);
        Assert.Equal(sunCells.Count, (int)sunHeader.Count);
        AssertReferenceCells(sunReference, sunCells);
        Assert.Contains((empty.X, empty.Y, empty.Z), sunCells);
        Report("sun", sunHeader, sunCells.Count, sunReference.Count, sunDispatches, pool.AllocatedBytes);
        Assert.Equal(3u, pointHeader.Status);
        Assert.Equal(0u, pointHeader.Unresolved);
        Assert.Equal(pointCells.Count, (int)pointHeader.Count);
        AssertReferenceCells(pointReference, pointCells);
        Assert.DoesNotContain(pointCells, cell => cell.X >= 7);
        Report("point", pointHeader, pointCells.Count, pointReference.Count, pointDispatches, pool.AllocatedBytes);

        // Projection movement and source motion inside the declared box retain the completed spatial association.
        Assert.True(pool.BeginBatch(changes, publication, world));
        var movedInside = Current(point, receiver, new Vector3d(6.6, 0.5, 0.5), sequence: 0, projection: 99);
        Assert.True(point.Covers(movedInside));
        Assert.True(pool.Advance(pointHandle, movedInside));
        var retainedPoint = ReadHeader(pool.Headers, pointHandle.Slot);
        Assert.Equal(3u, retainedPoint.Status);
        Assert.Equal(pointHeader.GenerationLow, retainedPoint.GenerationLow);
        Assert.Equal(pointCells, ReadCells(pool, pointHandle.Slot, retainedPoint.Count));

        // A source leaving its declared motion interval withdraws completion before a replacement association is rebuilt.
        Assert.True(pool.BeginBatch(changes, publication, world));
        var movedOutside = Current(point, receiver, new Vector3d(6.9, 0.5, 0.5), sequence: 0, projection: 100);
        Assert.False(point.Covers(movedOutside));
        Assert.True(pool.Advance(pointHandle, movedOutside));
        Assert.NotEqual(3u, ReadHeader(pool.Headers, pointHandle.Slot).Status);

        var moon = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Moon, 3), 19, 1, 1,
            domain, receiver, new Vector3d(-1, 1e-8, 1e-4), 4);
        var moonCurrent = Current(moon, receiver, new Vector3d(-1, 1e-8, 1e-4), sequence: 0);
        Assert.True(pool.TryAllocate(moon, out var moonHandle));
        var (moonHeader, moonDispatches) = Complete(pool, moonHandle, moonCurrent, changes, publication, world);
        var moonCells = ReadCells(pool, moonHandle.Slot, moonHeader.Count);
        var moonReference = ShadowCandidateOracle.Directional(domain, receiver, -1, 1e-8, 1e-4, 4);
        Assert.Equal(3u, moonHeader.Status);
        Assert.Equal(0u, moonHeader.Unresolved);
        AssertReferenceCells(moonReference, moonCells);
        Report("moon-negative-near-horizontal", moonHeader, moonCells.Count, moonReference.Count,
            moonDispatches, pool.AllocatedBytes);

        var boundaryReceiver = new ShadowBounds(0, 0, 0, 1, 1, 1);
        Vector3d[] signedAxes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
            new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        for (int i = 0; i < signedAxes.Length; i++)
        {
            var direction = signedAxes[i];
            var request = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Provider, 10 + i),
                19, 1, 1, domain, boundaryReceiver, direction, 6);
            var current = Current(request, boundaryReceiver, direction, sequence: 0);
            Assert.True(pool.TryAllocate(request, out var handle));
            var (header, slices) = Complete(pool, handle, current, changes, publication, world);
            var actual = ReadCells(pool, handle.Slot, header.Count);
            var reference = ShadowCandidateOracle.Directional(domain, boundaryReceiver,
                direction.X, direction.Y, direction.Z, 6);
            Assert.Equal(3u, header.Status);
            Assert.Equal(0u, header.Unresolved);
            AssertReferenceCells(reference, actual);
            Report($"signed-axis-{i}", header, actual.Count, reference.Count, slices, pool.AllocatedBytes);
        }

        var overlappingSource = new ShadowBounds(0.5, 0.5, 0.5, 1.5, 1.5, 1.5);
        var coincidentSource = receiver;
        foreach (var (box, lifetime, label) in new[]
        {
            (overlappingSource, 31L, "point-overlap"),
            (coincidentSource, 32L, "point-coincident")
        })
        {
            var request = ShadowCandidateRequest.Point(Source(ShadowSourceKind.Entity, lifetime),
                19, 1, 1, domain, receiver, box);
            var pointPosition = new Vector3d((box.MinX + box.MaxX) * 0.5,
                (box.MinY + box.MaxY) * 0.5, (box.MinZ + box.MaxZ) * 0.5);
            var current = Current(request, receiver, pointPosition, sequence: 0);
            Assert.True(pool.TryAllocate(request, out var handle));
            var (header, slices) = Complete(pool, handle, current, changes, publication, world);
            var actual = ReadCells(pool, handle.Slot, header.Count);
            var reference = ShadowCandidateOracle.Point(domain, receiver, box);
            Assert.Equal(3u, header.Status);
            Assert.Equal(0u, header.Unresolved);
            AssertReferenceCells(reference, actual);
            Report(label, header, actual.Count, reference.Count, slices, pool.AllocatedBytes);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A cap wider than one hemisphere matches the full reference set across a bounded local domain.</summary>
    [Fact]
    public void WideDirectionalCapMatchesFullDomainReference()
    {
        // Arrange: use a 27-cell domain so a 120-degree cap's complete conservative set fits one output segment.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 1, 1, 1);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var publication = Publication(domain, 1, 28, null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(1));
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));
        var direction = new Vector3d(1, 0, 0);
        var request = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 28), 28, 1, 1,
            domain, receiver, direction, 120);
        var current = Current(request, receiver, direction, sequence: 0);

        // Act: traverse every domain slab and compare against the independent interval-box oracle.
        Assert.True(pool.TryAllocate(request, out var handle));
        var (header, dispatches) = Complete(pool, handle, current, changes, publication, world);
        var actual = ReadCells(pool, handle.Slot, header.Count);
        var reference = ShadowCandidateOracle.Directional(domain, receiver, 1, 0, 0, 120);

        // Assert: the over-hemisphere cap completes with no omitted oracle cell or unresolved world coordinate.
        Assert.Equal(3u, header.Status);
        Assert.Equal(0u, header.Unresolved);
        AssertReferenceCells(reference, actual);
        Report("wide-cap-120-degrees", header, actual.Count, reference.Count, dispatches, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Candidate traversal keeps native chunk identities exact near both signed-coordinate limits.</summary>
    [Fact]
    public void TranslatedChunkOriginsPreserveCandidateCells()
    {
        // Arrange: keep the search small while placing its integer origin near both native coordinate limits.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, int.MaxValue - 5, int.MinValue, int.MaxValue - 4,
            int.MaxValue - 1, int.MinValue + 2, int.MaxValue - 2);
        var receiver = new ShadowBounds(domain.MinX + 0.2, domain.MinY + 0.2, domain.MinZ + 0.2,
            domain.MinX + 0.8, domain.MinY + 0.8, domain.MinZ + 0.8);
        var publication = Publication(domain, generation: 1, owner: 71, omitted: null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(headerCapacity: 2));
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));
        // The finite point hull has an exact rational oracle; the directional oracle deliberately keeps cells
        // undecided at its recursion bound, so it cannot define exact membership for this identity-limit case.
        var sourceBox = new ShadowBounds(domain.MaxX + 0.2, domain.MinY + 1.2, domain.MinZ + 0.2,
            domain.MaxX + 0.8, domain.MinY + 1.8, domain.MinZ + 0.8);
        var sourcePosition = new Vector3d(domain.MaxX + 0.5, domain.MinY + 1.5, domain.MinZ + 0.5);
        var request = ShadowCandidateRequest.Point(Source(ShadowSourceKind.Entity, 91), 71, 1, 1,
            domain, receiver, sourceBox);
        var current = Current(request, receiver, sourcePosition, sequence: 0, projection: 4);

        // Act: traverse the relative search extent through the production compute shader.
        Assert.True(pool.TryAllocate(request, out var handle));
        var (header, dispatches) = Complete(pool, handle, current, changes, publication, world);
        var actual = ReadCells(pool, handle.Slot, header.Count);
        var reference = ShadowCandidateOracle.Point(domain, receiver, sourceBox);

        // Assert: output retains exact signed chunk keys and includes every independently required cell.
        Assert.Equal(3u, header.Status);
        Assert.Equal(0u, header.Unresolved);
        AssertReferenceCells(reference, actual);
        Assert.All(actual, cell => Assert.InRange(cell.X, domain.MinX, domain.MaxX));
        Assert.All(actual, cell => Assert.InRange(cell.Y, domain.MinY, domain.MaxY));
        Assert.All(actual, cell => Assert.InRange(cell.Z, domain.MinZ, domain.MaxZ));
        Report("translated-point-signed-domain", header, actual.Count, reference.Count, dispatches, pool.AllocatedBytes);
    }

    /// <summary>Unknown interior data stays unresolved until loaded, while empty and filled transitions preserve spatial candidates.</summary>
    [Fact]
    public void UnknownCellsRevalidateFromUnknownToEmptyToFilled()
    {
        // Arrange: omit one interior coordinate while retaining the complete declared envelope.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, 0, 0, 0, 3, 2, 2);
        var receiver = new ShadowBounds(0.25, 0.25, 0.25, 0.75, 0.75, 0.75);
        var missing = new ShadowChunkIdentity(0, 1, 1, 1);
        var publication = Publication(domain, generation: 2, owner: 20, omitted: missing);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(headerCapacity: 2));
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));
        var request = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 3), 20, 1, 1,
            domain, receiver, new Vector3d(1, 0.2, -0.1), 65);
        var current = Current(request, receiver, new Vector3d(1, 0.2, -0.1), sequence: 0);

        // Act: process a 36-cell cap whose required set fits the 64-coordinate output span.
        Assert.True(pool.TryAllocate(request, out var handle));
        var (header, dispatches) = Complete(pool, handle, current, changes, publication, world);
        var reference = ShadowCandidateOracle.Directional(domain, receiver, 1, 0.2, -0.1, 65);
        var actual = ReadCells(pool, handle.Slot, header.Count);

        // Assert: an unknown lookup blocks drawable completeness, while every later publication revalidates the same coordinates.
        Assert.Equal(3u, header.Status);
        Assert.True(header.Unresolved > 0);
        AssertReferenceCells(reference, actual);
        Assert.Contains((missing.X, missing.Y, missing.Z), actual);
        Report("unknown-interior", header, (int)header.Count, reference.Count, dispatches, pool.AllocatedBytes);

        // Replacing unknown data with a proven-empty chunk and then occupied geometry revalidates dependencies,
        // while the spatial coordinate list itself remains unchanged because this path never prunes empties.
        {
            var provenEmpty = Publication(domain, generation: 3, owner: 20, omitted: null, cachedEmpty: missing);
            Assert.True(world.Upload(provenEmpty));
            Assert.True(pool.BeginBatch(changes, provenEmpty, world));
            Assert.True(pool.Advance(handle, current));
            var (emptyHeader, _) = Complete(pool, handle, current, changes, provenEmpty, world);
            var emptyCells = ReadCells(pool, handle.Slot, emptyHeader.Count);
            Assert.Equal(3u, emptyHeader.Status);
            Assert.True(actual.SetEquals(emptyCells));
            Assert.Contains((missing.X, missing.Y, missing.Z), emptyCells);
            Assert.Equal(0u, emptyHeader.Unresolved);

            var filled = Publication(domain, generation: 4, owner: 20, omitted: null);
            Assert.True(world.Upload(filled));
            Assert.True(pool.BeginBatch(changes, filled, world));
            Assert.True(pool.Advance(handle, current));
            var (filledHeader, _) = Complete(pool, handle, current, changes, filled, world);
            var filledCells = ReadCells(pool, handle.Slot, filledHeader.Count);
            Assert.Equal(3u, filledHeader.Status);
            Assert.True(emptyCells.SetEquals(filledCells));
            Assert.Contains((missing.X, missing.Y, missing.Z), filledCells);
            Assert.Equal(0u, filledHeader.Unresolved);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Configured storage, hash and header ceilings reject work explicitly instead of truncating an association.</summary>
    [Fact]
    public void MemoryHeaderAndOutputBudgetsRemainFinite()
    {
        // Arrange: initialize a real GL 4.5 context and prove oversized fixed layouts fail before shader/resource creation.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        Assert.Throws<InvalidOperationException>(() => ShadowCandidatePool.Create(assets.Api, new ShadowCandidateSettings
        {
            MemoryMiB = 1, HeaderCapacity = 2, SegmentsPerAssociation = 1024
        }));

        var domain = new ShadowChunkDomain(0, -3, -3, -3, 3, 3, 3);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var publication = Publication(domain, generation: 4, owner: 22, omitted: null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(headerCapacity: 1));
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.False(pool.BeginBatch(new(1, 0, 1, false, ImmutableArray<ShadowChunkIdentity>.Empty), publication, world));
        var excessiveHistory = Enumerable.Repeat(new ShadowChunkIdentity(0, 0, 0, 0), 129).ToImmutableArray();
        Assert.False(pool.BeginBatch(new(1, 0, 129, true, excessiveHistory), publication, world));
        Assert.True(pool.BeginBatch(changes, publication, world));
        var broad = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 4), 22, 1, 1,
            domain, receiver, new Vector3d(1, 0, 0), 179);
        var current = Current(broad, receiver, new Vector3d(1, 0, 0), sequence: 0);

        // Act: allocate exactly the one configured header and request a near-spherical search over 343 cells.
        Assert.True(pool.TryAllocate(broad, out var handle));
        Assert.False(pool.TryAllocate(ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Moon, 5),
            22, 1, 1, domain, receiver, new Vector3d(-1, 0, 0), 5), out _));
        var (header, dispatches) = Complete(pool, handle, current, changes, publication, world);

        // Assert: the fixed segmented output ceiling returns an explicit incomplete status with no shortened success claim.
        Assert.Equal(4u, header.Status);
        Assert.Equal((uint)pool.EntryCapacity, header.Count);
        Report("output-capacity", header, (int)header.Count, null, dispatches, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Aggregate per-batch work reservations leave later light slots pending when the shared slice is consumed.</summary>
    [Fact]
    public void WholeBatchWorkAllowanceIsSharedAcrossSlots()
    {
        // Arrange: two distinct light associations share a batch with capacity for only one dispatch slice.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 1, 1, 1);
        var receiver = new ShadowBounds(-0.8, 0.2, 0.2, -0.2, 0.8, 0.8);
        var publication = Publication(domain, generation: 5, owner: 23, omitted: null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        var policy = Settings(headerCapacity: 2);
        policy.SlabsPerBatch = 1; policy.CellsPerBatch = 1; policy.ProbesPerBatch = 128; policy.ChangesPerBatch = 16;
        policy.SlabsPerSlice = 1; policy.CellsPerSlice = 1; policy.ProbesPerSlice = 128; policy.ChangesPerSlice = 16;
        using var pool = ShadowCandidatePool.Create(assets.Api, policy);
        var sun = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 6), 23, 1, 1,
            domain, receiver, new Vector3d(1, 0, 0), 5);
        var moon = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Moon, 7), 23, 1, 1,
            domain, receiver, new Vector3d(-1, 0, 0), 5);
        Assert.True(pool.TryAllocate(sun, out var sunHandle));
        Assert.True(pool.TryAllocate(moon, out var moonHandle));
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));

        // Act: reserve the sole finite slice for sun, then schedule moon in the same aggregate batch.
        Assert.True(pool.Advance(sunHandle, Current(sun, receiver, new Vector3d(1, 0, 0), sequence: 0)));
        Assert.True(pool.Advance(moonHandle, Current(moon, receiver, new Vector3d(-1, 0, 0), sequence: 0)));
        var moonHeader = ReadHeader(pool.Headers, moonHandle.Slot);

        // A resumed request whose domain generation changed while paused must be withdrawn immediately.
        Assert.True(pool.BeginBatch(changes, publication, world));
        var changedDomain = Current(sun, receiver, new Vector3d(1, 0, 0), sequence: 0) with { DomainGeneration = 2 };
        Assert.True(pool.Advance(sunHandle, changedDomain));
        var staleSun = ReadHeader(pool.Headers, sunHandle.Slot);

        // Assert: the second slot remains explicitly pending; replenishing a new batch allows its continuation.
        Assert.Equal(0u, moonHeader.Status);
        Assert.Equal(0u, moonHeader.CellVisits);
        Assert.Equal(1u, ReadHeader(pool.Headers, sunHandle.Slot).CellVisits);
        Assert.Equal(5u, staleSun.Status);
        var (completedMoon, dispatches) = Complete(pool, moonHandle,
            Current(moon, receiver, new Vector3d(-1, 0, 0), sequence: 0), changes, publication, world);
        Assert.Equal(3u, completedMoon.Status);
        Assert.Equal(0u, completedMoon.Unresolved);
        Report("moon-after-shared-budget", completedMoon, (int)completedMoon.Count, null, dispatches, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A collision beyond the configured dedup probe ceiling leaves the candidate generation incomplete.</summary>
    [Fact]
    public void DedupProbeLimitRejectsUnresolvedCollision()
    {
        // Arrange: 257 exact point-path cells must share only 128 hash slots, while each slice probes once.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, 0, 0, 0, 256, 0, 0);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var source = new ShadowBounds(256.2, 0.2, 0.2, 256.8, 0.8, 0.8);
        var publication = Publication(domain, generation: 6, owner: 24, omitted: null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        var policy = Settings(headerCapacity: 1);
        policy.SegmentsPerAssociation = 8;
        policy.DedupCapacity = 128;
        policy.ProbeLimit = 1;
        using var pool = ShadowCandidatePool.Create(assets.Api, policy);
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));
        var request = ShadowCandidateRequest.Point(Source(ShadowSourceKind.Entity, 8), 24, 1, 1,
            domain, receiver, source);
        var current = Current(request, receiver, new Vector3d(256.5, 0.5, 0.5), sequence: 0);

        // Act: exhaust the one-probe dedup policy on this finite, fully represented domain.
        Assert.True(pool.TryAllocate(request, out var handle));
        var (header, dispatches) = Complete(pool, handle, current, changes, publication, world);
        var required = ShadowCandidateOracle.Point(domain, receiver, source);

        // Assert: a bounded hash collision fails before all required cells can be published as a complete result.
        Assert.Equal(4u, header.Status);
        Assert.InRange(header.Count, 1u, 128u);
        Assert.True(header.Count < (uint)required.Count);
        Report("dedup-probe-limit", header, (int)header.Count, required.Count, dispatches, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A real hash collision pauses with a live probe cursor, then resumes to the complete independent cell set.</summary>
    [Fact]
    public void DedupCollisionProbeResumesAcrossSlices()
    {
        // Arrange: 64 domain cells fit the table, while two known cells share one bucket and one probe is charged per slice.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, 0, 0, 0, 3, 3, 3);
        var receiver = new ShadowBounds(0, 0, 0, 4, 4, 4);
        var source = receiver;
        var publication = Publication(domain, 1, 42, null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        var policy = Settings(1);
        policy.SegmentsPerAssociation = 1;
        policy.DedupCapacity = 128;
        policy.ProbeLimit = 128;
        policy.ProbesPerSlice = 1;
        using var pool = ShadowCandidatePool.Create(assets.Api, policy);
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(changes, publication, world));
        var request = ShadowCandidateRequest.Point(Source(ShadowSourceKind.Entity, 42), 42, 1, 1,
            domain, receiver, source);
        var current = Current(request, receiver, new Vector3d(2, 2, 2), sequence: 0);
        Assert.True(pool.TryAllocate(request, out var handle));
        Assert.Equal(Hash(0, 0, 0, 1) & 127u, Hash(0, 3, 3, 3) & 127u);

        // Act: resume fixed slices until a collided bucket leaves the shader inside probe resolution.
        ShadowCandidateHeader paused = default;
        int dispatches = 0;
        for (; dispatches < 4096; dispatches++)
        {
            Assert.True(pool.BeginBatch(changes, publication, world));
            Assert.True(pool.Advance(handle, current));
            paused = ReadHeader(pool.Headers, handle.Slot);
            if (paused.Stage == 2 && paused.ProbeCursor > 0) break;
            Assert.True(paused.Status is 0 or 1);
        }

        // Assert the pause is a real occupied-bucket continuation, then finish without losing any required coordinates.
        Assert.True(dispatches < 4096, "The fixture did not encounter a dedup collision within its bounded traversal.");
        Assert.Equal(1u, paused.Status);
        Assert.Equal(2u, paused.Stage);
        Assert.True(paused.ProbeCursor > 0);
        int priorDispatches = dispatches + 1;
        ShadowCandidateHeader completed = paused;
        for (int attempt = 0; attempt < 8192 && completed.Status is not (3 or 4 or 5); attempt++)
        {
            Assert.True(pool.BeginBatch(changes, publication, world));
            Assert.True(pool.Advance(handle, current));
            completed = ReadHeader(pool.Headers, handle.Slot);
            priorDispatches++;
        }
        var required = ShadowCandidateOracle.Point(domain, receiver, source);
        var actual = ReadCells(pool, handle.Slot, completed.Count);
        Assert.Equal(3u, completed.Status);
        AssertReferenceCells(required, actual);
        Assert.Equal((int)completed.Count, actual.Count);
        Report("dedup-collision-resumed", completed, actual.Count, required.Count, priorDispatches, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Retired GPU slots wait for their fence and recycled handles cannot address a newer generation.</summary>
    [Fact]
    public void RetiredSlotReallocationAdvancesGeneration()
    {
        // Arrange: create one slot and retain its handle across an explicit retirement fence.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(1));
        var domain = new ShadowChunkDomain(0, 0, 0, 0, 0, 0, 0);
        var bounds = new ShadowBounds(0.1, 0.1, 0.1, 0.9, 0.9, 0.9);
        var request = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 40), 40, 1, 1,
            domain, bounds, new Vector3d(1, 0, 0), 5);
        var current = Current(request, bounds, new Vector3d(1, 0, 0), sequence: 0);
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        var publication = Publication(domain, 1, 40, null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        Assert.True(pool.TryAllocate(request, out var oldHandle));
        Assert.True(pool.BeginBatch(changes, publication, world));
        var (oldHeader, _) = Complete(pool, oldHandle, current, changes, publication, world);
        var oldCells = ReadCells(pool, oldHandle.Slot, oldHeader.Count);
        Assert.Equal(3u, oldHeader.Status);

        // Act: retire a completed association, wait for GPU users, then allocate and complete its replacement.
        pool.Retire(oldHandle);
        RHICommandContext.Current.WaitIdle();
        Assert.True(pool.TryAllocate(request, out var newHandle));
        Assert.True(pool.BeginBatch(changes, publication, world));
        var (newHeader, _) = Complete(pool, newHandle, current, changes, publication, world);
        var newCells = ReadCells(pool, newHandle.Slot, newHeader.Count);

        // Assert: reuse gets a fresh identity, preserves the correct spatial output, and rejects old-handle mutation.
        Assert.Equal(oldHandle.Slot, newHandle.Slot);
        Assert.True(newHandle.Generation > oldHandle.Generation);
        Assert.Equal(3u, newHeader.Status);
        Assert.True(oldCells.SetEquals(newCells));
        Assert.Throws<InvalidOperationException>(() => pool.Advance(oldHandle,
            current));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A changed-partition interval longer than one slice pauses at its cursor and resumes without losing history.</summary>
    [Fact]
    public void ChangedPartitionBacklogResumesAcrossSlices()
    {
        // Arrange: complete one small association, then provide three contiguous changed chunks with one check per slice.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 1, 1, 1);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var publication = Publication(domain, 1, 41, null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        var policy = Settings(1);
        policy.ChangesPerSlice = 1;
        using var pool = ShadowCandidatePool.Create(assets.Api, policy);
        var direction = new Vector3d(1, 0, 0);
        var request = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 41), 41, 1, 1,
            domain, receiver, direction, 5);
        Assert.True(pool.TryAllocate(request, out var handle));
        var initial = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(initial, publication, world));
        Assert.Equal(3u, Complete(pool, handle, Current(request, receiver, direction, 0), initial,
            publication, world).Header.Status);
        var changed = new ShadowCandidateChanges(1, 0, 3, true,
            ImmutableArray.Create(new ShadowChunkIdentity(0, -1, 0, 0),
                new ShadowChunkIdentity(0, 0, 0, 0), new ShadowChunkIdentity(0, 1, 0, 0)));
        var current = Current(request, receiver, direction, 3);

        // Act: consume one list entry, inspect the pending cursor, then continue the same frozen interval twice.
        Assert.True(pool.BeginBatch(changed, publication, world));
        Assert.True(pool.Advance(handle, current));
        var first = ReadHeader(pool.Headers, handle.Slot);
        Assert.True(pool.BeginBatch(changed, publication, world));
        Assert.True(pool.Advance(handle, current));
        var second = ReadHeader(pool.Headers, handle.Slot);
        Assert.True(pool.BeginBatch(changed, publication, world));
        var (completed, dispatches) = Complete(pool, handle, current, changed, publication, world);

        // Assert: the GPU retains an intermediate cursor and eventually validates the complete watermark.
        Assert.Equal(2u, first.Status);
        Assert.Equal(1u, first.ChangeCursor);
        Assert.Equal(2u, second.ChangeCursor);
        Assert.Equal(3u, completed.Status);
        Assert.Equal(3u, completed.ChangeCursor);
        Report("changed-list-resumed", completed, (int)completed.Count, null, dispatches + 2, pool.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Sequence or native world generation rollback withdraws a previously completed association.</summary>
    [Fact]
    public void ChangeAndWorldGenerationRollbackInvalidateReuse()
    {
        // Arrange: complete two independent sources against one world and one initial change watermark.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        ReportCapabilities();
        using var assets = new BinaryShaderApiFixture();
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 2, 1, 1);
        var receiver = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var publication = Publication(domain, generation: 7, owner: 25, omitted: null);
        using var world = new ShadowWorldGpuBuffers();
        Assert.True(world.Upload(publication));
        using var pool = ShadowCandidatePool.Create(assets.Api, Settings(headerCapacity: 2));
        var initial = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(initial, publication, world));
        var sun = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Sun, 9), 25, 1, 1,
            domain, receiver, new Vector3d(1, 0, 0), 8);
        var moon = ShadowCandidateRequest.Directional(Source(ShadowSourceKind.Moon, 10), 25, 1, 1,
            domain, receiver, new Vector3d(-1, 0, 0), 8);
        Assert.True(pool.TryAllocate(sun, out var sunHandle));
        Assert.True(pool.TryAllocate(moon, out var moonHandle));
        var sunCurrent = Current(sun, receiver, new Vector3d(1, 0, 0), sequence: 0);
        var moonCurrent = Current(moon, receiver, new Vector3d(-1, 0, 0), sequence: 0);
        Assert.Equal(3u, Complete(pool, sunHandle, sunCurrent, initial, publication, world).Header.Status);
        Assert.Equal(3u, Complete(pool, moonHandle, moonCurrent, initial, publication, world).Header.Status);

        // Act: advance the sun's complete change watermark, then present both a change-watermark and world-generation rollback.
        var advanced = new ShadowCandidateChanges(1, 0, 1, true,
            ImmutableArray.Create(new ShadowChunkIdentity(0, 0, 0, 0)));
        Assert.True(pool.BeginBatch(advanced, publication, world));
        var sunAtSequenceOne = Current(sun, receiver, new Vector3d(1, 0, 0), sequence: 1);
        Assert.True(pool.Advance(sunHandle, sunAtSequenceOne));
        var (advancedHeader, _) = Complete(pool, sunHandle, sunAtSequenceOne, advanced, publication, world);
        Assert.Equal(3u, advancedHeader.Status);
        Assert.Equal(1u, advancedHeader.SequenceLow);

        var rollback = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        Assert.True(pool.BeginBatch(rollback, publication, world));
        Assert.True(pool.Advance(sunHandle, sunCurrent));
        var staleSequence = ReadHeader(pool.Headers, sunHandle.Slot);

        var olderWorld = Publication(domain, generation: 6, owner: 25, omitted: null);
        Assert.True(world.Upload(olderWorld));
        Assert.True(pool.BeginBatch(initial, olderWorld, world));
        Assert.True(pool.Advance(moonHandle, moonCurrent));
        var staleWorld = ReadHeader(pool.Headers, moonHandle.Slot);

        // Assert: neither an older dependency watermark nor a reused native publication generation can retain stale selection.
        Assert.Equal(5u, staleSequence.Status);
        Assert.Equal(5u, staleWorld.Status);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Creates a small but separately charged candidate allocation with bounded continuation work.</summary>
    private static ShadowCandidateSettings Settings(int headerCapacity) => new()
    {
        MemoryMiB = 8,
        HeaderCapacity = headerCapacity,
        SegmentsPerAssociation = 1,
        DedupCapacity = 128,
        ProbeLimit = 128,
        ChangeCapacity = 128,
        SlabsPerSlice = 1,
        CellsPerSlice = 3,
        ProbesPerSlice = 128,
        ChangesPerSlice = 16,
        SlabsPerBatch = 256,
        CellsPerBatch = 8192,
        ProbesPerBatch = 32768,
        ChangesPerBatch = 128
    };

    /// <summary>Builds a complete coordinate table with deterministic open addressing and an optional unknown interior key.</summary>
    private static ShadowWorldPublication Publication(ShadowChunkDomain domain, long generation, long owner,
        ShadowChunkIdentity? omitted, ShadowChunkIdentity? cachedEmpty = null)
    {
        var cells = new List<ShadowChunkGpu>();
        for (int z = domain.MinZ; z <= domain.MaxZ; z++)
        for (int y = domain.MinY; y <= domain.MaxY; y++)
        for (int x = domain.MinX; x <= domain.MaxX; x++)
        {
            if (omitted == new ShadowChunkIdentity(domain.Dimension, x, y, z)) continue;
            cells.Add(new ShadowChunkGpu
            {
                X = x, Y = y, Z = z, Dimension = domain.Dimension,
                NativeLow = (uint)cells.Count + 1, LifetimeLow = 1, RevisionLow = 1,
                Flags = cachedEmpty == new ShadowChunkIdentity(domain.Dimension, x, y, z) ? 13u : 3u
            });
        }
        int tableSize = 1;
        while (tableSize < cells.Count * 2) tableSize <<= 1;
        var table = new ShadowChunkGpu[tableSize];
        foreach (var cell in cells)
        {
            uint slot = Hash(cell.Dimension, cell.X, cell.Y, cell.Z) & (uint)(tableSize - 1);
            while (table[slot].Flags != 0) slot = (slot + 1) & (uint)(tableSize - 1);
            table[slot] = cell;
        }
        var bounds = domain.Bounds;
        var envelopes = ImmutableDictionary<int, ShadowBounds>.Empty.Add(domain.Dimension,
            new(bounds.MinX * 32, bounds.MinY * 32, bounds.MinZ * 32,
                bounds.MaxX * 32, bounds.MaxY * 32, bounds.MaxZ * 32));
        return new(generation, true, null, ImmutableArray.Create(table), ImmutableArray<ShadowGeometryRange>.Empty,
            envelopes, tableSize) { Owner = owner };
    }

    /// <summary>Advances fixed GPU slices until a terminal status appears, without reading selection on the CPU during dispatch.</summary>
    private static (ShadowCandidateHeader Header, int Dispatches) Complete(ShadowCandidatePool pool,
        ShadowCandidateHandle handle, ShadowCandidateCurrent current, ShadowCandidateChanges changes,
        ShadowWorldPublication publication, ShadowWorldGpuBuffers world)
    {
        int dispatches = 0;
        for (int attempt = 0; attempt < 512; attempt++)
        {
            uint status = ReadHeader(pool.Headers, handle.Slot).Status;
            if (status is 3 or 4 or 5) return (ReadHeader(pool.Headers, handle.Slot), dispatches);
            Assert.True(pool.BeginBatch(changes, publication, world));
            Assert.True(pool.Advance(handle, current), "A valid candidate batch should reserve its next bounded GPU slice.");
            dispatches++;
        }
        throw new Xunit.Sdk.XunitException("Candidate traversal exceeded the test's finite dispatch allowance.");
    }

    /// <summary>Records actual GPU identity, baseline limits, and selection work without implying performance acceptance.</summary>
    private void ReportCapabilities()
    {
        var caps = GpuSupport.Graphics;
        output.WriteLine($"GL vendor={caps.VendorString}; renderer={caps.RendererString}; version={caps.VersionString}; GLSL={caps.ShadingLanguageVersionString}");
        output.WriteLine($"GL limits: SSBO bindings={caps.MaxShaderStorageBufferBindings}; compute SSBO blocks={caps.MaxComputeShaderStorageBlocks}; SSBO bytes={caps.MaxShaderStorageBlockSize}; baseline dispatch=compute/storage only; draw-parameters, indirect-count, bindless, vertex/TES layer output unused");
    }

    /// <summary>Reports reference cells, false positives, bounded traversal visits and owned storage for one association.</summary>
    private void Report(string name, ShadowCandidateHeader header, int actual, int? required, int dispatches, long bytes)
    {
        string falsePositives = required.HasValue && header.Status == 3
            ? Math.Max(0, actual - required.Value).ToString() : "not measured";
        output.WriteLine($"{name}: status={header.Status}; required reference cells={required?.ToString() ?? "not measured"}; actual coordinates={actual}; false positives={falsePositives}; unresolved={header.Unresolved}; dispatches={dispatches}; slab visits={header.SlabVisits}; cell visits={header.CellVisits}; probe visits={header.ProbeVisits}; duplicates={header.Duplicates}; allocated bytes={bytes}");
    }

    /// <summary>Reads one published header only after the integration test has ordered the preceding GPU dispatch.</summary>
    private static ShadowCandidateHeader ReadHeader(GpuShaderStorageBuffer buffer, int slot)
    {
        using var mapped = buffer.MapRange<ShadowCandidateHeader>(slot * 128, 1, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    /// <summary>Copies exactly one slot's deduplicated coordinates for the independent oracle comparison.</summary>
    private static HashSet<(int X, int Y, int Z)> ReadCells(ShadowCandidatePool pool, int slot, uint count)
    {
        using var mapped = pool.Coordinates.MapRange<int>(slot * pool.EntryCapacity * 16,
            checked((int)count * 4), BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        var cells = new HashSet<(int, int, int)>();
        for (int i = 0; i < count; i++)
        {
            int offset = i * 4;
            Assert.Equal(0, mapped.Span[offset + 3]);
            Assert.True(cells.Add((mapped.Span[offset], mapped.Span[offset + 1], mapped.Span[offset + 2])),
                "A published association contains duplicate coordinates.");
        }
        return cells;
    }

    /// <summary>Creates the exact identity and dependency tuple expected by an anchored request.</summary>
    private static ShadowCandidateCurrent Current(ShadowCandidateRequest request, ShadowBounds receiver,
        Vector3d position, long sequence, long projection = 1) => new(request.Source, request.WorldOwner,
            request.DomainGeneration, request.InfluenceGeneration, receiver, position, request.ChangeEpoch,
            sequence, projection);

    /// <summary>Reports any oracle-required cells omitted from a completed GPU association.</summary>
    private static void AssertReferenceCells(HashSet<(int X, int Y, int Z)> required,
        HashSet<(int X, int Y, int Z)> actual)
    {
        var missing = required.Except(actual).ToArray();
        Assert.True(missing.Length == 0, $"Missing required chunk cells: {string.Join(", ", missing)}");
    }

    /// <summary>Builds deterministic stable source keys for distinct local and celestial associations.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long lifetime) =>
        new(1, 0, kind, lifetime, lifetime, 0);

    /// <summary>Matches the world publication's explicit wrapping FNV coordinate hash.</summary>
    private static uint Hash(int dimension, int x, int y, int z)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)dimension) * 16777619;
            hash = (hash ^ (uint)x) * 16777619;
            hash = (hash ^ (uint)y) * 16777619;
            return (hash ^ (uint)z) * 16777619;
        }
    }
    #endregion
}
