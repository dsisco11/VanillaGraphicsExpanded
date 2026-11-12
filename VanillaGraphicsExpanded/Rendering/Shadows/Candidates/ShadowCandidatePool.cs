using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Owns bounded segmented associations, GPU-only selection and nonblocking slot retirement.</summary>
internal sealed class ShadowCandidatePool : IDisposable
{
    internal const int SegmentSize = 64;
    private static long nextOwner;
    private readonly long owner = Interlocked.Increment(ref nextOwner);
    private readonly ShadowCandidateSettings policy;
    private readonly ShadowCandidateShader shader;
    private readonly GpuShaderStorageBuffer inputs, dedup, changesBuffer;
    private readonly ShadowCandidateRequest?[] requests;
    private readonly long[] generations;
    private readonly GpuFence?[] retirements;
    private readonly bool[] submitted;
    private readonly RHIShaderRef executable;
    private readonly uint[] packed = new uint[256];
    private ShadowWorldPublication? publication;
    private ShadowWorldGpuBuffers? world;
    private ShadowCandidateChanges? changes;
    private long nextGeneration, batchGeneration;
    private int slabsLeft, cellsLeft, probesLeft, changesLeft;
    private bool batchValid, disposed;
    private ShadowFrameBudget? frameBudget;

    /// <summary>Persistent 128-byte headers; consumers must check status, generation and unresolved count.</summary>
    internal GpuShaderStorageBuffer Headers { get; }
    /// <summary>Absolute XYZ/dimension coordinates in contiguous 64-entry segments per slot.</summary>
    internal GpuShaderStorageBuffer Coordinates { get; }
    /// <summary>Persistent per-slot coverage/dependencies, with a four-word dispatch selector prefix.</summary>
    internal GpuShaderStorageBuffer Inputs => inputs;
    /// <summary>Fixed entries available to each association, shared by its declared segments.</summary>
    internal int EntryCapacity => checked(policy.SegmentsPerAssociation * SegmentSize);
    /// <summary>All owned allocations, including unused slots, replacement generations and retirement.</summary>
    internal long AllocatedBytes { get; }
    /// <summary>True only while a valid current publication and complete change interval are bound.</summary>
    internal bool BatchValid => batchValid;
    /// <summary>Identifies the current dependency-validation interval retained in GPU headers.</summary>
    internal long BatchGeneration => batchGeneration;

    /// <summary>Requires integrated candidate work to share the exact frame-wide accounting owner.</summary>
    internal bool UsesBudget(ShadowFrameBudget budget) => ReferenceEquals(frameBudget,budget);

