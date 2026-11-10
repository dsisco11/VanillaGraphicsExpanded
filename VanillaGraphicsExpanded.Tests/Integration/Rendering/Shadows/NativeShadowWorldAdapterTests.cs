using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.Common;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises bounded publication against actual engine chunk dictionaries and geometry references.</summary>
[Collection("CPU state")]
public sealed class NativeShadowWorldAdapterTests
{
    #region Public API
    /// <summary>Bootstrap preserves native dimensions, offscreen chunk ranges and the enclosing loaded envelope.</summary>
    [Fact]
    public void BootstrapPublishesNativeChunksRangesAndDimensionEnvelope()
    {
        // Arrange: create offscreen native chunks at distinct extrema and one intermediate coordinate.
        var map = CreateMap(4);
        long firstIndex = map.ChunkIndex3D(2, 3, 4, 0);
        long secondIndex = map.ChunkIndex3D(9, 6, 10, 0);
        long thirdIndex = map.ChunkIndex3D(5, 17, 7, 1);
        var first = CreateChunk([CreateLocation(2, 12, 18, 3, 14, 5)]);
        var second = CreateChunk([CreateLocation(4, 22, 25, 7, 27, 9)]);
        var third = CreateChunk([CreateLocation(6, 28, 30, 31, 33, 1)]);
        NativeChunks(map).Add(firstIndex, first);
        NativeChunks(map).Add(secondIndex, second);
        NativeChunks(map).Add(thirdIndex, third);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);

        // Act: bootstrap directly from the engine-owned dictionary and chunk range arrays.
        var publication = adapter.Capture();

