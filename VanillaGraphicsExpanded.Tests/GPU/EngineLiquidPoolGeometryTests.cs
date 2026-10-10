using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks installed liquid pool layouts and admitted grouped ranges against real GPU submission.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineLiquidPoolGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A complete admitted group draws through the pool adapter and retires without deleting borrowed engine storage.</summary>
    [Fact]
    public void PoolGroupsDrawAndRejectInvalidRanges()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage(); using var geometry = new EngineLiquidPoolGeometry(storage.Pool);
        using var programs = new ComponentShaderPrograms(); var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, EngineLiquidPoolGeometry.Layout,
            new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport), shader);
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        Assert.True(GraphicsCommandContext.TryRun("LiquidPoolFixture", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 1));
            long draws = StateCache.Current.DrawSubmissions, queries = StateCache.Current.BoundaryQueries;
            commands.Draw(geometry, new(0, 1)); Assert.Equal(queries, StateCache.Current.BoundaryQueries);
            storage.Pool.indicesStartsByte[1] = 1;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 1)));
            storage.Pool.indicesStartsByte[1] = 0; storage.Pool.indicesSizes[0] = 6;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 1)));
            storage.Pool.indicesSizes[0] = 3; storage.Location.VerticesEnd = 4;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 1)));
            storage.Location.VerticesEnd = 3;
            Assert.Equal(draws + 1, StateCache.Current.DrawSubmissions);
        }));
        Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
        int original = storage.Mesh.customDataIntVboId; storage.Mesh.customDataIntVboId = 0;
        Assert.False(geometry.IsCurrent); Assert.Throws<InvalidOperationException>(() => geometry.Validate(pipeline.Description, new(0, 1)));
        storage.Mesh.customDataIntVboId = original; geometry.Dispose(); Assert.True(GL.IsVertexArray(storage.Mesh.VaoId));
        Assert.False(geometry.IsCurrent); Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The installed allocator publishes the complete pool contract without reproducing its GL calls in the fixture.</summary>
    [Fact]
    public void InstalledEngineAllocationPublishesLiquidGeometry()
    {
        EnsureContextValid();
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        var floats = new CustomMeshDataPartFloat { InterleaveSizes = [2], InterleaveOffsets = [0], InterleaveStride = 8 };
        var integers = new CustomMeshDataPartInt { InterleaveSizes = [1, 1], InterleaveOffsets = [0, 4], InterleaveStride = 8 };
        floats.SetAllocationSize(6); integers.SetAllocationSize(6);
        VAO? mesh = null;
        try
        {
            // Allocate through the real pooled path; UploadMesh has a different signed integer contract.
            mesh = (VAO)platform.AllocateEmptyMesh(36, 0, 24, 12, 12, 12, floats, null, null, integers, EnumDrawMode.Triangles, true);
            GC.SuppressFinalize(mesh);
            StateCache.Current.InvalidateAll();
            using (StateCache.Current.BindVertexArrayScope(mesh.VaoId))
                foreach (int location in new[] { 3, 5, 6 })
                {
                    GL.GetVertexAttrib(location, VertexAttribParameter.ArrayType, out int storage);
                    Assert.Equal((int)VertexAttribIntegerType.UnsignedInt, storage);
                }
            var pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            pool.VerticesPoolSize = 3; pool.IndicesPoolSize = 3;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(pool, mesh);
            using var geometry = new EngineLiquidPoolGeometry(pool);
            Assert.True(geometry.IsCurrent);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            // The uninitialized platform has no game resource owner; retire only the native storage it allocated here.
            if (mesh is not null)
            {
                GL.DeleteVertexArray(mesh.VaoId);
                foreach (int buffer in new[] { mesh.xyzVboId, mesh.uvVboId, mesh.rgbaVboId, mesh.flagsVboId,
                    mesh.customDataFloatVboId, mesh.customDataIntVboId, mesh.vboIdIndex }) GL.DeleteBuffer(buffer);
            }
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Signed upload-style attributes are rejected individually instead of silently replacing the pooled unsigned contract.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void SignedIntegerAttributeRejectsPublication(int location)
    {
        EnsureContextValid(); using var storage = new LiquidPoolStorage();
        StateCache.Current.BindVertexArray(storage.Mesh.VaoId);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, location == 3 ? storage.Mesh.flagsVboId : storage.Mesh.customDataIntVboId);
        GL.VertexAttribIPointer(location, 1, VertexAttribIntegerType.Int, location == 3 ? 4 : 8, (IntPtr)(location == 6 ? 4 : 0));
        Assert.Throws<InvalidOperationException>(() => new EngineLiquidPoolGeometry(storage.Pool));
    }

    /// <summary>Interleaved custom integers require eight-byte stride rather than scalar tight packing.</summary>
    [Fact]
    public void InvalidCustomIntegerStrideRejectsPublication()
    {
        EnsureContextValid(); using var storage = new LiquidPoolStorage();
        StateCache.Current.BindVertexArray(storage.Mesh.VaoId);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, storage.Mesh.customDataIntVboId);
        GL.VertexAttribIPointer(5, 1, VertexAttribIntegerType.UnsignedInt, 0, IntPtr.Zero);
        Assert.Throws<InvalidOperationException>(() => new EngineLiquidPoolGeometry(storage.Pool));
    }
    #endregion
}

