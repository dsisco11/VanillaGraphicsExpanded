using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates geometry descriptors and published allocation bounds without inspecting index or indirect contents.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    #region Common inputs
    /// <summary>Checks packet admission, pipeline compatibility and cached device limits before native configuration or draw effects.</summary>
    private void ValidateGeometryInputs(RHIGraphicsPipeline pipeline, RHIGeometryInputs geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        geometry.RequireLive();
        geometry.ImportedArray?.RequireCurrent(NativeOrigin, cache.VertexConfigurationEpoch);
        if (geometry.Layout != pipeline.Description.VertexLayout)
            throw new InvalidOperationException("Geometry layout does not match the selected graphics pipeline.");
        foreach (var attribute in geometry.Layout.Attributes)
            if (attribute.Location >= NativeCapabilities.MaxVertexAttributes
                || attribute.Binding >= NativeCapabilities.MaxVertexBindings
                || attribute.Stride > NativeCapabilities.MaxVertexStride
                || attribute.Offset > NativeCapabilities.MaxVertexRelativeOffset)
                throw new ArgumentException("Vertex layout exceeds cached device limits.", nameof(geometry));
        foreach (var stream in geometry.Streams)
            RequireRange(stream.Range.Buffer, stream.Range.Offset, stream.Range.Length);
        if (geometry.Indices is { } indices)
            RequireRange(indices.Range.Buffer, indices.Range.Offset, indices.Range.Length);
    }

    /// <summary>Retains the same original stream allocations for the complete native submission interval.</summary>
    private void RetainGeometryInputs(RHIGeometryInputs geometry)
    {
        foreach (var stream in geometry.Streams) RetainExecutionResource(stream.Range.Buffer.RequireResource());
        if (geometry.Indices is { } indices) RetainExecutionResource(indices.Range.Buffer.RequireResource());
    }
    #endregion

    #region Direct ranges
    /// <summary>Checks exact vertex and instance fetch bounds using frozen packing and current published capacities.</summary>
    private static void ValidateDirectDraw(RHIGeometryInputs geometry, in RHIDraw draw)
    {
        if (draw.FirstVertex < 0 || draw.VertexCount < 0 || draw.InstanceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(draw));
        bool fetch = draw.VertexCount != 0 && draw.InstanceCount != 0;
        long lastVertex = (long)draw.FirstVertex + draw.VertexCount - 1;
        if (fetch && geometry.Layout.Attributes.Count == 0 && lastVertex >= geometry.ProceduralVertexCount)
            throw new ArgumentOutOfRangeException(nameof(draw), "Procedural draw exceeds its producer-declared vertex range.");
        ValidateVertexFetches(geometry, fetch ? lastVertex : -1, draw.InstanceCount, draw.BaseInstance, fetch);
    }

    /// <summary>Checks the selected index bytes and instance fetches while leaving actual index values to their producer.</summary>
    private static void ValidateIndexedDraw(RHIGeometryInputs geometry, in RHIDrawIndexed draw)
    {
        if (draw.FirstIndex < 0 || draw.IndexCount < 0 || draw.InstanceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(draw));
        var indices = geometry.Indices ?? throw new InvalidOperationException("Indexed drawing requires an explicit index stream.");
        int width = RHIGeometryInputs.IndexWidth(indices.Format);
        long end = checked(((long)draw.FirstIndex + draw.IndexCount) * width);
        if (end > indices.Range.Length)
            throw new ArgumentOutOfRangeException(nameof(draw), "Draw exceeds the supplied index interval.");
        // BaseVertex and actual indices determine per-vertex addresses. Their producer owns
        // those values; descriptor validation must never synchronize or read them back.
        ValidateVertexFetches(geometry, -1, draw.InstanceCount, draw.BaseInstance,
            draw.IndexCount != 0 && draw.InstanceCount != 0);
    }

    /// <summary>Checks the last addressed byte of each represented attribute, including base-instance fetch semantics.</summary>
    private static void ValidateVertexFetches(RHIGeometryInputs geometry, long lastVertex,
        int instanceCount, uint baseInstance, bool fetch)
    {
        if (!fetch) return;
        foreach (var stream in geometry.Streams)
        {
            // Streams are frozen in binding order; attributes are sorted by location,
            // so visit the matching attributes without assuming location equals binding.
            foreach (var attribute in geometry.Layout.Attributes)
            {
                if (attribute.Binding != stream.Binding) continue;
                long last = stream.Divisor == 0 ? lastVertex
                    : (long)baseInstance + (instanceCount - 1L) / stream.Divisor;
                // Indexed per-vertex values are producer-owned. Still require enough bytes
                // to represent an attribute at all when the draw can perform a fetch.
                long end = checked(Math.Max(0, last) * stream.Stride + attribute.Offset
                    + VertexScalarWidth(attribute.Storage) * attribute.Components);
                if (end > stream.Range.Length)
                    throw new ArgumentOutOfRangeException(nameof(geometry), "Vertex or instance fetch exceeds its supplied stream interval.");
            }
        }
    }
    #endregion

    #region Indirect descriptors
    /// <summary>Checks argument byte intervals and optional count storage without reading GPU-generated command values.</summary>
    private void ValidateIndirectDraw(RHIGeometryInputs geometry, in RHIIndirectDraw draw, bool indexed)
    {
        if (indexed && geometry.Indices is null)
            throw new InvalidOperationException("Indexed indirect drawing requires an index stream.");
        int width = indexed ? 20 : 16;
        int stride = draw.Stride == 0 ? width : draw.Stride;
        if (draw.Count < 0 || stride < width || stride % 4 != 0 || draw.Arguments.Offset % 4 != 0)
            throw new ArgumentException("Invalid indirect count, stride or argument alignment.", nameof(draw));
        long bytes = draw.Count == 0 ? 0 : checked((draw.Count - 1L) * stride + width);
        if (bytes > draw.Arguments.Length)
            throw new ArgumentException("Indirect commands exceed their supplied argument interval.", nameof(draw));
        RequireRange(draw.Arguments.Buffer, draw.Arguments.Offset, draw.Arguments.Length);
        if (draw.CountBuffer is { } count)
        {
            if (!Capabilities.IndirectDrawCount) throw new NotSupportedException("Indirect count drawing is unavailable.");
            if (count.Offset % 4 != 0 || count.Length < 4)
                throw new ArgumentException("Indirect count storage requires an aligned four-byte interval.", nameof(draw));
            RequireRange(count.Buffer, count.Offset, count.Length);
        }
    }
    #endregion

    /// <summary>Gets represented scalar byte extent independently of native enum values.</summary>
    private static int VertexScalarWidth(VertexComponentFormat format) => format switch
    {
        VertexComponentFormat.SignedByte or VertexComponentFormat.UnsignedByte => 1,
        VertexComponentFormat.SignedShort or VertexComponentFormat.UnsignedShort or VertexComponentFormat.Half => 2,
        VertexComponentFormat.SignedInt or VertexComponentFormat.UnsignedInt or VertexComponentFormat.Float => 4,
        VertexComponentFormat.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    #endregion
}
