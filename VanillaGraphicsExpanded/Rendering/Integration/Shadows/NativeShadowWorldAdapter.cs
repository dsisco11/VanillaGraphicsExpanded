using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Linq;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Publishes native chunk ownership through one bounded adapter; never classifies mesh pools.</summary>
internal sealed class NativeShadowWorldAdapter : IDisposable
{
    private static long nextOwner;
    private readonly long owner = Interlocked.Increment(ref nextOwner);
    private static readonly AccessTools.FieldRef<ClientWorldMap, Dictionary<long, ClientChunk>> ReadChunks =
        AccessTools.FieldRefAccess<ClientWorldMap, Dictionary<long, ClientChunk>>("chunks");
    private static readonly AccessTools.FieldRef<ClientWorldMap, object> ReadLock =
        AccessTools.FieldRefAccess<ClientWorldMap, object>("chunksLock");
    private readonly ClientWorldMap map;
    private readonly object gate = new();
    private int capacity, rangeCapacity, changeCapacity, probeLimit;
    private readonly Dictionary<long, ClientChunk> chunks = new();
    private readonly HashSet<long> changed = new();
    private readonly Dictionary<long, NativeShadowContentState> content = new();
    private readonly ShadowSourceLifetimes lifetimes = new();
    private long generation = 1;
    private int mutations;
    private bool bootstrap = true, disposed, failedMutation;
    private ShadowWorldPublication? publication;

    #region Public API
    #region Policy and construction
    /// <summary>Bounds native upload metadata inspection before accepting a replacement.</summary>
    internal int RangeCapacity => rangeCapacity;
    /// <summary>Bounds queued native work stamps as well as coordinate notifications.</summary>
    internal int ChangeCapacity => changeCapacity;
    /// <summary>Sets finite input, range and change allowances supplied by the owning policy.</summary>
    internal NativeShadowWorldAdapter(ClientWorldMap map, int capacity, int rangeCapacity, int changeCapacity, int probeLimit)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (capacity <= 0 || capacity > 1 << 24 || rangeCapacity <= 0 || changeCapacity <= 0 || probeLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.map = map; this.capacity = capacity; this.rangeCapacity = rangeCapacity;
        this.changeCapacity = changeCapacity; this.probeLimit = probeLimit;
        if (owner <= 0) throw new InvalidOperationException("Native publication identity space exhausted.");
    }

    #endregion
    #region Native content changes
    /// <summary>Withdraws all prior certificates before a native owner can mutate or retire a range.</summary>
    internal void BeginMutation()
    {
        lock (gate) { mutations = checked(mutations + 1); generation = checked(generation + 1); publication = null; }
    }

    /// <summary>Ends native work; an exception requires a fresh owner bootstrap before certification.</summary>
    internal void EndMutation(bool failed)
    {
        lock (gate)
        {
            if (mutations <= 0) throw new InvalidOperationException("Unbalanced native publication mutation.");
            mutations--;
            if (failed) { bootstrap = true; failedMutation = true; }
        }
    }

    /// <summary>Records source coordinates under native dictionary ownership; overflow forces resynchronization.</summary>
    internal void Changed(long index)
    {
        lock (gate)
        {
            generation = checked(generation + 1); publication = null;
            if (changed.Count >= changeCapacity && !changed.Contains(index)) { bootstrap = true; changed.Clear(); }
            else changed.Add(index);
        }
    }

    /// <summary>Keeps pre-edit meshes unresolved until the owning upload completes for that revision.</summary>
    internal void Dirty(long index, bool edgeOnly = false)
    {
        lock (gate)
        {
            Changed(index);
            var state = Content(index);
            if (state == null) return;
            if (!edgeOnly) state.RequiredCenter = generation;
            state.RequiredEdge = generation;
        }
    }

    /// <summary>Captures content before tessellation reads it, rather than relabeling old work at upload time.</summary>
    internal long ContentRevision(long index)
    {
        lock (gate) return content.TryGetValue(index, out var state) ? Math.Max(state.RequiredCenter, state.RequiredEdge) : 0;
    }

