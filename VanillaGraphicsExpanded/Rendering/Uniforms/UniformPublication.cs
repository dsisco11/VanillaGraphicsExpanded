using System;

namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Owns one logical block's publication while borrowing physical storage from context allocators.</summary>
internal sealed class UniformPublication : IDisposable
{
    private readonly UniformBufferUsage usage;
    private GpuUniformRingBuffer? allocator;
    private GpuUniformRingBuffer.Allocation transient;
    private ulong revision;
    private bool disposed;

    /// <summary>Describes one candidate without committing ownership or dirty CPU work.</summary>
    internal readonly record struct Candidate(GpuUniformRingBuffer.Allocation Transient)
    {
        internal GpuUniformBuffer Buffer => Transient.Buffer;
        internal int Offset => Transient.OffsetBytes;
        internal int Size => Transient.SizeBytes;
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
            throw new NotSupportedException("Retained uniform storage is not available.");
        if (usage == UniformBufferUsage.SingleFrame && unchanged && ring.CanReuse(transient)) return new(transient);
        return new(ring.AllocateAndWrite(bytes));
    }

    /// <summary>Commits only after binding succeeds, retaining the successfully bound immutable range.</summary>
    internal void Commit(GpuUniformRingBuffer ring, in Candidate candidate, ulong contentRevision)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        allocator = ring; transient = candidate.Transient; revision = contentRevision;
    }

    /// <summary>Abandons a candidate without replacing the previous successful publication.</summary>
    internal void Abort(in Candidate candidate)
    {
        // Ring reservations remain immutable until their epoch ends, including failed publications.
    }

    /// <summary>Terminates this logical publication; the ring retains physical storage until epoch retirement.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        allocator = null; transient = default;
    }
    #endregion
}
