using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns immutable indexed geometry configured through the existing vertex-binding helper.</summary>
internal sealed class ManagedGraphicsGeometry : GraphicsGeometry
{
    private readonly VertexLayoutDesc layout;
    private readonly PrimitiveType topology;
    private readonly uint[] indices;
    private readonly Dictionary<int, GpuVbo> buffers = new();
    private readonly GpuVao vao;
    private readonly GpuEbo elements;
    private bool disposed;

    #region Public API
    /// <summary>Validates upload contracts and publishes geometry only after all native setup succeeds.</summary>
    internal static ManagedGraphicsGeometry Create(VertexLayoutDesc layout, PrimitiveType topology,
        IReadOnlyList<GraphicsVertexData> streams, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(indices);
        StateCache.Current.RequireOutsideEngineBoundary();
        if (!Enum.IsDefined(topology)) throw new ArgumentOutOfRangeException(nameof(topology));
        var bindings = layout.Attributes.Select(a => a.Binding).Distinct().Order().ToArray();
        if (!streams.Select(s => s.Binding).Order().SequenceEqual(bindings)
            || streams.Any(s => s.Data is null))
            throw new ArgumentException("Supply exactly one upload for every vertex binding.", nameof(streams));
        return new(layout, topology, streams, indices);
    }

    /// <inheritdoc />
    internal override void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!vao.IsValid || !elements.IsValid || buffers.Values.Any(b => !b.IsValid))
            throw new InvalidOperationException("Geometry storage has retired.");
        if (pipeline.VertexLayout != layout || pipeline.Assembly.Topology != topology)
            throw new InvalidOperationException("Geometry layout or topology does not match the pipeline.");
        if (draw.FirstIndex < 0 || draw.IndexCount <= 0 || draw.FirstIndex > indices.Length - draw.IndexCount
            || draw.InstanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(draw));
        // CPU upload data is retained solely for validating requested index subranges, never read back per draw.
        long minimum = long.MaxValue, maximum = -1;
        for (int i = draw.FirstIndex; i < draw.FirstIndex + draw.IndexCount; i++)
        {
            uint index = indices[i];
            if (pipeline.Assembly.FixedIndexRestart ? index == uint.MaxValue
                : pipeline.Assembly.Restart && index == pipeline.Assembly.RestartIndex) continue;
            long vertex = (long)index + draw.BaseVertex;
            minimum = Math.Min(minimum, vertex);
            maximum = Math.Max(maximum, vertex);
        }
        if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(draw), "Negative vertex index.");
        foreach (var attribute in layout.Attributes)
        {
            long last = attribute.Divisor == 0 ? maximum : (draw.InstanceCount - 1L) / attribute.Divisor;
            if (last >= 0 && last >= buffers[attribute.Binding].SizeBytes / attribute.Stride)
                throw new ArgumentOutOfRangeException(nameof(draw), "Vertex or instance range exceeds uploaded storage.");
        }
    }

    /// <inheritdoc />
    internal override void Submit(GraphicsDraw draw)
    {
        vao.Bind();
        StateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, elements.BufferId);
        GL.DrawElementsInstancedBaseVertex(topology, draw.IndexCount, DrawElementsType.UnsignedInt,
            (IntPtr)((long)draw.FirstIndex * sizeof(uint)), draw.InstanceCount, draw.BaseVertex);
        StateCache.Current.RecordDrawSubmission();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        vao.Dispose();
        elements.Dispose();
        foreach (var buffer in buffers.Values) buffer.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Uses scoped binding restoration during cold upload and owns every resulting native resource.</summary>
    private ManagedGraphicsGeometry(VertexLayoutDesc layout, PrimitiveType topology,
        IReadOnlyList<GraphicsVertexData> streams, uint[] indices)
    {
        this.layout = layout;
        this.topology = topology;
        this.indices = (uint[])indices.Clone();
        vao = GpuVao.Create();
        try { elements = GpuEbo.Create(); }
        catch { vao.Dispose(); throw; }
        try
        {
            using var vertexBinding = vao.BindScope();
            using var arrayBinding = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
            using var errors = new GlDebug.ErrorScope("Graphics geometry upload");
            elements.UploadIndices(this.indices);
            vao.BindElementBuffer(elements);
            foreach (var stream in streams)
            {
                var buffer = GpuVbo.Create();
                buffers.Add(stream.Binding, buffer);
                buffer.UploadData(stream.Data);
                var attributes = layout.Attributes.Where(a => a.Binding == stream.Binding).ToArray();
                var binding = vao.GetBinding(stream.Binding).BindVertexBuffer(buffer.BufferId, 0, attributes[0].Stride)
                    .SetDivisor(attributes[0].Divisor);
                foreach (var attribute in attributes) Configure(binding, attribute);
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Maps authored interpretation to the existing floating/integer helper and explicit double path.</summary>
    private static void Configure(GpuVertexAttribBinding binding, VertexAttributeDesc attribute)
    {
        if (attribute.Interpretation == VertexInterpretation.Integer)
            binding.SetIntAttrib(attribute.Location, attribute.Components,
                (VertexAttribIntegerType)attribute.Storage, attribute.Offset);
        else if (attribute.Interpretation == VertexInterpretation.Double)
        {
            StateCache.Current.BindVertexArray(binding.VaoId);
            StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, binding.BufferId);
            GL.EnableVertexAttribArray(attribute.Location);
            GL.VertexAttribLPointer(attribute.Location, attribute.Components, VertexAttribDoubleType.Double,
                binding.StrideBytes, (IntPtr)attribute.Offset);
            GL.VertexAttribDivisor(attribute.Location, binding.Divisor);
        }
        else binding.SetFloatAttrib(attribute.Location, attribute.Components, attribute.Storage,
            attribute.Interpretation == VertexInterpretation.Normalized, attribute.Offset);
    }
    #endregion
}