    /// <summary>Freezes each part's required revision before native tessellation reads its voxels.</summary>
    internal (long Center, long Edge) ContentRevisions(long index)
    {
        lock (gate) return content.TryGetValue(index, out var state) ? (state.RequiredCenter, state.RequiredEdge) : (0, 0);
    }

    /// <summary>Clears pending content only when no later edit superseded the captured upload.</summary>
    internal void UploadFinished(long index, long centerRevision, long edgeRevision, bool failed, ClientChunk? expected = null,
        bool replacesCenter = true, bool replacesEdge = true)
    {
        lock (ReadLock(map)) lock (gate)
        {
            // An old world's/old chunk's queued mesh cannot clear a replacement chunk's pending edit.
            if (expected != null && (!ReadChunks(map).TryGetValue(index, out var current) || !ReferenceEquals(current, expected)))
            { EndMutation(failed); return; }
            var state = Content(index);
            if (state != null)
            {
                // Native upload overwrites geometry even when its source revision is obsolete.
                // Retain that fact instead of deleting a pending marker after an earlier success.
                if (replacesCenter) state.UploadedCenter = failed ? -1 : centerRevision;
                if (replacesEdge) state.UploadedEdge = failed ? -1 : edgeRevision;
            }
            EndMutation(failed);
        }
    }

