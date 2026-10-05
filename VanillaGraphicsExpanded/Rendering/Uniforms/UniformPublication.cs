using System;

namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Owns one logical block's publication while borrowing physical storage from context allocators.</summary>
internal sealed class UniformPublication : IDisposable
{
    private readonly UniformBufferUsage usage;
    private GpuUniformRingBuffer? allocator;
    private GpuUniformRingBuffer.Allocation transient;
    private UniformStorageVersion? retained;
    private ulong revision;
    private bool disposed;

    /// <summary>Describes one candidate without committing ownership or dirty CPU work.</summary>
    internal readonly record struct Candidate(GpuUniformRingBuffer.Allocation Transient, UniformStorageVersion? Retained)
    {
        internal GpuUniformBuffer Buffer => Retained?.Page.Buffer ?? Transient.Buffer;
        internal int Offset => Retained?.Offset ?? Transient.OffsetBytes;
        internal int Size => Retained?.Size ?? Transient.SizeBytes;
    }

    #region Public API
    /// <summary>Fixes lifetime policy for this logical instance.</summary>
    internal UniformPublication(UniformBufferUsage usage)
    {
        if (!Enum.IsDefined(usage)) throw new ArgumentOutOfRangeException(nameof(usage));
        this.usage = usage;
    }

    /// <summary>Selects a current immutable version or reserves and uploads a new complete version.</summary>
    internal Candidate Prepare(GpuUniformRingBuffer ring, ReadOnlySpan<byte> bytes, ulong contentRevision)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ring.RequireOpenPublication();
        bool unchanged = ReferenceEquals(allocator, ring) && revision == contentRevision;
        if (usage == UniformBufferUsage.MultiFrame)
        {
            var pool = ring.PersistentStorage;
            if (unchanged && retained is not null && pool.IsCurrent(retained)) return new(default, retained);
            return new(default, pool.Allocate(bytes));
        }
        if (usage == UniformBufferUsage.SingleFrame && unchanged && ring.CanReuse(transient)) return new(transient, null);
        return new(ring.AllocateAndWrite(bytes), null);
    }

    /// <summary>Commits only after binding succeeds, recording even unchanged persistent rebindings.</summary>
    internal void Commit(GpuUniformRingBuffer ring, in Candidate candidate, ulong contentRevision)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        candidate.Retained?.Owner.MarkUsed(candidate.Retained);
        if (retained is not null && !ReferenceEquals(retained, candidate.Retained)) retained.Owner.Release(retained);
        allocator = ring; transient = candidate.Transient; retained = candidate.Retained; revision = contentRevision;
    }

    /// <summary>Releases only a newly reserved candidate; failed publication preserves the previous version.</summary>
    internal void Abort(in Candidate candidate)
    {
        if (candidate.Retained is not null && !ReferenceEquals(candidate.Retained, retained))
            candidate.Retained.Owner.Release(candidate.Retained);
        // Ring reservations remain immutable until their epoch ends, including failed publications.
    }

    /// <summary>Terminates this logical publication and queues its retained range for safe retirement.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        retained?.Owner.Release(retained);
        retained = null; allocator = null; transient = default;
    }
    #endregion
}
