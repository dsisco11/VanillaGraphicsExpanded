using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VanillaGraphicsExpanded.Collections;

/// <summary>Owns a pooled array until disposal; borrowed memory and spans must not outlive the owner.</summary>
public sealed class PooledArray<T> : IMemoryOwner<T>
{
    private readonly ArrayPool<T> pool;
    private T[]? array;

    /// <summary>Gets the logical element count, excluding unused pool capacity.</summary>
    public int Length { get; }

    /// <summary>Gets the logical memory range, rejecting access after disposal.</summary>
    public Memory<T> Memory => (Volatile.Read(ref array) ?? throw new ObjectDisposedException(nameof(PooledArray<T>))).AsMemory(0, Length);

    /// <summary>Borrows the logical elements while this owner remains alive.</summary>
    public Span<T> Span => Memory.Span;

    #region Ownership
    /// <summary>Rents storage from the selected pool without exposing excess capacity.</summary>
    private PooledArray(int length, ArrayPool<T> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        this.pool = pool;
        Length = length;
        array = pool.Rent(length);
    }

    /// <summary>Creates an owner whose logical elements are uninitialized until the caller writes them.</summary>
    public static PooledArray<T> Rent(int length, ArrayPool<T>? pool = null) => new(length, pool ?? ArrayPool<T>.Shared);

    /// <summary>Returns storage exactly once, clearing references so the pool cannot retain owned objects.</summary>
    public void Dispose()
    {
        // Reference ownership avoids double-return from copied value types; exchange also
        // makes repeated or concurrent disposal harmless. Borrowing must end before disposal.
        T[]? returned = Interlocked.Exchange(ref array, null);
        if (returned != null) pool.Return(returned, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }
    #endregion
}
