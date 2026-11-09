using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Water.Fixtures;

/// <summary>Owns native storage matching the ordinary liquid terrain pool and its admitted index allocation.</summary>
internal sealed class LiquidPoolStorage : IDisposable
{
    internal readonly MeshDataPool Pool;
    internal readonly VAO Mesh;
    internal readonly ModelDataPoolLocation Location = new() { VerticesStart = 0, VerticesEnd = 3, IndicesStart = 0, IndicesEnd = 3 };
    private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
    private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
    private readonly GpuVbo[] streams = Enumerable.Range(0, 6).Select(_ => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource()).ToArray();

    #region Public API
    /// <summary>Releases only fixture-owned native resources.</summary>
    public void Dispose() { vao.Dispose(); indices.Dispose(); foreach (var stream in streams) stream.Dispose(); }
    #endregion

    #region Internal API
    /// <summary>Configures separate engine streams and the shared interleaved pair of integer attributes.</summary>
    internal LiquidPoolStorage(bool detachedIndices = false)
    {
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId); indices.UploadIndices(new uint[] { 0, 1, 2 }); OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
        streams[0].UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        streams[1].UploadData(new float[6]); streams[2].UploadData(Enumerable.Repeat((byte)255, 12).ToArray());
        streams[3].UploadData(new int[3]); streams[4].UploadData(new float[6]); streams[5].UploadData(new int[6]);
        // Describe the installed AllocateEmptyMesh/AddCustoms contract independently of the adapter.
        RHIVertexAttribute[] nativeAttributes = [
            new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
            new(2, 4, VertexComponentFormat.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
            new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4),
            new(4, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 4, 0, 8),
            new(5, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 5, 0, 8),
            new(6, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 5, 4, 8)];
        foreach (var attribute in nativeAttributes)
        {
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, streams[attribute.Binding].BufferId);
            if (attribute.Interpretation == VertexInterpretation.Integer)
                GL.VertexAttribIPointer(attribute.Location, attribute.Components, VertexAttribIntegerType.UnsignedInt, attribute.Stride, (IntPtr)attribute.Offset);
            else GL.VertexAttribPointer(attribute.Location, attribute.Components, OpenGLVertexValues.FloatingStorage(attribute.Storage),
                attribute.Interpretation == VertexInterpretation.Normalized, attribute.Stride, (IntPtr)attribute.Offset);
            GL.EnableVertexAttribArray(attribute.Location);
        }
        Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO)); GC.SuppressFinalize(Mesh);
        Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = indices.BufferId; Mesh.drawMode = PrimitiveType.Triangles;
        Mesh.xyzVboId = streams[0].BufferId; Mesh.uvVboId = streams[1].BufferId; Mesh.rgbaVboId = streams[2].BufferId;
        Mesh.flagsVboId = streams[3].BufferId; Mesh.customDataFloatVboId = streams[4].BufferId; Mesh.customDataIntVboId = streams[5].BufferId;
        if (detachedIndices) OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, 0);
        Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
        Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3; Pool.indicesGroupsCount = 1;
        Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, Mesh);
        AccessTools.Field(typeof(MeshDataPool), "poolLocations").SetValue(Pool, new List<ModelDataPoolLocation> { Location });
    }

    #endregion
}