        // Assert: source references remain represented by coordinates and ranges without mesh copies or pool scans.
        Assert.True(publication.Complete, publication.Failure);
        Assert.Equal(3, publication.Table.Count(entry => entry.Flags != 0));
        Assert.Equal(3, publication.Ranges.Length);
        var firstPosition = map.ChunkPosFromChunkIndex3D(firstIndex);
        var secondPosition = map.ChunkPosFromChunkIndex3D(secondIndex);
        var thirdPosition = map.ChunkPosFromChunkIndex3D(thirdIndex);
        Assert.Equal((0, 2, 3, 4), (firstPosition.Dimension, firstPosition.X, firstPosition.Y, firstPosition.Z));
        Assert.Equal((0, 9, 6, 10), (secondPosition.Dimension, secondPosition.X, secondPosition.Y, secondPosition.Z));
        Assert.Equal((1, 5, 17, 7), (thirdPosition.Dimension, thirdPosition.X, thirdPosition.Y, thirdPosition.Z));
        Assert.True(publication.TryFind(new(firstPosition.Dimension, firstPosition.X, firstPosition.Y, firstPosition.Z), out var firstEntry));
        Assert.Equal((uint)1, firstEntry.RangeCount);
        Assert.Equal(2, publication.Ranges[(int)firstEntry.RangeOffset].PoolId);
        Assert.Equal(new ShadowBounds(64, 96, 128, 320, 224, 352), publication.Envelopes[0]);
        Assert.Equal(new ShadowBounds(160, 544, 224, 192, 576, 256), publication.Envelopes[1]);
        var missingInterior = new ShadowChunkIdentity(0, 5, 5, 7);
        var missingBounds = new ShadowBounds(160, 160, 224, 192, 192, 256);
        Assert.False(publication.TryFind(missingInterior, out _));
        Assert.True(publication.Envelopes[0].Contains(missingBounds));
        Assert.Equal(64L * publication.Table.Length + 32L * publication.Ranges.Length, publication.PackedBytes);
    }

    /// <summary>Delta publication withdraws on a pending mutation and replaces old chunk lifetimes after edits.</summary>
    [Fact]
    public void DeltaAndMutationBoundariesRejectStaleNativeReferences()
    {
        // Arrange: bootstrap one live native chunk and retain its completed publication.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(1, 2, 3, 0);
        var original = CreateChunk([CreateLocation(1, 0, 3, 3, 4, 2)]);
        NativeChunks(map).Add(index, original);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        var prior = adapter.Capture();
        bool submitted = false;
        Assert.True(adapter.TryUse(prior, () => submitted = true));
        Assert.True(submitted);

        // Act: withdraw before native range retirement, replace the dictionary value, and publish its delta.
        adapter.BeginMutation();
        NativeChunks(map)[index] = CreateChunk([CreateLocation(9, 40, 44, 50, 55, 10)]);
        adapter.Changed(index);
        var duringMutation = adapter.Capture();
        adapter.EndMutation(false);
        var current = adapter.Capture();

        // Assert: old certificates cannot submit during replacement and current ranges belong to the replacement.
        Assert.False(duringMutation.Complete);
        Assert.False(adapter.TryUse(prior, () => throw new InvalidOperationException("Stale range submitted.")));
        Assert.True(current.Complete, current.Failure);
        Assert.NotEqual(prior.Generation, current.Generation);
        Assert.Equal(9, current.Ranges[0].PoolId);
    }

    /// <summary>Overflow leaves the whole loaded envelope incomplete rather than publishing a truncated domain.</summary>
    [Fact]
    public void LoadedDomainCapacityFailureDoesNotTruncatePublication()
    {
        // Arrange: the native dictionary has more entries than this adapter may describe.
        var map = CreateMap(1);
        NativeChunks(map).Add(map.ChunkIndex3D(0, 0, 0, 0), CreateChunk([]));
        NativeChunks(map).Add(map.ChunkIndex3D(1, 0, 0, 0), CreateChunk([]));
        using var adapter = new NativeShadowWorldAdapter(map, 1, 4, 4, 4);

        // Act: attempt one bounded full bootstrap.
        var publication = adapter.Capture();

        // Assert: unavailable coverage remains distinguishable from a complete empty or partial domain.
        Assert.False(publication.Complete);
        Assert.Contains("capacity", publication.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(publication.Table);
        Assert.Empty(publication.Envelopes);
    }

    /// <summary>Missing interior coordinates and stale native connectivity stay unresolved and unpruned.</summary>
    [Fact]
    public void MissingCellAndConnectivityCannotProveAnEmptyPath()
    {
        // Arrange: one chunk has a closed-looking but stale face-connectivity value and one neighboring cell is absent.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(1, 0, 0, 0);
        var chunk = CreateChunk([]);
        SetField(typeof(ClientChunk), chunk, "traversability", (ushort)0);
        SetField(typeof(ClientChunk), chunk, "traversabilityFresh", false);
        NativeChunks(map).Add(index, chunk);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 4, 4, 4);

        // Act: publish the real chunk metadata, then query the absent adjacent coordinate.
        var publication = adapter.Capture();
        var position = map.ChunkPosFromChunkIndex3D(index);
        bool foundNeighbor = publication.TryFind(new(0, position.X + 1, position.Y, position.Z), out _);
        bool foundSource = publication.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var value);

        // Assert: cached bits are diagnostic only; absent and stale cells cannot authorize pruning.
        Assert.False(foundNeighbor);
        Assert.True(foundSource);
        Assert.Equal(0u, value.Flags & 8u);
        Assert.Equal(0u, value.Flags & 16u);
        Assert.True(publication.Complete);
    }

    /// <summary>Out-of-order native uploads preserve per-part revisions and reject overwritten newer geometry.</summary>
    [Fact]
    public void DirtyUploadsRequireLatestRevisionAndAllowIndependentEdgeReplacement()
    {
        // Arrange: bootstrap a loaded native chunk and record a dirty edit for its coordinate.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(2, 1, 3, 0);
        var chunk = CreateChunk([CreateLocation(4, 10, 14, 20, 24, 6)]);
        NativeChunks(map).Add(index, chunk);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        Assert.True(adapter.Capture().Complete);
        adapter.Dirty(index);

        // Act: capture two successive edit revisions and let the older tessellation upload first.
        var dirty = adapter.Capture();
        var position = map.ChunkPosFromChunkIndex3D(index);
        Assert.True(dirty.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var stale));
        var r1 = adapter.ContentRevisions(index);
        adapter.Dirty(index);
        var r2 = adapter.ContentRevisions(index);
        adapter.BeginMutation();
        SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations",
            new ModelDataPoolLocation[] { CreateLocation(7, 30, 34, 40, 44, 3) });
        adapter.UploadFinished(index, r1.Center, r1.Edge, false, chunk);
        var firstUpload = adapter.Capture();
        Assert.True(firstUpload.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var firstResult));
        Assert.Equal(0u, firstResult.Flags & 2u);
        Assert.Empty(firstUpload.Ranges);

        // Complete r2, then prove that a late r1 overwrite withdraws readiness again.
        adapter.BeginMutation();
        SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations",
            new ModelDataPoolLocation[] { CreateLocation(8, 40, 44, 50, 54, 4) });
        adapter.UploadFinished(index, r2.Center, r2.Edge, false, chunk);
        var currentUpload = adapter.Capture();
        Assert.True(currentUpload.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var current));
        Assert.Equal(2u, current.Flags & 2u);
        Assert.Equal(8, currentUpload.Ranges[0].PoolId);

        adapter.BeginMutation();
        SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations",
            new ModelDataPoolLocation[] { CreateLocation(7, 30, 34, 40, 44, 3) });
        adapter.UploadFinished(index, r1.Center, r1.Edge, false, chunk);
        var lateOldUpload = adapter.Capture();
        Assert.True(lateOldUpload.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var lateOld));
        Assert.Equal(0u, lateOld.Flags & 2u);
        Assert.Empty(lateOldUpload.Ranges);

        // Restore r2, then change only edge geometry and ensure the center revision remains valid.
        adapter.BeginMutation();
        SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations",
            new ModelDataPoolLocation[] { CreateLocation(8, 40, 44, 50, 54, 4) });
        adapter.UploadFinished(index, r2.Center, r2.Edge, false, chunk);
        var restored = adapter.Capture();
        Assert.True(restored.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out _));
        adapter.Dirty(index, edgeOnly: true);
        var edgeRevision = adapter.ContentRevisions(index);
        adapter.BeginMutation();
        SetField(typeof(ClientChunk), chunk, "edgeModelPoolLocations",
            new ModelDataPoolLocation[] { CreateLocation(10, 60, 64, 70, 74, 5) });
        adapter.UploadFinished(index, edgeRevision.Center, edgeRevision.Edge, false, chunk,
            replacesCenter: false, replacesEdge: true);
        var edgeUpload = adapter.Capture();

        // Assert: old depth consumers see the chunk as unresolved until upload; afterward the new range is current.
        Assert.Equal(0u, stale.Flags & 2u);
        Assert.NotEqual(stale.RevisionLow, current.RevisionLow);
        Assert.NotEqual(dirty.Generation, currentUpload.Generation);
        Assert.True(edgeUpload.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var edgeResult));
        Assert.Equal(2u, edgeResult.Flags & 2u);
        Assert.Equal(2, edgeUpload.Ranges.Length);
        Assert.Equal(8, edgeUpload.Ranges[0].PoolId);
        Assert.Equal(10, edgeUpload.Ranges[1].PoolId);
    }

    /// <summary>Installs the production Harmony hooks and retires a real engine chunk reference.</summary>
    [Fact]
    public void InstalledNativeRetirementWithdrawsPreviouslyCapturedRanges()
    {
        // Arrange: publish a real ClientChunk with ranges, then install the native retirement and dictionary seams.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(1, 0, 0, 0);
        var chunk = CreateChunk([CreateLocation(3, 1, 4, 2, 5, 0)]);
        NativeChunks(map).Add(index, chunk);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        var prior = adapter.Capture();
        var harmony = new Harmony("VGE.Tests.NativeShadowWorldAdapter");
        try
        {
            // Act: Harmony validates current installed dictionary IL while real range removal runs.
            NativeShadowWorldHooks.Install(harmony, map, adapter);
            var renderer = CreateDelayedRemovalRenderer();
            AccessTools.Method(typeof(ClientChunk), "RemoveCenterDataPoolLocations")!
                .Invoke(chunk, [renderer]);
            var current = adapter.Capture();

            // Assert: the previous GPU input can no longer be submitted after native retirement.
            Assert.False(adapter.TryUse(prior, () => throw new InvalidOperationException("Retired range submitted.")));
            Assert.True(current.Complete, current.Failure);
            Assert.Empty(current.Ranges);
            Assert.Single(DelayedRemovals(renderer)[0]);
        }
        finally
        {
            NativeShadowWorldHooks.Detach();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>A native upload without a matching pre-tessellation stamp remains unresolved.</summary>
    [Fact]
    public void InstalledUploadWithoutSourceStampCannotCertifyOldGeometry()
    {
        // Arrange: install production hooks around a native chunk with a valid old range but no captured mesh stamp.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(0, 0, 0, 0);
        var chunk = CreateChunk([CreateLocation(3, 1, 4, 2, 5, 0)]);
        NativeChunks(map).Add(index, chunk);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        var harmony = new Harmony("VGE.Tests.NativeShadowUnknownUpload");
        try
        {
            NativeShadowWorldHooks.Install(harmony, map, adapter);
            adapter.Dirty(index);
            var nativeRenderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
            var tesselated = (TesselatedChunk)RuntimeHelpers.GetUninitializedObject(typeof(TesselatedChunk));

            // Act: the actual engine upload boundary runs without NowProcessChunk having stamped this object.
            nativeRenderer.AddTesselatedChunk(tesselated, chunk);
            var publication = adapter.Capture();
            var position = map.ChunkPosFromChunkIndex3D(index);

            // Assert: absence of a matching pre-read revision cannot restore the stale native range.
            Assert.True(publication.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var entry));
            Assert.Equal(0u, entry.Flags & 2u);
        }
        finally
        {
            NativeShadowWorldHooks.Detach();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>Executed native merge and upload hooks transfer exact revisions for edge and full replacements.</summary>
    [Fact]
    public void InstalledNativeMergePreservesPartRevisionAndReplacementProvenance()
    {
        // Arrange: install checked native seams over one real chunk with empty, non-null terrain part arrays.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(0, 0, 0, 0);
        var chunk = CreateChunk([]);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        NativeChunks(map).Add(index, chunk);
        Assert.True(adapter.Capture().Complete);
        adapter.Dirty(index);
        var revision1 = adapter.ContentRevisions(index);
        var harmony = new Harmony("VGE.Tests.NativeShadowMergeRevision");
        try
        {
            NativeShadowWorldHooks.Install(harmony, map, adapter);

            // Act: execute a complete native upload and retain another pre-edit mesh in the native queue.
            var initial = CreateTesselatedChunk(chunk, includeCenter: true, includeEdge: true);
            StampTessellation(0, 0, 0, initial, skipCenter: false);
            UploadThroughNativeRenderer(initial, chunk);
            var retained = CreateTesselatedChunk(chunk, includeCenter: true, includeEdge: true);
            StampTessellation(0, 0, 0, retained, skipCenter: false);

            // Merge an edge-only rebuild into the queued old mesh, then run its real native upload boundary.
            adapter.Dirty(index, edgeOnly: true);
            var revision2 = adapter.ContentRevisions(index);
            var edgeUpdate = CreateTesselatedChunk(chunk, includeCenter: false, includeEdge: true);
            StampTessellation(0, 0, 0, edgeUpdate, skipCenter: true);
            Assert.True(retained.MergeIfEqual(edgeUpdate));
            UploadThroughNativeRenderer(retained, chunk);
            var edgePublished = adapter.Capture();
            var edgePosition = map.ChunkPosFromChunkIndex3D(index);
            Assert.True(edgePublished.TryFind(new(edgePosition.Dimension, edgePosition.X, edgePosition.Y, edgePosition.Z), out var edgeEntry));
            Assert.Equal(2u, edgeEntry.Flags & 2u);

            // A later complete rebuild replaces both native parts and uploads the matching new revisions.
            var oldQueuedFull = CreateTesselatedChunk(chunk, includeCenter: true, includeEdge: true);
            StampTessellation(0, 0, 0, oldQueuedFull, skipCenter: false);
            adapter.Dirty(index);
            var revision3 = adapter.ContentRevisions(index);
            var fullReplacement = CreateTesselatedChunk(chunk, includeCenter: true, includeEdge: true);
            StampTessellation(0, 0, 0, fullReplacement, skipCenter: false);
            var replacementCenters = NativeShadowTessellationFields.Centers(fullReplacement);
            var replacementEdges = NativeShadowTessellationFields.Edges(fullReplacement);
            Assert.True(oldQueuedFull.MergeIfEqual(fullReplacement));
            Assert.Same(replacementCenters, NativeShadowTessellationFields.Centers(oldQueuedFull));
            Assert.Same(replacementEdges, NativeShadowTessellationFields.Edges(oldQueuedFull));
            UploadThroughNativeRenderer(oldQueuedFull, chunk);
            var fullPublished = adapter.Capture();

            // Assert: native queue transfer retained the old center revision, accepted the edge revision,
            // then accepted both revisions from a complete part replacement.
            Assert.Equal(revision1.Center, revision2.Center);
            Assert.True(revision2.Edge > revision1.Edge);
            Assert.True(revision3.Center > revision2.Center);
            Assert.True(revision3.Edge > revision2.Edge);
            Assert.True(fullPublished.TryFind(new(edgePosition.Dimension, edgePosition.X, edgePosition.Y, edgePosition.Z), out var fullEntry));
            Assert.Equal(2u, fullEntry.Flags & 2u);
        }
        finally
        {
            NativeShadowWorldHooks.Detach();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>Failed native work withdraws the publication until a complete owner reset is observed.</summary>
    [Fact]
    public void FailedNativeMutationCanOnlyRecoverThroughSuccessfulReset()
    {
        // Arrange: bootstrap an empty real engine map and enter its range-mutation boundary.
        var map = CreateMap(4);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        var original = adapter.Capture();
        adapter.BeginMutation();
        adapter.EndMutation(true);

        // Act: inspect failure, then reset and rescan the native dictionary.
        var failed = adapter.Capture();
        adapter.Reset();
        var recovered = adapter.Capture();

        // Assert: reset re-establishes the complete current domain, while prior identities remain withdrawn.
        Assert.False(failed.Complete);
        Assert.True(recovered.Complete, recovered.Failure);
        Assert.False(adapter.TryUse(original, () => throw new InvalidOperationException("Pre-reset publication submitted.")));
    }

    /// <summary>Limit reconfiguration preserves pending and failed upload history until authoritative recovery.</summary>
    [Fact]
    public void LimitReconfigurationDoesNotForgetPendingOrFailedUploads()
    {
        // Arrange: dirty native content and retain a completed pre-edit publication.
        var map = CreateMap(4);
        long index = map.ChunkIndex3D(1, 1, 1, 0);
        var chunk = CreateChunk([CreateLocation(2, 0, 4, 0, 4, 0)]);
        NativeChunks(map).Add(index, chunk);
        using var adapter = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
        var original = adapter.Capture();
        adapter.Dirty(index);

        // Act: reload live limits while the native replacement upload remains outstanding.
        adapter.Reconfigure(8, 16, 8, 8);
        var pending = adapter.Capture();
        var position = map.ChunkPosFromChunkIndex3D(index);

        // Assert: bootstrap may refresh the domain but cannot certify the old mesh after limit changes.
        Assert.False(adapter.TryUse(original, () => throw new InvalidOperationException("Pre-reconfigure publication submitted.")));
        Assert.True(pending.Complete, pending.Failure);
        Assert.True(pending.TryFind(new(position.Dimension, position.X, position.Y, position.Z), out var entry));
        Assert.Equal(0u, entry.Flags & 2u);

        // Arrange a failed upload in the same owner, then reconfigure its bounds again.
        adapter.BeginMutation();
        adapter.EndMutation(true);
        adapter.Reconfigure(8, 16, 8, 8);

        // Act and assert: configuration cannot clear a failure that requires an authoritative reset.
        var failed = adapter.Capture();
        Assert.False(failed.Complete);
        Assert.Contains("failed", failed.Failure, StringComparison.OrdinalIgnoreCase);
    }
    #endregion

    #region Private
    /// <summary>Builds an uninitialized native map with the real index codec and its expected synchronization fields.</summary>
    private static ClientWorldMap CreateMap(int capacity)
    {
        var map = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
        SetField(typeof(ClientWorldMap), map, "chunks", new Dictionary<long, ClientChunk>(capacity));
        SetField(typeof(ClientWorldMap), map, "chunksLock", new object());
        SetField(typeof(WorldMap), map, "index3dMulX", 1024);
        SetField(typeof(WorldMap), map, "index3dMulZ", 1024 * 1024);
        return map;
    }

    /// <summary>Creates actual engine chunk metadata with owned arrays used by terrain upload.</summary>
    private static ClientChunk CreateChunk(ModelDataPoolLocation[] center)
    {
        var chunk = (ClientChunk)RuntimeHelpers.GetUninitializedObject(typeof(ClientChunk));
        SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations", center);
        SetField(typeof(ClientChunk), chunk, "edgeModelPoolLocations", Array.Empty<ModelDataPoolLocation>());
        return chunk;
    }

    /// <summary>Creates an engine tessellation object with selected center and edge part ownership.</summary>
    private static TesselatedChunk CreateTesselatedChunk(ClientChunk chunk, bool includeCenter, bool includeEdge)
    {
        var tesselated = (TesselatedChunk)RuntimeHelpers.GetUninitializedObject(typeof(TesselatedChunk));
        SetField(typeof(TesselatedChunk), tesselated, "positionX", 0);
        SetField(typeof(TesselatedChunk), tesselated, "positionYAndDimension", 0);
        SetField(typeof(TesselatedChunk), tesselated, "positionZ", 0);
        SetField(typeof(TesselatedChunk), tesselated, "chunk", chunk);
        SetField(typeof(TesselatedChunk), tesselated, "centerParts",
            includeCenter ? Array.Empty<TesselatedChunkPart>() : null!);
        SetField(typeof(TesselatedChunk), tesselated, "edgeParts",
            includeEdge ? Array.Empty<TesselatedChunkPart>() : null!);
        return tesselated;
    }

    /// <summary>Invokes the production pre-read stamp callback while leaving the complete game tessellator offline.</summary>
    private static void StampTessellation(int x, int y, int z, TesselatedChunk chunk, bool skipCenter)
    {
        AccessTools.Method(typeof(NativeShadowWorldHooks), "Tessellate")!
            .Invoke(null, [x, y, z, chunk, skipCenter]);
    }

    /// <summary>Executes the installed native upload boundary over zero-length parts without OpenGL resources.</summary>
    private static void UploadThroughNativeRenderer(TesselatedChunk tesselated, ClientChunk chunk)
    {
        var renderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
        var master = (MeshDataPoolMasterManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolMasterManager));
        SetField(typeof(ChunkRenderer), renderer, "masterPool", master);
        renderer.AddTesselatedChunk(tesselated, chunk);
    }

    /// <summary>Builds an engine range with half-open vertex and index endpoints.</summary>
    private static ModelDataPoolLocation CreateLocation(int pool, int indexStart, int indexEnd,
        int vertexStart, int vertexEnd, int detail)
    {
        var location = new ModelDataPoolLocation();
        SetField(typeof(ModelDataPoolLocation), location, "PoolId", pool);
        SetField(typeof(ModelDataPoolLocation), location, "IndicesStart", indexStart);
        SetField(typeof(ModelDataPoolLocation), location, "IndicesEnd", indexEnd);
        SetField(typeof(ModelDataPoolLocation), location, "VerticesStart", vertexStart);
        SetField(typeof(ModelDataPoolLocation), location, "VerticesEnd", vertexEnd);
        SetField(typeof(ModelDataPoolLocation), location, "LodLevel", detail);
        return location;
    }

    /// <summary>Creates native delayed-removal state so engine retirement performs its actual queue action.</summary>
    private static ChunkRenderer CreateDelayedRemovalRenderer()
    {
        var renderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
        var master = (MeshDataPoolMasterManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolMasterManager));
        SetField(typeof(MeshDataPoolMasterManager), master, "DelayedPoolLocationRemoval", true);
        SetField(typeof(MeshDataPoolMasterManager), master, "removalQueue",
            new[] { new Queue<ModelDataPoolLocation[]>() });
        SetField(typeof(ChunkRenderer), renderer, "masterPool", master);
        return renderer;
    }

    /// <summary>Reads the native delayed-removal queue after its public retirement method runs.</summary>
    private static Queue<ModelDataPoolLocation[]>[] DelayedRemovals(ChunkRenderer renderer)
    {
        var master = AccessTools.Field(typeof(ChunkRenderer), "masterPool").GetValue(renderer)!;
        return (Queue<ModelDataPoolLocation[]>[])AccessTools.Field(typeof(MeshDataPoolMasterManager), "removalQueue").GetValue(master)!;
    }

    /// <summary>Reads the engine's canonical coordinate dictionary from its native owner.</summary>
    private static Dictionary<long, ClientChunk> NativeChunks(ClientWorldMap map) =>
        (Dictionary<long, ClientChunk>)AccessTools.Field(typeof(ClientWorldMap), "chunks").GetValue(map)!;

    /// <summary>Sets inherited or private native test-fixture fields without invoking engine startup.</summary>
    private static void SetField(Type owner, object target, string name, object value) =>
        (owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(owner.FullName, name)).SetValue(target, value);
    #endregion
}

