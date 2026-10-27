using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns uploaded vertex and index buffers while supplying immutable packets to backend rendering.</summary>
internal sealed class ManagedGraphicsGeometry : GraphicsGeometry
{
    private readonly Dictionary<int, GpuVbo> buffers = new();
    private readonly RHIGeometryBorrow borrow = new();
    private readonly RHIGeometryInputs inputs;
    private GpuEbo? elements;
    private bool disposed;

    #region Public API
    /// <summary>Gets the fixed uploaded streams without a native vertex-array or draw capability.</summary>
    internal override RHIGeometryInputs Inputs => inputs;

    /// <summary>Validates supplied uploads and publishes only the complete set of owned buffer references.</summary>
    internal static ManagedGraphicsGeometry Create(RHIVertexLayout layout, PrimitiveTopology topology,
        IReadOnlyList<GraphicsVertexData> streams, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(indices);
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        if (!Enum.IsDefined(topology)) throw new ArgumentOutOfRangeException(nameof(topology));
        if (!streams.Select(stream => stream.Binding).Order().SequenceEqual(layout.Attributes.Select(attribute => attribute.Binding).Distinct().Order())
            || streams.Any(stream => stream.Data is null))
            throw new ArgumentException("Supply exactly one upload for every vertex binding.", nameof(streams));
        return new(layout, streams, indices);
    }

    /// <summary>Expires captured packets before retiring only this producer's owned storage.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        borrow.Dispose();
        elements?.Dispose(); elements = null;
        foreach (var buffer in buffers.Values) buffer.Dispose();
        buffers.Clear();
    }
    #endregion

    #region Private
    /// <summary>Uploads through semantic buffer operations; the backend realizes a VAO only when a packet executes.</summary>
    private ManagedGraphicsGeometry(RHIVertexLayout layout, IReadOnlyList<GraphicsVertexData> streams, uint[] indices)
    {
        try
        {
            var context = RHICommandContext.Current;
            // Realize names first; the following uploads provide the only allocation and its complete contents.
            elements = context.CreateIndexBuffer(new RHIBufferDescription
                { Role = BufferRole.Index, Mode = ResourceStorageMode.Realization }).RequireResource();
            elements.UploadIndices(indices);
            foreach (var stream in streams)
            {
                var buffer = context.CreateVertexBuffer(new RHIBufferDescription
                    { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Realization }).RequireResource();
                buffers.Add(stream.Binding, buffer);
                buffer.UploadData(stream.Data);
            }
            var inputs = layout.Attributes.GroupBy(attribute => attribute.Binding).Select(group =>
            {
                var first = group.First();
                var buffer = buffers[first.Binding];
                return new RHIVertexStream(first.Binding, new(buffer, 0, buffer.SizeBytes), first.Stride, first.Divisor);
            });
            this.inputs = new(layout, inputs, new RHIIndexStream(new(elements, 0, elements.SizeBytes)), 0, borrow);
        }
        catch { Dispose(); throw; }
    }
    #endregion
}
