using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns explicitly packed liquid vertices and indices for production-shader raster tests.</summary>
internal sealed class LiquidMeshFixture : IDisposable
{
    private readonly GpuResourceCollection resources = new();
    private readonly RHIGeometryBorrow borrow = new();
    private readonly GpuVbo vertices;
    private readonly GpuVbo flags;
    private readonly GpuEbo indices;
    private readonly int vertexCount;

    #region Public API
    /// <summary>Declares the independent fixture's float and unsigned packed-flag streams.</summary>
    internal static RHIVertexLayout Layout { get; } = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 44),
        new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 12, 44),
        new(2, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 20, 44),
        new(4, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 36, 44),
        new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 1, 0, 12),
        new(5, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 1, 4, 12),
        new(6, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 1, 8, 12)]);

    /// <summary>Gets the current frozen stream and index intervals without transferring ownership.</summary>
    internal RHIGeometryInputs Inputs { get; private set; } = null!;
    /// <summary>Gets the producer-authored topology length.</summary>
    internal int IndexCount { get; private set; }

    /// <summary>Uploads independently authored vertex bytes and establishes explicit index ownership.</summary>
    internal LiquidMeshFixture(float[] vertexData, int[] packedFlags, uint[] topology)
    {
        if (vertexData.Length % 11 != 0 || packedFlags.Length != vertexData.Length / 11 * 3)
            throw new ArgumentException("Liquid fixture streams must describe the same complete vertices.");
        vertexCount = vertexData.Length / 11;
        var context = RHICommandContext.Current;
        try
        {
            vertices = resources.Own(context.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = checked(vertexData.Length * sizeof(float)),
                Usage = BufferUsage.StaticDraw, Label = "Tests.Liquid.Vertices" }).RequireResource());
            flags = resources.Own(context.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
                Mode = ResourceStorageMode.Mutable, Capacity = checked(packedFlags.Length * sizeof(int)),
                Usage = BufferUsage.StaticDraw, Label = "Tests.Liquid.Flags" }).RequireResource());
            indices = resources.Own(context.CreateIndexBuffer(new() { Role = BufferRole.Index,
                Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw,
                Label = "Tests.Liquid.Indices" }).RequireResource());
            UpdateVertices(vertexData, packedFlags);
            SetTopology(topology);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Replaces fixed-layout vertex inputs while preserving the fixture's explicit geometry ownership.</summary>
    internal void UpdateVertices(float[] vertexData, int[] packedFlags)
    {
        if (vertexData.Length != vertexCount * 11 || packedFlags.Length != vertexCount * 3)
            throw new ArgumentException("Updated liquid streams must retain the declared vertex extent.");
        vertices.UploadData(vertexData);
        flags.UploadData(packedFlags);
    }

    /// <summary>Changes the authored triangles and publishes matching explicit bounds for the next draw.</summary>
    internal void SetTopology(uint[] topology)
    {
        if (topology.Any(index => index >= vertexCount)) throw new ArgumentOutOfRangeException(nameof(topology));
        indices.UploadIndices(topology);
        IndexCount = topology.Length;
        Inputs = new(Layout,
            [new(0, new(vertices, 0, vertices.SizeBytes), 44), new(1, new(flags, 0, flags.SizeBytes), 12)],
            new(new(indices, 0, indices.SizeBytes), IndexFormat.UnsignedInt), 0, borrow);
    }
    /// <summary>Expires backend geometry before retiring its independently owned buffer storage.</summary>
    public void Dispose()
    {
        borrow.Dispose();
        resources.Dispose();
    }
    #endregion
}
