using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns a bounded GPU-only geometry/view selection and initialized indirect intervals for one immutable batch.</summary>
internal sealed class ShadowGeometrySelection : IDisposable
{
    private readonly ShadowSelectionShader shader;
    private readonly GpuShaderStorageBuffer inputs, ranges;
    private readonly long bytes;
    private readonly bool[] requiredCommands;
    private readonly ShadowPagePool pages;
    private readonly long pageBatch;
    private readonly ImmutableArray<ShadowDrawView> views;
    private readonly ImmutableArray<ShadowViewCandidates> associations;
    private readonly ShadowCandidatePool candidates;
    private readonly ShadowWorldPublication world;
    private readonly ImmutableArray<ShadowObjectDrawRange> objects;
    private readonly ImmutableArray<ShadowDrawRange> drawRanges;
    private GpuFence? completion;
    private bool sealedBatch, disposed;

    internal GpuShaderStorageBuffer Output { get; }
    internal int CommandsPerView { get; }
    internal int ViewCount { get; }
    /// <summary>Bounds static terrain deformation beyond its fixed voxel partition in world units.</summary>
    internal double TerrainPadding { get; }
    internal long AllocatedBytes => disposed ? 0 : bytes;
    /// <summary>Supplies GPU success and the exact page submission epoch for replacement admission.</summary>
    internal RHIBufferRange Certificate => new(new GpuBufferRef(Output), 48, 12);
    internal RHIBufferRange Selectors => new(new GpuBufferRef(Output),
        checked(64 + CommandsPerView * ViewCount * 20), checked(CommandsPerView * ViewCount * 8));

