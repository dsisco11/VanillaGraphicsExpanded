using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns an ordinary engine-uploaded position/UV mesh with a verified native fullscreen representation.</summary>
internal sealed class EngineFullscreenGeometry : GraphicsGeometry
{
    internal static VertexLayoutDesc Layout { get; } = new([
        new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 1, 0, 8)]);
    private readonly IRenderAPI render;
    private readonly VAO mesh;
    private readonly int vao, positions, uv, elements, count;
    private bool disposed;

    #region Public API
    /// <summary>Uploads only the owned fullscreen stream contract and verifies native metadata before publication.</summary>
    internal static EngineFullscreenGeometry Upload(IRenderAPI render, MeshData data)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(data);
        if (data.VerticesCount <= 0 || data.IndicesCount <= 0 || data.xyz is null || data.Uv is null
            || data.Indices is null || data.Rgba is not null || data.xyz.Length < data.VerticesCount * 3L
            || data.Uv.Length < data.VerticesCount * 2L || data.Indices.Length < data.IndicesCount
            || data.Indices.Take(data.IndicesCount).Any(i => i < 0 || i >= data.VerticesCount))
            throw new ArgumentException("Expected indexed position/UV geometry without a color stream.", nameof(data));
        StateCache.Current.RequireOutsideEngineBoundary();
        // Upload may bind arrays through the engine; existing scopes preserve the caller's bindings.
        using var vertexScope = StateCache.Current.BindVertexArrayScope(0);
        using var arrayScope = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        MeshRef? uploaded = null;
        try
        {
            uploaded = render.UploadMesh(data);
            if (uploaded is not VAO native) throw new InvalidOperationException("Unsupported engine mesh representation.");
            return new(render, native, data.VerticesCount, data.IndicesCount);
        }
        catch (Exception failure)
        {
            try { if (uploaded is not null) render.DeleteMesh(uploaded); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        finally
        {
            // Upload is a cold external operation; query subsequent bindings only when needed by restoration.
            StateCache.Current.ForgetEngineMeshUpload((uploaded as VAO)?.VaoId);
        }
    }

    /// <inheritdoc />
    internal override void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (mesh.Disposed || !mesh.Initialized || mesh.VaoId != vao || mesh.xyzVboId != positions
            || mesh.uvVboId != uv || mesh.vboIdIndex != elements || mesh.IndicesCount != count
            || mesh.drawMode != PrimitiveType.Triangles || !HasOnlyPositionUvStreams(mesh))
            throw new InvalidOperationException("Owned engine mesh was retired or changed.");
        if (pipeline.VertexLayout != Layout || pipeline.Assembly.Topology != PrimitiveType.Triangles
            || pipeline.Assembly.Restart || pipeline.Assembly.FixedIndexRestart)
            throw new InvalidOperationException("Pipeline does not match ordinary fullscreen engine geometry.");
        if (draw != new GraphicsDraw(0, count))
            throw new ArgumentOutOfRangeException(nameof(draw), "Ordinary engine geometry draws its complete noninstanced index range.");
    }

    /// <inheritdoc />
    internal override void Submit(GraphicsDraw draw)
    {
        bool completed = false;
        try
        {
            render.RenderMesh(mesh);
            completed = true;
            StateCache.Current.RecordDrawSubmission();
        }
        finally { StateCache.Current.ObserveEngineMeshDraw(vao, completed); }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        render.DeleteMesh(mesh);
    }
    #endregion

    #region Private
    /// <summary>Checks the installed VAO once; native queries never occur on its draw path.</summary>
    private EngineFullscreenGeometry(IRenderAPI render, VAO mesh, int vertices, int count)
    {
        this.render = render; this.mesh = mesh; this.count = count;
        vao = mesh.VaoId; positions = mesh.xyzVboId; uv = mesh.uvVboId; elements = mesh.vboIdIndex;
        if (vao == 0 || positions == 0 || uv == 0 || elements == 0 || mesh.IndicesCount != count
            || mesh.drawMode != PrimitiveType.Triangles || !HasOnlyPositionUvStreams(mesh))
            throw new InvalidOperationException("Unsupported uploaded engine geometry.");
        using var errors = new GlDebug.ErrorScope("Engine geometry publication");
        // Invalidate upload knowledge before using cache-owned scopes to inspect the resulting VAO.
        StateCache.Current.ForgetEngineMeshUpload(vao);
        StateCache.Current.BindVertexArray(vao);
        CheckAttribute(0, 3, 12, positions);
        CheckAttribute(1, 2, 8, uv);
        CheckBuffer(BufferTarget.ArrayBuffer, positions, checked(vertices * 12));
        CheckBuffer(BufferTarget.ArrayBuffer, uv, checked(vertices * 8));
        CheckBuffer(BufferTarget.ElementArrayBuffer, elements, checked(count * sizeof(uint)));
    }

    /// <summary>Requires enabled floating attributes, zero offsets, and no instancing or integer reinterpretation.</summary>
    private static void CheckAttribute(int location, int components, int stride, int buffer)
    {
        // The engine uses legacy pointer stride zero to mean tightly packed, unlike separated bindings.
        int nativeStride = GetAttribute(location, VertexAttribParameter.ArrayStride);
        if (GetAttribute(location, VertexAttribParameter.ArrayEnabled) != 1
            || GetAttribute(location, VertexAttribParameter.ArraySize) != components
            || GetAttribute(location, VertexAttribParameter.ArrayType) != (int)VertexAttribPointerType.Float
            || nativeStride != 0 && nativeStride != stride
            || GetAttribute(location, VertexAttribParameter.ArrayNormalized) != 0
            || GetAttribute(location, VertexAttribParameter.VertexAttribArrayInteger) != 0
            || GetAttribute(location, VertexAttribParameter.VertexAttribArrayDivisor) != 0)
            throw new InvalidOperationException("Unsupported engine vertex attribute representation.");
        GL.GetVertexAttrib(location, (VertexAttribParameter)All.VertexAttribArrayBufferBinding, out int actual);
        IntPtr pointer = IntPtr.Zero;
        GL.GetVertexAttribPointer(location, VertexAttribPointerParameter.ArrayPointer, ref pointer);
        if (actual != buffer || pointer != IntPtr.Zero)
            throw new InvalidOperationException("Engine vertex attribute points outside its declared stream.");
    }

    /// <summary>Reads one native attribute property during cold publication.</summary>
    private static int GetAttribute(int location, VertexAttribParameter parameter)
    {
        GL.GetVertexAttrib(location, parameter, out int value);
        return value;
    }

    /// <summary>Rejects pooled or extended streams instead of assigning them fullscreen semantics.</summary>
    private static bool HasOnlyPositionUvStreams(VAO mesh) => mesh.normalsVboId == 0 && mesh.rgbaVboId == 0
        && mesh.flagsVboId == 0 && mesh.customDataFloatVboId == 0 && mesh.customDataIntVboId == 0
        && mesh.customDataShortVboId == 0 && mesh.customDataByteVboId == 0;

    /// <summary>Checks allocated byte extent through the existing cache-owned binding API.</summary>
    private static void CheckBuffer(BufferTarget target, int buffer, int required)
    {
        StateCache.Current.BindBuffer(target, buffer);
        GL.GetBufferParameter(target, BufferParameterName.BufferSize, out int bytes);
        if (bytes < required) throw new InvalidOperationException("Engine geometry buffer is smaller than its upload.");
    }
    #endregion
}