/// <summary>Owns native storage matching the ordinary liquid terrain pool and its admitted index allocation.</summary>
internal sealed class LiquidPoolStorage : IDisposable
{
    internal readonly MeshDataPool Pool;
    internal readonly VAO Mesh;
    internal readonly ModelDataPoolLocation Location = new() { VerticesStart = 0, VerticesEnd = 3, IndicesStart = 0, IndicesEnd = 3 };
    private readonly GpuVao vao = GpuVao.Create();
    private readonly GpuEbo indices = GpuEbo.Create();
    private readonly GpuVbo[] streams = Enumerable.Range(0, 6).Select(_ => GpuVbo.Create()).ToArray();

    #region Public API
    /// <summary>Configures separate engine streams and the shared interleaved pair of integer attributes.</summary>
    internal LiquidPoolStorage()
    {
        vao.Bind(); indices.UploadIndices(new uint[] { 0, 1, 2 }); vao.BindElementBuffer(indices);
        streams[0].UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        streams[1].UploadData(new float[6]); streams[2].UploadData(Enumerable.Repeat((byte)255, 12).ToArray());
        streams[3].UploadData(new int[3]); streams[4].UploadData(new float[6]); streams[5].UploadData(new int[6]);
        // Describe the installed AllocateEmptyMesh/AddCustoms contract independently of the adapter.
        VertexAttributeDesc[] nativeAttributes = [
            new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12),
            new(1, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 1, 0, 8),
            new(2, 4, VertexAttribPointerType.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
            new(3, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4),
            new(4, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 4, 0, 8),
            new(5, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 5, 0, 8),
            new(6, 1, VertexAttribPointerType.UnsignedInt, VertexInterpretation.Integer, 5, 4, 8)];
        foreach (var attribute in nativeAttributes)
        {
            StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, streams[attribute.Binding].BufferId);
            if (attribute.Interpretation == VertexInterpretation.Integer)
                GL.VertexAttribIPointer(attribute.Location, attribute.Components, VertexAttribIntegerType.UnsignedInt, attribute.Stride, (IntPtr)attribute.Offset);
            else GL.VertexAttribPointer(attribute.Location, attribute.Components, attribute.Storage,
                attribute.Interpretation == VertexInterpretation.Normalized, attribute.Stride, (IntPtr)attribute.Offset);
            GL.EnableVertexAttribArray(attribute.Location);
        }
        Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO)); GC.SuppressFinalize(Mesh);
        Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = indices.BufferId; Mesh.drawMode = PrimitiveType.Triangles;
        Mesh.xyzVboId = streams[0].BufferId; Mesh.uvVboId = streams[1].BufferId; Mesh.rgbaVboId = streams[2].BufferId;
        Mesh.flagsVboId = streams[3].BufferId; Mesh.customDataFloatVboId = streams[4].BufferId; Mesh.customDataIntVboId = streams[5].BufferId;
        Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
        Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3; Pool.indicesGroupsCount = 1;
        Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, Mesh);
        AccessTools.Field(typeof(MeshDataPool), "poolLocations").SetValue(Pool, new List<ModelDataPoolLocation> { Location });
    }

    /// <summary>Releases only fixture-owned native resources.</summary>
    public void Dispose() { vao.Dispose(); indices.Dispose(); foreach (var stream in streams) stream.Dispose(); }
    #endregion
}