    #region Public API
    /// <summary>Validates exact current dependencies and aggregate storage before emitting any GPU selection work.</summary>
    internal ShadowGeometrySelection(ICoreAPI api, IReadOnlyList<ShadowDrawView> views,
        IReadOnlyList<ShadowViewCandidates> associations, ShadowCandidatePool candidates,
        ShadowWorldPublication world, ShadowWorldGpuBuffers uploaded,
        ImmutableArray<ShadowDrawRange> drawRanges, int commandsPerView, ShadowPagePool pages,
        ShadowDrawSettings settings, long otherDrawingBytes, ImmutableArray<ShadowObjectDrawRange> objects = default,
        double terrainPadding = 0)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(associations);
        var policy = settings.Freeze();
        if (objects.IsDefault) objects = [];
        if (!world.Complete || !uploaded.Matches(world) || !pages.CanSubmit ||
            drawRanges.IsDefault || drawRanges.Length != world.Ranges.Length || drawRanges.Length > policy.RangeVisits ||
            views.Count < 1 || views.Count > policy.ViewCapacity || associations.Count < views.Count ||
            associations.Count > policy.ExpansionVisits || objects.Length > policy.RangeVisits - drawRanges.Length ||
            commandsPerView < 0 || otherDrawingBytes < 0 || !double.IsFinite(terrainPadding) ||
            terrainPadding < 0 || terrainPadding >= float.MaxValue)
            throw new InvalidOperationException("Geometry selection requires complete bounded current inputs.");
        long slots = checked((long)commandsPerView * views.Count);
        float packedPadding = terrainPadding == 0 ? 0 : MathF.BitIncrement((float)terrainPadding);
        if (!float.IsFinite(packedPadding))
            throw new InvalidOperationException("Terrain deformation cannot be rounded outward to a finite GPU envelope.");
        int[] independent = Enumerable.Range(0, drawRanges.Length).Where(i => drawRanges[i].Eligible == 2).ToArray();
        long inputBytes = checked((32L + views.Count * 64L + associations.Count * 12L + objects.Length * 8L + independent.Length) * 4);
        long rangeBytes = Math.Max(44L, drawRanges.Length * 44L);
        long outputBytes = checked(64L + slots * 28L);
        // Include the CPU packing array, immutable input copies, independent-range
        // references and the largest submission coverage check in the same allowance.
        long metadataBytes = checked(inputBytes + independent.Length * 4L + commandsPerView + slots +
            views.Count * (long)IntPtr.Size + associations.Count * (long)Unsafe.SizeOf<ShadowViewCandidates>() + 1024);
        bytes = checked(inputBytes + rangeBytes + outputBytes + metadataBytes);
        var caps = GpuSupport.Graphics;
        if (slots > policy.CommandSlots || bytes > policy.MemoryMiB * 1048576L ||
            otherDrawingBytes > policy.MemoryMiB * 1048576L - bytes ||
            Math.Max(inputBytes, Math.Max(rangeBytes, outputBytes)) > caps.MaxShaderStorageBlockSize ||
            bytes > int.MaxValue || caps.MaxComputeShaderStorageBlocks < 6 || caps.MaxShaderStorageBufferBindings < 6)
            throw new InvalidOperationException("Selection intervals exceed aggregate drawing memory or baseline storage limits.");
        CommandsPerView = commandsPerView; ViewCount = views.Count; TerrainPadding = terrainPadding;
        this.pages = pages; pageBatch = pages.Batch;
        this.views = ImmutableArray.CreateRange(views); this.associations = ImmutableArray.CreateRange(associations);
        this.candidates = candidates; this.world = world;
        this.objects = objects;
        this.drawRanges = drawRanges;
        var words = new uint[checked((int)(inputBytes / 4))];
        words[0] = (uint)views.Count; words[1] = (uint)associations.Count;
        words[2] = (uint)commandsPerView; words[3] = (uint)drawRanges.Length;
        words[4] = (uint)(world.Table.Length - 1); words[5] = (uint)world.ProbeLimit;
        words[6] = (uint)candidates.EntryCapacity; words[7] = (uint)policy.ExpansionVisits;
        words[8] = (uint)policy.PairCapacity;
        words[9] = (uint)policy.EstimatedTriangles; words[10] = (uint)policy.EstimatedRasterSamples;
        words[11] = (uint)slots;
        ShadowPageRequest.Put(words, 12, pages.Batch);
        words[14] = (uint)objects.Length;
        words[15] = checked((uint)(32 + views.Count * 64 + associations.Count * 12));
        words[16] = (uint)policy.ViewInstances;
        words[17] = (uint)independent.Length;
        words[18] = checked(words[15] + (uint)objects.Length * 8);
        for (int i = 0; i < independent.Length; i++) words[words[18] + i] = (uint)independent[i];
        words[19] = BitConverter.SingleToUInt32Bits(packedPadding);
        long rasterPixels = 0;
        for (int view = 0; view < views.Count; view++)
        {
            var region = views[view].Region;
            rasterPixels = checked(rasterPixels + region.Width * (long)region.Height);
            if (rasterPixels > policy.RasterPixels || views[view].Projection.PageSize != pages.PageSize)
                throw new InvalidOperationException("Selection raster work exceeds the admitted region allowance.");
            views[view].Pack(words.AsSpan(32 + view * 64, 64), pages.Capacity, pages.ProbeLimit, pages.ArrayLayers, pages.Batch);
        }
        PackAssociations(words, views, associations, candidates, world, terrainPadding);
        requiredCommands = ValidateRanges(drawRanges, objects, commandsPerView);
        for (int i = 0; i < objects.Length; i++)
        {
            int at = checked((int)words[15] + i * 8);
            var item = objects[i];
            words[at] = item.Command; words[at + 1] = item.IndexStart; words[at + 2] = item.IndexCount;
            words[at + 3] = unchecked((uint)item.BaseVertex); words[at + 4] = item.Instances;
            words[at + 5] = item.TriangleCost; words[at + 6] = item.Object;
            words[at + 7] = unchecked((uint)item.Dimension);
        }
        shader = ShadowSelectionShader.Create(api);
        try
        {
            inputs = Buffer((int)inputBytes, "Inputs"); ranges = Buffer((int)rangeBytes, "Ranges");
            Output = Buffer((int)outputBytes, "Output");
            inputs.UploadSubData<uint>(words, 0);
            if (!drawRanges.IsEmpty) ranges.UploadSubData(drawRanges.AsSpan(), 0);
            var bindings = (IShadowSelectionBindings)shader;
            bindings.Inputs = inputs; bindings.World = uploaded.Table;
            bindings.Headers = candidates.Headers; bindings.Coordinates = candidates.Coordinates;
            bindings.Ranges = ranges; bindings.Output = Output;
            var rhi = RHICommandContext.Current;
            using var boundary = rhi.EnterBoundary(new("Shadows.Selection", true, compute: [shader.Pipeline]));
            boundary.Run(() =>
            {
                shader.Dispatch(1);
                rhi.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.Indirect | ResourceAccess.Vertex));
            });
        }
        catch { Output?.Dispose(); ranges?.Dispose(); inputs?.Dispose(); shader.Dispose(); throw; }
    }

    /// <summary>Returns the CPU-known compatible interval without observing GPU-selected geometry.</summary>
    internal RHIIndirectDraw Commands(int view, ShadowDrawInterval interval)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch || (uint)view >= (uint)ViewCount || interval.FirstCommand < 0 || interval.Count <= 0 ||
            interval.FirstCommand > CommandsPerView - interval.Count)
            throw new ArgumentOutOfRangeException(nameof(interval));
        int first = checked(view * CommandsPerView + interval.FirstCommand);
        return new(new(new GpuBufferRef(Output), checked(64 + first * 20), checked(interval.Count * 20)), interval.Count, 20);
    }

    /// <summary>Supplies a single object/view selector without rebasing the native object's own instance streams.</summary>
    internal RHIBufferRange ObjectSelector(int view, int command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch || (uint)view >= (uint)ViewCount || (uint)command >= (uint)CommandsPerView)
            throw new ArgumentOutOfRangeException(nameof(command));
        var selectors = Selectors;
        return new(selectors.Buffer, checked(selectors.Offset + (view * CommandsPerView + command) * 8), 8);
    }

    /// <summary>Associates captured drawing with the exact selection projection and page interval.</summary>
    internal bool MatchesView(int index, ShadowDrawView view, ShadowPagePool pool) => !disposed && !sealedBatch &&
        ReferenceEquals(pool, pages) && pool.CanSubmit && pool.Batch == pageBatch &&
        (uint)index < (uint)views.Length && ReferenceEquals(views[index], view);

    /// <summary>Returns the original CPU-supplied object interval, never a GPU-selected draw result.</summary>
    internal ShadowObjectDrawRange ObjectRange(int index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch || (uint)index >= (uint)objects.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return objects[index];
    }

    /// <summary>Associates native submission with the exact immutable CPU inputs used to construct its GPU commands.</summary>
    internal bool MatchesInputs(ImmutableArray<ShadowDrawRange> terrain, ImmutableArray<ShadowObjectDrawRange> nonTerrain,
        ShadowWorldPublication publication) => !disposed && !sealedBatch && drawRanges.Equals(terrain) &&
        objects.Equals(nonTerrain) && ReferenceEquals(world, publication);

    /// <summary>Rejects omitted or duplicated native submission intervals before any replacement tile is cleared.</summary>
    internal void ValidateSubmission(IReadOnlyList<ShadowPreparedDraw> draws, ShadowPagePool pool)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch || !ReferenceEquals(pool, pages) || !pool.CanSubmit || pool.Batch != pageBatch)
            throw new InvalidOperationException("Geometry selection no longer belongs to the current page interval.");
        if (draws.Count > checked(ViewCount * CommandsPerView))
            throw new InvalidOperationException("Native drawing group count exceeds its bounded command intervals.");
        foreach (var association in associations)
            if (!candidates.CanConsumeEnvelope(association.Handle, association.Current, world, TerrainPadding / 32))
                throw new InvalidOperationException("Candidate input changed after geometry selection.");
        var covered = new bool[checked(ViewCount * CommandsPerView)];
        foreach (var draw in draws)
        {
            var interval = draw.Interval;
            if ((uint)draw.ViewIndex >= (uint)ViewCount || interval.Count <= 0 || interval.FirstCommand < 0 ||
                interval.FirstCommand > CommandsPerView - interval.Count || !ReferenceEquals(draw.Selection, this) || !draw.IsCurrent)
                throw new InvalidOperationException("Native submission interval is incomplete or obsolete.");
            for (int i = 0; i < interval.Count; i++)
            {
                int slot = draw.ViewIndex * CommandsPerView + interval.FirstCommand + i;
                if (covered[slot]) throw new InvalidOperationException("Native submission repeats an admitted command interval.");
                covered[slot] = true;
            }
        }
        for (int view = 0; view < ViewCount; view++)
            for (int command = 0; command < CommandsPerView; command++)
                if (requiredCommands[command] && !covered[view * CommandsPerView + command])
                    throw new InvalidOperationException("Native submission omits a required geometry/view interval.");
    }

    /// <summary>Requires complete raster coverage and a candidate enclosure for every receiving region being certified.</summary>
    internal void ValidateReplacement(int viewIndex, ShadowPageRequest request)
    {
        if ((uint)viewIndex >= (uint)views.Length || request.Operation != ShadowPageOperation.Allocate ||
            request.Projection is null || !views[viewIndex].Projection.Matches(request.Projection) ||
            request.WorldRevision != world.Generation)
            throw new InvalidOperationException("Replacement identity differs from its selected virtual view.");
        var view = views[viewIndex];
        long x = (long)request.Key.X * pages.PageSize, y = (long)request.Key.Y * pages.PageSize;
        if (x < view.Uncached.X || y < view.Uncached.Y ||
            x + pages.PageSize > view.Uncached.X + view.Uncached.Width ||
            y + pages.PageSize > view.Uncached.Y + view.Uncached.Height)
            throw new InvalidOperationException("A replacement page is not entirely covered by its uncached raster rectangle.");
        // Candidate coordinates are dimension-local chunk units; rendered positions
        // retain the native internal-Y dimension offset until final projection.
        double offset = request.Key.Source.Dimension == 1 ? 0 : request.Key.Source.Dimension * 32768.0;
        var r = request.Region;
        var receiver = new ShadowBounds((r.MinX - TerrainPadding) / 32, (r.MinY - offset - TerrainPadding) / 32, (r.MinZ - TerrainPadding) / 32,
            (r.MaxX + TerrainPadding) / 32, (r.MaxY - offset + TerrainPadding) / 32, (r.MaxZ + TerrainPadding) / 32);
        foreach (var association in associations)
            if (association.View == viewIndex && association.Current.Receiver.Contains(receiver)) return;
        // Multiple disjoint receiving boxes cannot certify their enclosure merely
        // by sharing a page. Request an association covering that enclosure first.
        throw new InvalidOperationException("Replacement receiving coverage lacks a complete candidate enclosure.");
    }

    /// <summary>Ends submission while retaining all charged bytes until the nonblocking retirement fence completes.</summary>
    internal void Seal()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch) throw new InvalidOperationException("Selection was already sealed.");
        completion = RHICommandContext.Current.InsertFence(); sealedBatch = true;
    }

    /// <summary>Releases a sealed batch only when prior commands have completed.</summary>
    internal bool Poll()
    {
        if (disposed) return true;
        if (!sealedBatch || completion is null || !completion.TryConsumeIfSignaled()) return false;
        completion = null; Dispose(); return true;
    }

    /// <summary>Retires only owned metadata and commands through the RHI; native geometry remains borrowed.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; completion?.Dispose(); completion = null;
        Output.Dispose(); ranges.Dispose(); inputs.Dispose(); shader.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Requires every view to have current conservative associations with matching light and projection coordinates.</summary>
    private static void PackAssociations(uint[] words, IReadOnlyList<ShadowDrawView> views,
        IReadOnlyList<ShadowViewCandidates> associations, ShadowCandidatePool candidates, ShadowWorldPublication world, double terrainPadding)
    {
        var covered = new bool[views.Count];
        for (int i = 0; i < associations.Count; i++)
        {
            var association = associations[i];
            if ((uint)association.View >= (uint)views.Count ||
                !candidates.CanConsumeEnvelope(association.Handle, association.Current, world, terrainPadding / 32))
                throw new InvalidOperationException("View candidate input is unavailable or no longer current.");
            var projection = views[association.View].Projection;
            var position = projection.IsPoint ?
                (projection.PositionOrForward - new Numerics.Vector3d(0,
                    projection.Source.Dimension == 1 ? 0 : projection.Source.Dimension * 32768.0, 0)) / 32.0 :
                -projection.PositionOrForward;
            if (association.Current.Source != projection.Source || association.Current.ProjectionGeneration != projection.Generation ||
                association.Current.PositionOrDirection != position)
                throw new InvalidOperationException("Candidate and raster projection snapshots disagree.");
            covered[association.View] = true;
            int a = 32 + views.Count * 64 + i * 12;
            words[a] = (uint)association.View; words[a + 1] = (uint)association.Handle.Slot;
            ShadowPageRequest.Put(words, a + 2, association.Handle.Generation);
            ShadowPageRequest.Put(words, a + 4, candidates.BatchGeneration);
            ShadowPageRequest.Put(words, a + 6, world.Generation);
            ShadowPageRequest.Put(words, a + 8, association.Current.ChangeSequence);
        }
        foreach (bool present in covered)
            if (!present) throw new InvalidOperationException("A virtual view has no complete candidate input.");
    }

    /// <summary>Rejects overlapping eligible command ownership before GPU deduplication can mistake it for a duplicate.</summary>
    private static bool[] ValidateRanges(ImmutableArray<ShadowDrawRange> ranges,
        ImmutableArray<ShadowObjectDrawRange> objects, int commands)
    {
        var assigned = new bool[commands];
        foreach (var range in ranges)
        {
            if (range.Eligible == 0) continue;
            if (range.Eligible > 2 || range.Command >= commands || assigned[range.Command] || range.IndexCount % 3 != 0 ||
                range.TriangleCost < range.IndexCount / 3)
                throw new InvalidOperationException("Native ranges do not own distinct valid command intervals.");
            assigned[range.Command] = true;
        }
        foreach (var item in objects)
        {
            if (item.Command >= commands || assigned[item.Command] || item.IndexCount == 0 ||
                item.IndexCount % 3 != 0 || item.Instances == 0 || item.TriangleCost < (ulong)item.IndexCount / 3 * item.Instances)
                throw new InvalidOperationException("Non-terrain draws do not own complete bounded command intervals.");
            assigned[item.Command] = true;
        }
        return assigned;
    }

    /// <summary>Allocates charged mutable storage through the existing resource owner.</summary>
    private static GpuShaderStorageBuffer Buffer(int bytes, string name) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable,
            Capacity = bytes, Usage = BufferUsage.DynamicDraw, Label = "Shadows.Selection." + name }).RequireResource();
    #endregion
}
