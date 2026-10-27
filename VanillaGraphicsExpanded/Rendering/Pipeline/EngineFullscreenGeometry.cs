using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
    internal static RHIVertexLayout Layout { get; } = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8)]);
    private readonly IRenderAPI render;
    private readonly (nint Handle, long Generation) context;
    private readonly GpuResourceManager? deletionManager;
    private readonly VAO mesh;
    private readonly EngineGeometryInputs inputs;
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
        var context = RHICommandContext.Current.OpenGL.NativeOrigin;
        var deletionManager = GpuResourceManagerSystem.CurrentManager;
        // Upload may bind arrays through the engine; existing scopes preserve the caller's bindings.
        using var vertexScope = StateCache.Current.BindVertexArrayScope(0);
        using var arrayScope = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        MeshRef? uploaded = null;
        try
        {
            uploaded = render.UploadMesh(data);
            if (uploaded is not VAO native) throw new InvalidOperationException("Unsupported engine mesh representation.");
            return new(render, native, data.VerticesCount, data.IndicesCount, context, deletionManager);
        }
        catch (Exception failure)
        {
            try { if (uploaded is not null) RetireMesh(render, uploaded, context, deletionManager); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        finally
        {
            // Upload is a cold external operation; query subsequent bindings only when needed by restoration.
            StateCache.Current.ForgetEngineMeshUpload((uploaded as VAO)?.VaoId);
        }
    }

    /// <summary>Lends certified geometry without granting native draw or ownership operations.</summary>
    internal override RHIGeometryInputs Inputs
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (mesh.Disposed || !mesh.Initialized || mesh.VaoId != vao || mesh.xyzVboId != positions
                || mesh.uvVboId != uv || mesh.vboIdIndex != elements || mesh.IndicesCount != count
                || mesh.drawMode != PrimitiveType.Triangles || !HasOnlyPositionUvStreams(mesh))
                throw new InvalidOperationException("Owned engine mesh was retired or changed.");
            return inputs.Inputs;
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        inputs.Dispose();
        RetireMesh(render, mesh, context, deletionManager);
    }
    #endregion

    #region Private
    /// <summary>Checks the installed VAO once; native queries never occur on its draw path.</summary>
    private EngineFullscreenGeometry(IRenderAPI render, VAO mesh, int vertices, int count,
        (nint Handle, long Generation) context, GpuResourceManager? deletionManager)
    {
        this.render = render; this.mesh = mesh; this.count = count;
        this.context = context; this.deletionManager = deletionManager;
        if (context != RHICommandContext.Current.OpenGL.NativeOrigin)
            throw new InvalidOperationException("Engine mesh publication requires its uploading context.");
        vao = mesh.VaoId; positions = mesh.xyzVboId; uv = mesh.uvVboId; elements = mesh.vboIdIndex;
        if (vao == 0 || positions == 0 || uv == 0 || elements == 0 || mesh.IndicesCount != count
            || mesh.drawMode != PrimitiveType.Triangles || !HasOnlyPositionUvStreams(mesh))
            throw new InvalidOperationException("Unsupported uploaded engine geometry.");
        StateCache.Current.ForgetEngineMeshUpload(vao);
        inputs = new(mesh, Layout, new Dictionary<int, int> { [0] = positions, [1] = uv }, vertices, count);
    }

    /// <summary>Retires only the adapter's owned mesh, preserving engine cleanup on its originating context.</summary>
    private static void RetireMesh(IRenderAPI render, MeshRef mesh,
        (nint Handle, long Generation) origin, GpuResourceManager? manager)
    {
        // The adapter owns terminal cleanup. Suppress the engine leak diagnostic when
        // ownership moves to deferred cleanup or the retired context reclaims its names.
        GC.SuppressFinalize(mesh);
        if (GpuResource.IsCurrentResourceContext(origin))
        {
            render.DeleteMesh(mesh);
            return;
        }
        if (Integration.RenderContextRegistry.IsAlive(origin))
            manager?.EnqueueContextCleanup(() => render.DeleteMesh(mesh), origin);
    }

    /// <summary>Rejects pooled or extended streams instead of assigning them fullscreen semantics.</summary>
    private static bool HasOnlyPositionUvStreams(VAO mesh) => mesh.normalsVboId == 0 && mesh.rgbaVboId == 0
        && mesh.flagsVboId == 0 && mesh.customDataFloatVboId == 0 && mesh.customDataIntVboId == 0
        && mesh.customDataShortVboId == 0 && mesh.customDataByteVboId == 0;

    #endregion
}