    #region Public API
    #region Creation and scheduling
    /// <summary>Validates CPU-known coverage/lifetime before a GPU consumer checks the completed current header.</summary>
    internal bool CanConsume(ShadowCandidateHandle handle, ShadowCandidateCurrent current, ShadowWorldPublication snapshot)
    {
        var request = Require(handle);
        return batchValid && submitted[handle.Slot] && ReferenceEquals(snapshot, publication)
            && world!.Matches(snapshot) && request.Covers(current) && request.WorldOwner == snapshot.Owner
            && changes!.Epoch == current.ChangeEpoch && changes.ToSequence == current.ChangeSequence
            && DomainMatches(request, snapshot);
    }
    /// <summary>Requires a point-source movement box large enough to cover conservative displaced caster segments.</summary>
    internal bool CanConsumeEnvelope(ShadowCandidateHandle handle, ShadowCandidateCurrent current,
        ShadowWorldPublication snapshot, double paddingInChunks)
    {
        if (!double.IsFinite(paddingInChunks) || paddingInChunks < 0 || !CanConsume(handle, current, snapshot)) return false;
        var request = Require(handle);
        if (!request.IsPoint) return true;
        var p = current.PositionOrDirection;
        return request.SourceBox.Contains(new(p.X - paddingInChunks, p.Y - paddingInChunks, p.Z - paddingInChunks,
            p.X + paddingInChunks, p.Y + paddingInChunks, p.Z + paddingInChunks));
    }
    /// <summary>Validates the complete fixed layout against policy and cached hardware limits before allocating.</summary>
    internal static ShadowCandidatePool Create(ICoreAPI api, ShadowCandidateSettings settings)
    {
        var policy = settings.Freeze();
        long inputBytes = checked(16L + policy.HeaderCapacity * 1024L);
        long headerBytes = checked(policy.HeaderCapacity * 128L);
        long coordinateBytes = checked(policy.HeaderCapacity * (long)policy.SegmentsPerAssociation * SegmentSize * 16L);
        long hashBytes = checked(policy.HeaderCapacity * (long)policy.DedupCapacity * 4L);
        long changeBytes = checked(policy.ChangeCapacity * 16L);
        long total = checked(inputBytes + headerBytes + coordinateBytes + hashBytes + changeBytes);
        var caps = GpuSupport.Graphics;
        if (total > policy.MemoryMiB * 1048576L || total > int.MaxValue || caps.ApiVersion is null || caps.ApiVersion < new Version(4, 5)
            || caps.MaxComputeShaderStorageBlocks < 6 || caps.MaxShaderStorageBufferBindings < 6
            || Math.Max(Math.Max(inputBytes, coordinateBytes), Math.Max(hashBytes, changeBytes)) > caps.MaxShaderStorageBlockSize)
            throw new InvalidOperationException("Candidate layout exceeds the configured memory or GL 4.5 storage limits.");
        return new(api, policy, total, (int)inputBytes, (int)headerBytes, (int)coordinateBytes, (int)hashBytes, (int)changeBytes);
    }

