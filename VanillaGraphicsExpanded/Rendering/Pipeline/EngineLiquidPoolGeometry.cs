using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Borrows the installed engine's ordinary liquid pools and validates their culled indexed groups.</summary>
internal sealed class EngineLiquidPoolGeometry : GraphicsGeometry
{
    private static readonly AccessTools.FieldRef<MeshDataPool, MeshRef> ReadMesh = AccessTools.FieldRefAccess<MeshDataPool, MeshRef>("modelRef");
    private static readonly AccessTools.FieldRef<MeshDataPool, List<ModelDataPoolLocation>> ReadLocations =
        AccessTools.FieldRefAccess<MeshDataPool, List<ModelDataPoolLocation>>("poolLocations");
    // AllocateEmptyMesh/AddCustoms expose packed flags and custom integers as unsigned integer attributes.
    internal static readonly VertexLayoutDesc Layout = new([
        new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 1, 0, 8),
        new(2, 4, VertexAttribPointerType.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
        new(3, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4),
        new(4, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 4, 0, 8),
        new(5, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 5, 0, 8),
        new(6, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 5, 4, 8)]);
    private readonly MeshDataPool pool;
    private readonly VAO mesh;
    private readonly int vao, elements, vertices, indices;
    private readonly int[] streams;
    private bool disposed;

