using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns immutable vertex-array configuration while borrowing mutable streaming buffers.</summary>
internal sealed class ArrayGraphicsGeometry : GraphicsGeometry
{
    private readonly GpuVao vao;
    private readonly VertexLayoutDesc layout;
    private readonly PrimitiveType topology;
    private readonly Dictionary<int, GpuVbo> buffers;
    private readonly int proceduralVertices;
    private bool disposed;

    #region Public API
    /// <summary>Configures a private VAO through the existing binding owner; empty layouts require an explicit procedural range.</summary>
    internal ArrayGraphicsGeometry(VertexLayoutDesc layout, PrimitiveType topology,
        IReadOnlyDictionary<int, GpuVbo> buffers, int proceduralVertices = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(buffers);
        if (!Enum.IsDefined(topology) || proceduralVertices < 0) throw new ArgumentOutOfRangeException(nameof(topology));
        if (!layout.Attributes.Select(a => a.Binding).Distinct().Order().SequenceEqual(buffers.Keys.Order())
            || (layout.Attributes.Count == 0 && proceduralVertices == 0))
            throw new ArgumentException("Supply every declared stream or a bounded procedural vertex range.");
        this.layout = layout;
        this.topology = topology;
        this.buffers = new(buffers);
        this.proceduralVertices = proceduralVertices;
        vao = GpuVao.Create("Graphics.ArrayGeometry");
        try
        {
            using var vertexScope = vao.BindScope();
            using var arrayScope = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
            using var errors = new GlDebug.ErrorScope("Array geometry configuration");
            foreach (var group in layout.Attributes.GroupBy(a => a.Binding))
            {
                var buffer = this.buffers[group.Key];
                if (!buffer.IsValid) throw new InvalidOperationException("Vertex stream has retired.");
                var first = group.First();
                var binding = vao.GetBinding(group.Key).BindVertexBuffer(buffer.BufferId, 0, first.Stride).SetDivisor(first.Divisor);
                foreach (var attribute in group)
                {
                    if (attribute.Interpretation == VertexInterpretation.Double)
                        throw new NotSupportedException("Double streaming attributes require an explicit adapter.");
                    if (attribute.Interpretation == VertexInterpretation.Integer)
                        binding.SetIntAttrib(attribute.Location, attribute.Components, (VertexAttribIntegerType)attribute.Storage, attribute.Offset);
                    else binding.SetFloatAttrib(attribute.Location, attribute.Components, attribute.Storage,
                        attribute.Interpretation == VertexInterpretation.Normalized, attribute.Offset);
                }
            }
        }
        catch { vao.Dispose(); throw; }
    }

    /// <inheritdoc />
    internal override void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!vao.IsValid || buffers.Values.Any(b => !b.IsValid)) throw new InvalidOperationException("Geometry storage retired.");
        if (pipeline.VertexLayout != layout || pipeline.Assembly.Topology != topology)
            throw new InvalidOperationException("Array geometry does not match the selected pipeline.");
        if (draw.FirstIndex < 0 || draw.IndexCount <= 0 || draw.InstanceCount <= 0 || draw.BaseVertex != 0)
            throw new ArgumentOutOfRangeException(nameof(draw));
        long end = (long)draw.FirstIndex + draw.IndexCount;
        if (layout.Attributes.Count == 0 && end > proceduralVertices) throw new ArgumentOutOfRangeException(nameof(draw));
        foreach (var attribute in layout.Attributes)
        {
            long count = attribute.Divisor == 0 ? end : (draw.InstanceCount - 1L) / attribute.Divisor + 1;
            if (count > buffers[attribute.Binding].SizeBytes / attribute.Stride)
                throw new ArgumentOutOfRangeException(nameof(draw), "Draw exceeds the current streaming buffer storage.");
        }
    }

    /// <inheritdoc />
    internal override void Submit(GraphicsDraw draw)
    {
        vao.Bind();
        GL.DrawArraysInstanced(topology, draw.FirstIndex, draw.IndexCount, draw.InstanceCount);
        StateCache.Current.RecordDrawSubmission();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        vao.Dispose();
    }
    #endregion
}
