using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Supplies immutable stream packets while borrowing independently owned streaming buffers.</summary>
internal sealed class ArrayGraphicsGeometry : GraphicsGeometry
{
    private readonly RHIVertexLayout layout;
    private readonly Dictionary<int, GpuVbo> buffers;
    private readonly int proceduralVertices;
    private readonly RHIGeometryBorrow borrow = new();
    private RHIGeometryInputs? inputs;

    #region Public API
    /// <summary>Captures new declared extents only when streaming capacity changes; unchanged packets are reused.</summary>
    internal override RHIGeometryInputs Inputs
    {
        get
        {
            borrow.RequireLive();
            if (inputs is not null && inputs.Streams.All(stream => stream.Range.Length == buffers[stream.Binding].SizeBytes)) return inputs;
            // A packet keeps these declared bounds even when the producer enlarges storage later.
            var streams = layout.Attributes.GroupBy(attribute => attribute.Binding).Select(group =>
            {
                var first = group.First();
                var buffer = buffers[first.Binding];
                return new RHIVertexStream(first.Binding, new(buffer, 0, buffer.SizeBytes), first.Stride, first.Divisor);
            });
            return inputs = new(layout, streams, null, proceduralVertices, borrow);
        }
    }

    /// <summary>Freezes stream ownership and layout without allocating or configuring a native VAO.</summary>
    internal ArrayGraphicsGeometry(RHIVertexLayout layout, PrimitiveTopology topology,
        IReadOnlyDictionary<int, GpuVbo> buffers, int proceduralVertices = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(buffers);
        if (!Enum.IsDefined(topology) || proceduralVertices < 0) throw new ArgumentOutOfRangeException(nameof(topology));
        if (!layout.Attributes.Select(attribute => attribute.Binding).Distinct().Order().SequenceEqual(buffers.Keys.Order())
            || layout.Attributes.Count == 0 && proceduralVertices == 0)
            throw new ArgumentException("Supply every declared stream or a bounded procedural range.");
        this.layout = layout;
        this.buffers = new(buffers);
        this.proceduralVertices = proceduralVertices;
    }

    /// <summary>Expires packets and backend realizations while preserving externally owned streaming buffers.</summary>
    public override void Dispose() => borrow.Dispose();
    #endregion
}