    #region Public API
    /// <summary>Checks native stream metadata once before any submission boundary; engine storage remains borrowed.</summary>
    internal EngineLiquidPoolGeometry(MeshDataPool pool)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        this.pool = pool;
        mesh = ReadMesh(pool) as VAO ?? throw new NotSupportedException("Unsupported liquid pool mesh.");
        vao = mesh.VaoId; elements = mesh.vboIdIndex;
        vertices = pool.VerticesPoolSize; indices = pool.IndicesPoolSize;
        streams = [mesh.xyzVboId, mesh.uvVboId, mesh.rgbaVboId, mesh.flagsVboId, mesh.customDataFloatVboId, mesh.customDataIntVboId];
        if (vao == 0 || elements == 0 || vertices <= 0 || indices <= 0 || streams.Any(id => id == 0))
            throw new InvalidOperationException("Liquid pool has incomplete ordinary vertex storage.");
        using var vertexScope = StateCache.Current.BindVertexArrayScope(vao);
        using var arrayScope = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        using var errors = new GlDebug.ErrorScope("Liquid pool geometry publication");
        foreach (var attribute in Layout.Attributes) ValidateAttribute(attribute);
        foreach (var attribute in Layout.Attributes.GroupBy(a => a.Binding).Select(g => g.First()))
            ValidateBuffer(BufferTarget.ArrayBuffer, streams[attribute.Binding], checked(vertices * attribute.Stride));
        using var elementScope = StateCache.Current.BindBufferScope(BufferTarget.ElementArrayBuffer, elements);
        ValidateBuffer(BufferTarget.ElementArrayBuffer, elements, checked(indices * sizeof(uint)));
    }

    /// <summary>Detects engine retirement and replacement without querying GPU state.</summary>
    internal bool IsCurrent => !disposed && ReferenceEquals(ReadMesh(pool), mesh) && !mesh.Disposed && mesh.Initialized
        && mesh.VaoId == vao && mesh.vboIdIndex == elements && pool.VerticesPoolSize == vertices && pool.IndicesPoolSize == indices
        && mesh.xyzVboId == streams[0] && mesh.uvVboId == streams[1] && mesh.rgbaVboId == streams[2]
        && mesh.flagsVboId == streams[3] && mesh.customDataFloatVboId == streams[4] && mesh.customDataIntVboId == streams[5];

    /// <inheritdoc />
    internal override void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw)
    {
        if (!IsCurrent) throw new InvalidOperationException("Liquid pool storage changed after preparation.");
        if (pipeline.VertexLayout != Layout || pipeline.Assembly.Topology != PrimitiveType.Triangles
            || mesh.drawMode != PrimitiveType.Triangles || pipeline.Assembly.Restart || pipeline.Assembly.FixedIndexRestart)
            throw new InvalidOperationException("Liquid pools require the ordinary indexed triangle layout.");
        int count = pool.indicesGroupsCount;
        if (IntPtr.Size != 8 || draw != new GraphicsDraw(0, count) || count <= 0
            || count > pool.indicesSizes.Length || count > pool.indicesStartsByte.Length / 2)
            throw new ArgumentOutOfRangeException(nameof(draw));
        var locations = ReadLocations(pool);
        // Pool insertion offsets indices into each admitted vertex allocation. Culling may select only
        // complete admitted allocations; it cannot invent a range or use the SSBO shared index stream.
        int allocation = 0;
        for (int group = 0; group < count; group++)
        {
            int offset = pool.indicesStartsByte[group * 2], size = pool.indicesSizes[group];
            // Engine culling emits allocations in pool-list order. Walk that order once so
            // validating many visible chunks remains linear in the pool's allocation count.
            while (allocation < locations.Count && locations[allocation].IndicesStart != offset / sizeof(uint)) allocation++;
            if (offset < 0 || offset % sizeof(uint) != 0 || pool.indicesStartsByte[group * 2 + 1] != 0
                || size <= 0 || size % 3 != 0 || offset / sizeof(uint) > indices - size
                || allocation >= locations.Count)
                throw new InvalidOperationException("Culled liquid group exceeds its admitted pool allocation.");
            var location = locations[allocation++];
            if (location.IndicesEnd - location.IndicesStart != size || location.VerticesStart < 0
                || location.VerticesEnd <= location.VerticesStart || location.VerticesEnd > vertices)
                throw new InvalidOperationException("Culled liquid group exceeds its admitted vertex allocation.");
        }
    }

    /// <inheritdoc />
    internal override void Submit(GraphicsDraw draw)
    {
        using var vertexScope = StateCache.Current.BindVertexArrayScope(vao);
        using var elementScope = StateCache.Current.BindBufferScope(BufferTarget.ElementArrayBuffer, elements);
        GL.MultiDrawElements(PrimitiveType.Triangles, pool.indicesSizes, DrawElementsType.UnsignedInt,
            pool.indicesStartsByte, pool.indicesGroupsCount);
        StateCache.Current.RecordDrawSubmission();
    }

    /// <summary>Retires this adapter without deleting engine-owned meshes or buffers.</summary>
    public override void Dispose() => disposed = true;
    #endregion

    #region Private
    /// <summary>Checks the installed engine attribute interpretation rather than guessing from the shader inputs.</summary>
    private void ValidateAttribute(VertexAttributeDesc attribute)
    {
        /// <summary>Reads one property of the currently inspected native attribute.</summary>
        int Read(VertexAttribParameter parameter) { GL.GetVertexAttrib(attribute.Location, parameter, out int value); return value; }
        int stride = Read(VertexAttribParameter.ArrayStride);
        if (stride == 0) stride = attribute.Components * (attribute.Storage == VertexAttribPointerType.UnsignedByte ? 1 : 4);
        IntPtr offset = IntPtr.Zero;
        GL.GetVertexAttribPointer(attribute.Location, VertexAttribPointerParameter.ArrayPointer, ref offset);
        if (Read(VertexAttribParameter.ArrayEnabled) != 1 || Read(VertexAttribParameter.ArraySize) != attribute.Components
            || Read(VertexAttribParameter.ArrayType) != (int)attribute.Storage || stride != attribute.Stride
            || Read(VertexAttribParameter.ArrayNormalized) != (attribute.Interpretation == VertexInterpretation.Normalized ? 1 : 0)
            || Read(VertexAttribParameter.VertexAttribArrayInteger) != (attribute.Interpretation == VertexInterpretation.Integer ? 1 : 0)
            || Read(VertexAttribParameter.VertexAttribArrayDivisor) != 0
            || Read((VertexAttribParameter)All.VertexAttribArrayBufferBinding) != streams[attribute.Binding]
            || offset != (IntPtr)attribute.Offset)
            throw new InvalidOperationException("Unsupported liquid pool attribute representation.");
    }

    /// <summary>Checks capacity at publication while keeping ordinary draws query-free.</summary>
    private static void ValidateBuffer(BufferTarget target, int id, int required)
    {
        StateCache.Current.BindBuffer(target, id);
        GL.GetBufferParameter(target, BufferParameterName.BufferSize, out int size);
        if (size < required) throw new InvalidOperationException("Liquid pool buffer is smaller than its declared capacity.");
    }
    #endregion
}
