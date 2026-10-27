using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes geometry consumption inputs without owning buffers, a native VAO, or a draw executor.</summary>
public sealed class RHIGeometryInputs
{
    private readonly RHIGeometryBorrow? borrow;

    #region Public API
    /// <summary>Gets the immutable attribute interpretation expected by the streams.</summary>
    public RHIVertexLayout Layout { get; }
    /// <summary>Gets copied streams in binding order.</summary>
    public ImmutableArray<RHIVertexStream> Streams { get; }
    /// <summary>Gets optional indexed geometry; nonindexed draws never infer an index stream.</summary>
    public RHIIndexStream? Indices { get; }
    /// <summary>Gets the producer-declared direct range for vertex-ID-only geometry.</summary>
    public int ProceduralVertexCount { get; }

    /// <summary>Validates supplied shape and freezes references without acquiring metadata or requiring a native context.</summary>
    public RHIGeometryInputs(RHIVertexLayout layout, IEnumerable<RHIVertexStream> streams,
        RHIIndexStream? indices = null, int proceduralVertexCount = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(streams);
        if (proceduralVertexCount < 0) throw new ArgumentOutOfRangeException(nameof(proceduralVertexCount));
        Layout = layout;
        Streams = streams.OrderBy(stream => stream.Binding).ToImmutableArray();
        Indices = indices;
        ProceduralVertexCount = proceduralVertexCount;
        // Layout owns attribute formats. Stream selectors must agree with its packing,
        // and execution later checks the frozen intervals against current published capacity.
        var declarations = layout.Attributes.GroupBy(attribute => attribute.Binding)
            .Select(group => group.First()).OrderBy(attribute => attribute.Binding).ToArray();
        if (declarations.Length != Streams.Length)
            throw new ArgumentException("Supply exactly one stream for every layout binding.", nameof(streams));
        for (int index = 0; index < Streams.Length; index++)
        {
            var stream = Streams[index];
            var declaration = declarations[index];
            if (stream.Binding != declaration.Binding || stream.Stride != declaration.Stride
                || stream.Divisor != declaration.Divisor || stream.Range.Buffer.IsEmpty
                || stream.Range.Offset < 0 || stream.Range.Length < 0)
                throw new ArgumentException("Stream packing or range disagrees with its layout.", nameof(streams));
            _ = checked(stream.Range.Offset + stream.Range.Length);
        }
        if (indices is { } element)
        {
            int width = IndexWidth(element.Format);
            if (element.Range.Buffer.IsEmpty || element.Range.Offset < 0 || element.Range.Length < 0
                || element.Range.Offset % width != 0 || element.Range.Length % width != 0)
                throw new ArgumentException("Index storage requires an aligned interval and represented scalar format.", nameof(indices));
            _ = checked(element.Range.Offset + element.Range.Length);
        }
    }
    #endregion

    #region Internal API
    /// <summary>Gets optional already certified external array identity without exposing native selectors publicly.</summary>
    internal RHIGeometryImport? ImportedArray { get; }

    /// <summary>Shares frozen resource inputs with a new synchronous borrowing interval, without metadata access.</summary>
    internal RHIGeometryInputs(RHIGeometryInputs source, RHIGeometryBorrow borrow)
    {
        Layout = source.Layout; Streams = source.Streams; Indices = source.Indices;
        ProceduralVertexCount = source.ProceduralVertexCount; ImportedArray = source.ImportedArray;
        this.borrow = borrow ?? throw new ArgumentNullException(nameof(borrow));
    }

    /// <summary>Freezes an explicitly certified external array with its streams and independent ownership interval.</summary>
    internal RHIGeometryInputs(RHIVertexLayout layout, IEnumerable<RHIVertexStream> streams,
        RHIIndexStream? indices, RHIGeometryImport importedArray, RHIGeometryBorrow borrow)
        : this(layout, streams, indices, 0, borrow)
        => ImportedArray = importedArray ?? throw new ArgumentNullException(nameof(importedArray));

    /// <summary>Freezes an adapter-owned packet with its separate CPU borrowing interval.</summary>
    internal RHIGeometryInputs(RHIVertexLayout layout, IEnumerable<RHIVertexStream> streams,
        RHIIndexStream? indices, int proceduralVertexCount, RHIGeometryBorrow borrow)
        : this(layout, streams, indices, proceduralVertexCount)
        => this.borrow = borrow ?? throw new ArgumentNullException(nameof(borrow));

    /// <summary>Rejects an expired adapter before execution inspects any backing resource.</summary>
    internal void RequireLive() => borrow?.RequireLive();
    /// <summary>Associates private native realization cleanup with an explicitly owned adapter lifetime when present.</summary>
    internal void RetireWithBorrow(Action retire) => borrow?.RetireWithBorrow(retire);

    /// <summary>Gets the byte width using the semantic format rather than native token arithmetic.</summary>
    internal static int IndexWidth(IndexFormat format) => format switch
    {
        IndexFormat.UnsignedByte => 1,
        IndexFormat.UnsignedShort => 2,
        IndexFormat.UnsignedInt => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    #endregion
}