    #endregion
    #region Publication lifetime
    /// <summary>Applies finite input limits without forgetting failed uploads or outstanding content changes.</summary>
    internal void Reconfigure(int chunkLimit, int rangeLimit, int changeLimit, int probes)
    {
        if (chunkLimit <= 0 || chunkLimit > 1 << 24 || rangeLimit <= 0 || changeLimit <= 0 || probes <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkLimit));
        lock (gate)
        {
            capacity = chunkLimit; rangeCapacity = rangeLimit; changeCapacity = changeLimit; probeLimit = probes;
            generation = checked(generation + 1); publication = null; bootstrap = true;
            chunks.Clear(); changed.Clear();
            // Shrinking below retained pending history cannot drop its invalidation proof.
            // Existing entries drain; additional unseen entries fail closed at the new limit.
        }
    }

    /// <summary>Forces rebootstrap after dictionary replacement, shader/world reset or lost lifecycle history.</summary>
    internal void Reset()
    {
        lock (gate)
        {
            generation = checked(generation + 1); publication = null;
            bootstrap = true; chunks.Clear(); changed.Clear(); content.Clear(); failedMutation = false;
        }
    }

    /// <summary>Freezes the whole loaded envelope and bounded inputs only after pending native changes drain.</summary>
    internal ShadowWorldPublication Capture()
    {
        // Native dictionary lock always precedes the adapter lock. Mutation notifications never
        // hold the adapter lock while entering native code, avoiding a worker/render lock inversion.
        lock (ReadLock(map)) lock (gate)
        {
            if (disposed || mutations != 0) return Incomplete("Native changes pending or adapter disposed.");
            if (publication != null) return publication;
            var source = ReadChunks(map);
            if (source.Count > capacity) return Incomplete("Loaded chunk capacity exceeded; domain is not truncated.");
            if (bootstrap)
            {
                chunks.Clear();
                foreach (var pair in source) chunks.Add(pair.Key, pair.Value);
                // History belonging to unloaded owners no longer needs a retained pending entry.
                foreach (long index in content.Keys.ToArray()) if (!source.ContainsKey(index)) content.Remove(index);
                bootstrap = false; changed.Clear();
            }
            else
            {
                foreach (long index in changed)
                {
                    if (source.TryGetValue(index, out var chunk)) chunks[index] = chunk;
                    else { chunks.Remove(index); content.Remove(index); }
                }
                changed.Clear();
            }
            if (failedMutation) return Incomplete("Native upload/mutation failed; requires a successful reset boundary.");
            int tableSize = 2;
            while (tableSize < capacity * 2) tableSize = checked(tableSize * 2);
            var table = new ShadowChunkGpu[tableSize];
            var ranges = ImmutableArray.CreateBuilder<ShadowGeometryRange>();
            var envelopes = ImmutableDictionary.CreateBuilder<int, ShadowBounds>();
            foreach (var pair in chunks)
            {
                var coordinate = map.ChunkPosFromChunkIndex3D(pair.Key);
                var key = new ShadowChunkIdentity(coordinate.Dimension, coordinate.X, coordinate.Y, coordinate.Z);
                var box = new ShadowBounds((double)key.X * 32, (double)key.Y * 32, (double)key.Z * 32,
                    ((double)key.X + 1) * 32, ((double)key.Y + 1) * 32, ((double)key.Z + 1) * 32);
                if (envelopes.TryGetValue(key.Dimension, out var old)) box = new(
                    Math.Min(old.MinX, box.MinX), Math.Min(old.MinY, box.MinY), Math.Min(old.MinZ, box.MinZ),
                    Math.Max(old.MaxX, box.MaxX), Math.Max(old.MaxY, box.MaxY), Math.Max(old.MaxZ, box.MaxZ));
                envelopes[key.Dimension] = box;
                ClientChunk chunk = pair.Value;
                long lifetime = lifetimes.Get(chunk);
                uint offset = (uint)ranges.Count;
                var centers = NativeShadowChunkFields.Centers(chunk);
                var edges = NativeShadowChunkFields.Edges(chunk);
                bool ready = (!content.TryGetValue(pair.Key, out var state) || state.Ready) && !chunk.Disposed
                    && !NativeShadowChunkFields.QueuedUpload(chunk) && !NativeShadowChunkFields.QueuedRedraw(chunk)
                    && centers != null && edges != null;
                if (ready && (!Append(centers!, ranges) || !Append(edges!, ranges)))
                    return Incomplete("Native range capacity or range validation failed.");
                var entry = new ShadowChunkGpu {
                    X = key.X, Y = key.Y, Z = key.Z, Dimension = key.Dimension,
                    NativeLow = (uint)pair.Key, NativeHigh = (uint)((ulong)pair.Key >> 32),
                    LifetimeLow = (uint)lifetime, LifetimeHigh = (uint)((ulong)lifetime >> 32),
                    RevisionLow = (uint)generation, RevisionHigh = (uint)((ulong)generation >> 32),
                    Flags = 1u | (ready ? 2u : 0) | (chunk.Empty ? 4u : 0),
                    // Native connectivity is observable but asynchronous. Without a synchronized
                    // opacity/freshness proof it is advisory only; freshness bits remain clear.
                    Connectivity = chunk.traversability, RangeOffset = offset, RangeCount = (uint)ranges.Count - offset,
                    Reserved0 = chunk.traversabilityFresh ? 1u : 0u };
                bool inserted = false;
                for (uint probe = 0; probe < Math.Min(probeLimit, tableSize); probe++)
                {
                    int slot = (int)((key.Hash() + probe) & (uint)(tableSize - 1));
                    if (table[slot].Flags != 0) continue;
                    table[slot] = entry; inserted = true; break;
                }
                if (!inserted) return Incomplete("Coordinate lookup probe allowance exhausted.");
            }
            return publication = new(generation, true, null, ImmutableArray.Create(table), ranges.ToImmutable(),
                envelopes.ToImmutable(), Math.Min(probeLimit, tableSize)) { Owner = owner };
        }
    }

    /// <summary>Revalidates and submits a bounded consumer interval before native retirement may proceed.</summary>
    internal bool TryUse(ShadowWorldPublication captured, Action submit)
    {
        ArgumentNullException.ThrowIfNull(submit);
        lock (gate)
        {
            if (disposed || mutations != 0 || !captured.Complete || !ReferenceEquals(captured, publication)
                || captured.Generation != generation) return false;
            submit();
            return captured.Generation == generation && mutations == 0 && !disposed;
        }
    }

    /// <summary>Freezes current hide/detail eligibility from chunk-owned ranges without consulting pool allocations.</summary>
    internal bool TryCaptureDrawEligibility(ShadowWorldPublication captured, NativeShadowTerrainDetail detail,
        int visitAllowance, out uint[] eligibility)
    {
        lock (gate)
        {
            eligibility = [];
            if (disposed || mutations != 0 || !captured.Complete || !ReferenceEquals(captured, publication)
                || captured.Generation != generation || visitAllowance < captured.Table.Length
                || captured.Ranges.Length > visitAllowance - captured.Table.Length) return false;
            var result = new uint[captured.Ranges.Length];
            foreach (var entry in captured.Table)
            {
                if ((entry.Flags & 2) == 0) continue;
                long index = unchecked((long)((ulong)entry.NativeLow | (ulong)entry.NativeHigh << 32));
                if (!chunks.TryGetValue(index, out var chunk) || chunk.Disposed) return false;
                var centers = NativeShadowChunkFields.Centers(chunk);
                var edges = NativeShadowChunkFields.Edges(chunk);
                if (centers is null || edges is null || centers.Length + (long)edges.Length != entry.RangeCount) return false;
                int offset = checked((int)entry.RangeOffset);
                // Array identity/ranges are rechecked under the same native mutation gate
                // as submission. Eligibility is frame data and never a membership registry.
                foreach (var locations in new[] { centers, edges })
                    foreach (var location in locations)
                    {
                        var expected = captured.Ranges[offset];
                        if (location is null || location.PoolId != expected.PoolId || location.IndicesStart != expected.IndexStart
                            || location.IndicesEnd - location.IndicesStart != expected.IndexCount
                            || location.VerticesStart != expected.VertexStart || location.VerticesEnd - location.VerticesStart != expected.VertexCount
                            || location.LodLevel != expected.Detail) return false;
                        result[offset++] = detail.Eligible(location) ? 1u : 0u;
                    }
            }
            eligibility = result;
            return true;
        }
    }

    /// <summary>Releases references and withdraws certificates without disposing native chunks or meshes.</summary>
    public void Dispose()
    {
        lock (gate) { disposed = true; publication = null; chunks.Clear(); changed.Clear(); content.Clear(); generation = checked(generation + 1); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Bounds retained content history; loss of a required revision withdraws the whole publication.</summary>
    private NativeShadowContentState? Content(long index)
    {
        if (content.TryGetValue(index, out var state)) return state;
        if (content.Count >= capacity) { failedMutation = true; return null; }
        content.Add(index, state = new());
        return state;
    }
    /// <summary>Copies only bounded range metadata, never vertex/index storage or pool classification.</summary>
    private bool Append(ModelDataPoolLocation[] locations, ImmutableArray<ShadowGeometryRange>.Builder output)
    {
        if (locations.Length > rangeCapacity - output.Count) return false;
        foreach (var location in locations)
        {
            if (location == null || location.PoolId < 0 || location.IndicesStart < 0 || location.IndicesEnd < location.IndicesStart
                || location.VerticesStart < 0 || location.VerticesEnd < location.VerticesStart) return false;
            output.Add(new(location.PoolId, location.IndicesStart, location.IndicesEnd - location.IndicesStart,
                location.VerticesStart, location.VerticesEnd - location.VerticesStart, location.LodLevel,
                (uint)generation, (uint)((ulong)generation >> 32)));
        }
        return true;
    }

    /// <summary>Publishes no partial domain or ranges when bounded work cannot finish.</summary>
    private ShadowWorldPublication Incomplete(string reason) => new(generation, false, reason,
        ImmutableArray<ShadowChunkGpu>.Empty, ImmutableArray<ShadowGeometryRange>.Empty,
        ImmutableDictionary<int, ShadowBounds>.Empty, probeLimit) { Owner = owner };
    #endregion
}
