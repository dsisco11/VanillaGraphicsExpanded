using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    internal static readonly RHIVertexLayout Layout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
        new(2, 4, VertexComponentFormat.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
        new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4),
        new(4, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 4, 0, 8),
        new(5, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 5, 0, 8),
        new(6, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 5, 4, 8)]);
    private readonly MeshDataPool pool;
    private readonly VAO mesh;
    private readonly EngineGeometryInputs inputs;
    private RHIDrawIndexed[] draws = [];
    private readonly int vao, elements, vertices, indices;
    private readonly int[] streams;
    private bool disposed;

    #region Public API
    /// <summary>Admits the producer's layout and original allocations before submission; engine storage remains borrowed.</summary>
    internal EngineLiquidPoolGeometry(MeshDataPool pool)
    {
        RHICommandContext.RequirePreparationAdmission();
        this.pool = pool;
        mesh = ReadMesh(pool) as VAO ?? throw new NotSupportedException("Unsupported liquid pool mesh.");
        vao = mesh.VaoId; elements = mesh.vboIdIndex;
        vertices = pool.VerticesPoolSize; indices = pool.IndicesPoolSize;
        streams = [mesh.xyzVboId, mesh.uvVboId, mesh.rgbaVboId, mesh.flagsVboId, mesh.customDataFloatVboId, mesh.customDataIntVboId];
        if (vao == 0 || elements == 0 || vertices <= 0 || indices <= 0 || streams.Any(id => id == 0))
            throw new InvalidOperationException("Liquid pool has incomplete ordinary vertex storage.");
        inputs = new(mesh, Layout, new Dictionary<int, int>
            { [0] = streams[0], [1] = streams[1], [2] = streams[2], [3] = streams[3], [4] = streams[4], [5] = streams[5] }, vertices, indices);
    }

    /// <summary>Detects engine retirement and replacement without querying GPU state.</summary>
    internal bool IsCurrent => !disposed && ReferenceEquals(ReadMesh(pool), mesh) && !mesh.Disposed && mesh.Initialized
        && mesh.VaoId == vao && mesh.vboIdIndex == elements && pool.VerticesPoolSize == vertices && pool.IndicesPoolSize == indices
        && mesh.xyzVboId == streams[0] && mesh.uvVboId == streams[1] && mesh.rgbaVboId == streams[2]
        && mesh.flagsVboId == streams[3] && mesh.customDataFloatVboId == streams[4] && mesh.customDataIntVboId == streams[5]
        && GpuResourceManager.IsEngineGeometryConfigurationCurrent(inputs.Inputs.ImportedArray!)
        && inputs.Inputs.Streams.All(stream => stream.Range.Buffer.TryGetMetadata(out var data) && data.Capacity >= stream.Range.Offset + stream.Range.Length);

    /// <summary>Lends original engine storage without a native draw executor.</summary>
    internal override RHIGeometryInputs Inputs
    {
        get
        {
            if (!IsCurrent) throw new InvalidOperationException("Liquid pool storage changed after preparation.");
            return inputs.Inputs;
        }
    }

    /// <summary>Validates producer-owned culling allocations and freezes the current noninstanced indexed intervals.</summary>
    internal ReadOnlySpan<RHIDrawIndexed> CaptureDraws()
    {
        if (!IsCurrent) throw new InvalidOperationException("Liquid pool storage changed after preparation.");
        int count = pool.indicesGroupsCount;
        if (IntPtr.Size != 8 || count <= 0
            || count > pool.indicesSizes.Length || count > pool.indicesStartsByte.Length / 2)
            throw new InvalidOperationException("Liquid pool has no complete culled index intervals.");
        if (draws.Length < count) Array.Resize(ref draws, count);
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
            draws[group] = new(offset / sizeof(uint), size);
        }
        return draws.AsSpan(0, count);
    }

    /// <summary>Retires this adapter without deleting engine-owned meshes or buffers.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        inputs.Dispose();
    }
    #endregion

}