    /// <summary>Reserves one fixed segment span; old and replacement associations occupy different charged slots.</summary>
    internal bool TryAllocate(ShadowCandidateRequest request, out ShadowCandidateHandle handle)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(request);
        handle = default;
        if (nextGeneration == long.MaxValue || owner <= 0) return false;
        for (int slot = 0; slot < requests.Length; slot++)
        {
            if (requests[slot] is not null) continue;
            if (retirements[slot] is { } fence)
            {
                if (!fence.TryConsumeIfSignaled()) continue;
                retirements[slot] = null;
            }
            long generation = ++nextGeneration;
            // Only the small header is reset on CPU. Hash clearing is bounded GPU continuation work.
            var header = new ShadowCandidateHeader { GenerationLow = (uint)generation, GenerationHigh = (uint)(generation >> 32),
                SequenceLow = (uint)request.ChangeSequence, SequenceHigh = (uint)(request.ChangeSequence >> 32) };
            Headers.UploadSubData<ShadowCandidateHeader>([header], slot * 128);
            requests[slot] = request; generations[slot] = generation;
            handle = new(owner, slot, generation); return true;
        }
        return false;
    }

    /// <summary>Freezes a changed-partition interval and borrows an exact native upload without copying terrain metadata.</summary>
    internal bool BeginBatch(ShadowCandidateChanges changed, ShadowWorldPublication snapshot, ShadowWorldGpuBuffers uploadedWorld,
        ShadowFrameBudget? budget = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        frameBudget=budget;
        bool sameInterval = changes is { } previous && previous.Epoch == changed.Epoch
            && previous.FromSequence == changed.FromSequence && previous.ToSequence == changed.ToSequence
            && previous.Complete == changed.Complete && !previous.Coordinates.IsDefault && !changed.Coordinates.IsDefault
            && previous.Coordinates.AsSpan().SequenceEqual(changed.Coordinates.AsSpan());
        publication = snapshot; world = uploadedWorld; changes = changed;
        Array.Clear(submitted);
        slabsLeft = policy.SlabsPerBatch; cellsLeft = policy.CellsPerBatch;
        probesLeft = policy.ProbesPerBatch; changesLeft = policy.ChangesPerBatch;
        if (batchGeneration == long.MaxValue) { batchValid = false; return false; }
        if (!sameInterval) batchGeneration++;
        batchValid = snapshot.Owner > 0 && snapshot.Generation > 0 && uploadedWorld.Matches(snapshot)
            && !snapshot.Table.IsDefaultOrEmpty && (snapshot.Table.Length & (snapshot.Table.Length - 1)) == 0
            && snapshot.ProbeLimit > 0 && snapshot.ProbeLimit <= snapshot.Table.Length
            && changed.Complete && changed.Epoch > 0 && changed.FromSequence >= 0 && changed.ToSequence >= changed.FromSequence
            && !changed.Coordinates.IsDefault && changed.Coordinates.Length <= policy.ChangeCapacity
            && (changed.ToSequence != changed.FromSequence || changed.Coordinates.IsEmpty);
        if (!batchValid) return false;
        if (!changed.Coordinates.IsEmpty)
        {
            var bytes = new int[checked(changed.Coordinates.Length * 4)];
            for (int i = 0; i < changed.Coordinates.Length; i++)
            {
                var c = changed.Coordinates[i]; bytes[i * 4] = c.X; bytes[i * 4 + 1] = c.Y;
                bytes[i * 4 + 2] = c.Z; bytes[i * 4 + 3] = c.Dimension;
            }
            changesBuffer.UploadSubData<int>(bytes.AsSpan(), 0);
        }
        return true;
    }

    /// <summary>Reserves finite whole-batch work and queues one GPU slice; returning true does not imply completion.</summary>
    internal bool Advance(ShadowCandidateHandle handle, ShadowCandidateCurrent current)
    {
        var request = Require(handle);
        if (submitted[handle.Slot]) return false;
        submitted[handle.Slot] = true;
        bool valid = batchValid && world!.Matches(publication!) && request.Covers(current)
            && publication!.Owner == request.WorldOwner && changes!.Epoch == current.ChangeEpoch
            && changes.ToSequence == current.ChangeSequence && DomainMatches(request, publication);
        int slabs = Math.Min(slabsLeft, policy.SlabsPerSlice), cells = Math.Min(cellsLeft, policy.CellsPerSlice);
        int probes = Math.Min(probesLeft, policy.ProbesPerSlice), checks = Math.Min(changesLeft, policy.ChangesPerSlice);
        if(frameBudget is { } ledger && !ledger.TryCharge(new(Slabs:slabs,Cells:cells,Probes:probes,Changes:checks)))
        { valid=false;slabs=cells=probes=checks=0; }
        // Even a zero-work slice updates dependency validity, so stale completed headers cannot survive exhaustion.
        slabsLeft -= slabs; cellsLeft -= cells; probesLeft -= probes; changesLeft -= checks;
        Pack(handle, request, current, valid, slabs, cells, probes, checks);
        inputs.UploadSubData<uint>(packed.AsSpan(), 16 + handle.Slot * 1024);
        inputs.UploadSubData<uint>([(uint)handle.Slot], 0);
        var binding = (IShadowCandidateBindings)shader;
        binding.Inputs = inputs; binding.Headers = Headers; binding.Coordinates = Coordinates;
        binding.Dedup = dedup; binding.Changes = changesBuffer;
        // Invalid batches still execute withdrawal; the shader returns before touching this harmless sentinel.
        binding.World = valid ? world!.Table : dedup;
        var commands = RHICommandContext.Current;
        if (!shader.EnsureReady())
        {
            string failure = shader.PreparationLog;
            Dispose();
            throw new InvalidOperationException(failure);
        }
        if (shader.Shader != executable)
        {
            Dispose();
            throw new InvalidOperationException("Candidate shader replacement requires a new pool and fresh associations.");
        }
        using var boundary = commands.EnterBoundary(new("Shadows.Candidates", true, compute: [shader.Pipeline]));
        boundary.Run(() =>
        {
            shader.Dispatch(1);
            commands.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate));
        });
        return true;
    }
    #endregion

    #region Lifetime
    /// <summary>Withdraws a handle before inserting a nonblocking retirement fence; reuse waits for all prior users.</summary>
    internal void Retire(ShadowCandidateHandle handle)
    {
        Require(handle);
        Headers.UploadSubData<uint>([5u], handle.Slot * 128);
        retirements[handle.Slot] = RHICommandContext.Current.InsertFence();
        requests[handle.Slot] = null;
    }

    /// <summary>Withdraws the pool and releases only its owned metadata through the existing resource lifetime.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true; batchValid = false;
        foreach (var fence in retirements) fence?.Dispose();
        shader.Dispose(); inputs.Dispose(); Headers.Dispose(); Coordinates.Dispose(); dedup.Dispose(); changesBuffer.Dispose();
        Array.Clear(requests); publication = null; world = null; changes = null;
    }
    #endregion
    #endregion

    #region Private
    #region Allocation and identity
    /// <summary>Allocates each fixed owner once; partial allocation failure releases every successful predecessor.</summary>
    private ShadowCandidatePool(ICoreAPI api, ShadowCandidateSettings policy, long bytes,
        int inputBytes, int headerBytes, int coordinateBytes, int hashBytes, int changeBytes)
    {
        this.policy = policy; requests = new ShadowCandidateRequest[policy.HeaderCapacity];
        generations = new long[policy.HeaderCapacity]; retirements = new GpuFence[policy.HeaderCapacity];
        submitted = new bool[policy.HeaderCapacity];
        shader = ShadowCandidateShader.Create(api);
        executable = shader.Shader;
        try
        {
            inputs = Allocate(inputBytes, "Inputs"); Headers = Allocate(headerBytes, "Headers");
            Coordinates = Allocate(coordinateBytes, "Coordinates"); dedup = Allocate(hashBytes, "Dedup");
            changesBuffer = Allocate(changeBytes, "Changes"); AllocatedBytes = bytes;
        }
        catch
        {
            shader.Dispose(); inputs?.Dispose(); Headers?.Dispose(); Coordinates?.Dispose(); dedup?.Dispose(); changesBuffer?.Dispose(); throw;
        }
    }
    /// <summary>Requests fixed mutable RHI storage; callers initialize header/hash entries before any shader reads.</summary>
    private static GpuShaderStorageBuffer Allocate(int bytes, string label) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable,
            Capacity = bytes, Usage = BufferUsage.DynamicDraw, Label = "Shadows.Candidates." + label }).RequireResource();
    /// <summary>Rejects foreign, retired and recycled slots without reading GPU selection results.</summary>
    private ShadowCandidateRequest Require(ShadowCandidateHandle handle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (handle.Owner != owner || handle.Slot < 0 || handle.Slot >= requests.Length
            || generations[handle.Slot] != handle.Generation || requests[handle.Slot] is not { } request)
            throw new InvalidOperationException("Candidate handle is not current in this pool.");
        return request;
    }
    /// <summary>Requires the full native block-unit envelope, preventing a work batch from shrinking its search domain.</summary>
    private static bool DomainMatches(ShadowCandidateRequest request, ShadowWorldPublication snapshot)
    {
        if (!snapshot.Envelopes.TryGetValue(request.Domain.Dimension, out var worldBounds)) return false;
        var d = request.Domain.Bounds;
        return worldBounds == new ShadowBounds(d.MinX * 32, d.MinY * 32, d.MinZ * 32, d.MaxX * 32, d.MaxY * 32, d.MaxZ * 32);
    }
    #endregion

    #region Shader packing
    /// <summary>Writes exact integer identities and local double bounds into the documented 1024-byte slot record.</summary>
    private void Pack(ShadowCandidateHandle handle, ShadowCandidateRequest request, ShadowCandidateCurrent current,
        bool valid, int slabs, int cells, int probes, int checks)
    {
        Array.Clear(packed);
        packed[0] = (uint)handle.Slot; packed[1] = (uint)EntryCapacity; packed[2] = (uint)policy.DedupCapacity; packed[3] = (uint)policy.ProbeLimit;
        packed[4] = (uint)slabs; packed[5] = (uint)cells; packed[6] = (uint)probes; packed[7] = (uint)checks;
        packed[8] = request.IsPoint ? 1u : 0u; packed[9] = (uint)request.Sectors.Length; packed[10] = valid ? 1u : 0u;
        packed[12] = publication is { } snapshot && !snapshot.Table.IsDefaultOrEmpty ? (uint)(snapshot.Table.Length - 1) : 0;
        packed[13] = (uint)(publication?.ProbeLimit ?? 1);
        packed[14] = changes is { } delta && !delta.Coordinates.IsDefault ? (uint)delta.Coordinates.Length : 0;
        packed[15] = (uint)request.Domain.Dimension;
        packed[16] = (uint)request.Domain.MinX; packed[17] = (uint)request.Domain.MinY; packed[18] = (uint)request.Domain.MinZ;
        packed[19] = request.NeedsRefresh(current, policy.RefreshGuardFraction) ? 1u : 0u;
        packed[20] = (uint)((long)request.Domain.MaxX - request.Domain.MinX);
        packed[21] = (uint)((long)request.Domain.MaxY - request.Domain.MinY); packed[22] = (uint)((long)request.Domain.MaxZ - request.Domain.MinZ);
        Long(24, current.ChangeSequence); Long(26, changes?.FromSequence ?? 0); Long(28, changes?.ToSequence ?? 0);
        Long(30, batchGeneration); Long(32, publication?.Generation ?? 0); Long(34, handle.Generation);
        Bounds(40, request.Receiver, request.Domain);
        Bounds(56, request.IsPoint ? request.SourceBox : request.Receiver, request.Domain);
        for (int i = 0; i < request.Sectors.Length; i++)
        {
            var sector = request.Sectors[i]; int offset = 72 + i * 16;
            packed[offset] = (uint)sector.Axis; packed[offset + 1] = (uint)sector.Sign;
            Double(offset + 4, sector.MinA); Double(offset + 6, sector.MaxA); Double(offset + 8, sector.MinB); Double(offset + 10, sector.MaxB);
        }
        // Persistent identity and coverage are available to later GPU consumers without reconstructing frame-list identity.
        Long(176, request.Source.World); Long(178, request.Source.Owner); Long(180, request.Source.Lifetime);
        packed[182] = (uint)request.Source.Kind; packed[183] = (uint)request.Source.Slot;
        Long(184, request.WorldOwner); Long(186, request.DomainGeneration); Long(188, request.InfluenceGeneration);
        Long(190, request.ChangeEpoch); Double(192, request.Direction.X); Double(194, request.Direction.Y);
        Double(196, request.Direction.Z); Double(198, request.RadiusDegrees);
        packed[200] = (uint)request.Source.X; packed[201] = (uint)request.Source.Y; packed[202] = (uint)request.Source.Z;
    }
    /// <summary>Splits version words with explicit unsigned semantics shared by the baseline shader.</summary>
    private void Long(int index, long value) { packed[index] = (uint)value; packed[index + 1] = (uint)(value >> 32); }
    /// <summary>Preserves the exact double payload without depending on CLR vector packing.</summary>
    private void Double(int index, double value) => Long(index, BitConverter.DoubleToInt64Bits(value));
    /// <summary>Subtracts the integer origin before GPU arithmetic, retaining native coordinates in output.</summary>
    private void Bounds(int index, ShadowBounds bounds, ShadowChunkDomain domain)
    {
        Double(index, bounds.MinX - domain.MinX); Double(index + 2, bounds.MinY - domain.MinY); Double(index + 4, bounds.MinZ - domain.MinZ);
        Double(index + 8, bounds.MaxX - domain.MinX); Double(index + 10, bounds.MaxY - domain.MinY); Double(index + 12, bounds.MaxZ - domain.MinZ);
    }
    #endregion
    #endregion
}
