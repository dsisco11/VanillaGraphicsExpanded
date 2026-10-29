using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns an engine-uploaded position/UV mesh and supplies its authored fullscreen stream contract.</summary>
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
    /// <summary>Uploads owned fullscreen streams and admits their original allocations before publication.</summary>
    internal static EngineFullscreenGeometry Upload(IRenderAPI render, MeshData data)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(data);
        if (data.VerticesCount <= 0 || data.IndicesCount <= 0 || data.xyz is null || data.Uv is null
            || data.Indices is null || data.Rgba is not null || data.xyz.Length < data.VerticesCount * 3L
            || data.Uv.Length < data.VerticesCount * 2L || data.Indices.Length < data.IndicesCount
            || data.Indices.Take(data.IndicesCount).Any(i => i < 0 || i >= data.VerticesCount))
            throw new ArgumentException("Expected indexed position/UV geometry without a color stream.", nameof(data));
        RHICommandContext.RequirePreparationAdmission();
        var execution = RHICommandContext.Current;
        var context = GpuResource.CaptureCreationContext(GpuResourceKind.VertexArray);
        var deletionManager = GpuResourceManagerSystem.CurrentManager;
        // Upload may bind arrays through the engine; existing scopes preserve the caller's bindings.
        MeshRef? uploaded = null;
        try
        {
            uploaded = OpenGLRHIContext.UploadEngineMesh(render, data);
            if (uploaded is not VAO native) throw new InvalidOperationException("Unsupported engine mesh representation.");
            var geometry = new EngineFullscreenGeometry(render, native, data.VerticesCount, data.IndicesCount, context, deletionManager, execution);
            return geometry;
        }
        catch (Exception failure)
        {
            try { if (uploaded is not null) RetireMesh(render, uploaded, context, deletionManager); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
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
    /// <summary>Admits original engine allocations from the supplied layout without scanning native attributes.</summary>
    private EngineFullscreenGeometry(IRenderAPI render, VAO mesh, int vertices, int count,
        (nint Handle, long Generation) context, GpuResourceManager? deletionManager, RHICommandContext execution)
    {
        this.render = render; this.mesh = mesh; this.count = count;
        this.context = context; this.deletionManager = deletionManager;
        execution.RequireContext();
        vao = mesh.VaoId; positions = mesh.xyzVboId; uv = mesh.uvVboId; elements = mesh.vboIdIndex;
        if (vao == 0 || positions == 0 || uv == 0 || elements == 0 || mesh.IndicesCount != count
            || mesh.drawMode != PrimitiveType.Triangles || !HasOnlyPositionUvStreams(mesh))
            throw new InvalidOperationException("Unsupported uploaded engine geometry.");
        inputs = new(mesh, Layout, new Dictionary<int, int> { [0] = positions, [1] = uv }, vertices, count);
    }

    /// <summary>Retires only the adapter's owned mesh, preserving engine cleanup on its originating context.</summary>
    private static void RetireMesh(IRenderAPI render, MeshRef mesh,
        (nint Handle, long Generation) origin, GpuResourceManager? manager)
    {
        // The adapter owns terminal cleanup. Suppress the engine leak diagnostic when
        // ownership moves to deferred cleanup or the retired context reclaims its names.
        GC.SuppressFinalize(mesh);
        if (VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
        {
            OpenGLRHIContext.DeleteEngineMesh(render, mesh);
            return;
        }
        if (Integration.RenderContextRegistry.IsAlive(origin))
            manager?.EnqueueContextCleanup(() => OpenGLRHIContext.DeleteEngineMesh(render, mesh), origin);
    }

    /// <summary>Rejects pooled or extended streams instead of assigning them fullscreen semantics.</summary>
    private static bool HasOnlyPositionUvStreams(VAO mesh) => mesh.normalsVboId == 0 && mesh.rgbaVboId == 0
        && mesh.flagsVboId == 0 && mesh.customDataFloatVboId == 0 && mesh.customDataIntVboId == 0
        && mesh.customDataShortVboId == 0 && mesh.customDataByteVboId == 0;

    #endregion
}
