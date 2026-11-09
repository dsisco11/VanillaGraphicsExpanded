using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns native position/UV upload storage for engine-service fixtures.</summary>
internal sealed class RuntimeNativeMesh : IDisposable
{
    private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
    private readonly GpuVbo positions = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(), uv = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
    private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
    internal VAO Mesh { get; }

    #region Public API
    /// <summary>Releases the test-owned native buffers and vertex array.</summary>
    public void Dispose() { vao.Dispose(); indices.Dispose(); uv.Dispose(); positions.Dispose(); }
    #endregion

    #region Internal API
    /// <summary>Publishes the same ordinary fullscreen layout as the installed engine uploader.</summary>
    internal RuntimeNativeMesh(MeshData data)
    {
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId); positions.UploadData(data.xyz); uv.UploadData(data.Uv);
        indices.UploadIndices(data.Indices.Take(data.IndicesCount).Select(value => checked((uint)value)).ToArray());
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, positions.BufferId);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(0);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, uv.BufferId);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(1);
        OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
        Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO)); GC.SuppressFinalize(Mesh);
        Mesh.VaoId = vao.VertexArrayId; Mesh.xyzVboId = positions.BufferId; Mesh.uvVboId = uv.BufferId;
        Mesh.vboIdIndex = indices.BufferId; Mesh.IndicesCount = data.IndicesCount; Mesh.drawMode = PrimitiveType.Triangles;
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, 0);
    }

    /// <summary>Draws through the installed ordinary engine mesh helper without a game window.</summary>
    internal void Draw()
    {
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform); platform.RenderMesh(Mesh);
    }

    #endregion
}
