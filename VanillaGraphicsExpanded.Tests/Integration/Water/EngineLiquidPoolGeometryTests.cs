using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Integration.Water.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Checks installed liquid pool layouts and admitted grouped ranges against real GPU submission.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineLiquidPoolGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A complete admitted group draws through the pool adapter and retires without deleting borrowed engine storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PoolGroupsDrawAndRejectInvalidRanges(bool detachedIndices)
    {
        // Arrange: Admit grouped index ranges over borrowed engine storage and select attached or detached indices.
        // Act: Draw valid groups through the pool adapter, then submit invalid ranges.
        // Assert: Valid geometry renders and invalid groups reject without deleting borrowed storage.
        EnsureContextValid();
        using var storage = new LiquidPoolStorage(detachedIndices); using var geometry = new EngineLiquidPoolGeometry(storage.Pool);
        using var programs = new ComponentShaderPrograms(); var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, EngineLiquidPoolGeometry.Layout,
            new([new(TextureFormat.R32f)]), DynamicPipelineState.Viewport)).RequireResource();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        {
    var commands=RHICommandContext.Current;
    using var scope29=commands.EnterBoundary(new("LiquidPoolFixture",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope29.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.BindResources(shader.CaptureBindings()); commands.DrawIndexedBatch(geometry.Inputs, geometry.CaptureDraws());
#if DEBUG
            long draws = OpenGlStateCache.Current.DrawSubmissions, queries = OpenGlStateCache.Current.BoundaryQueries;
#endif
            commands.DrawIndexedBatch(geometry.Inputs, geometry.CaptureDraws());
#if DEBUG
            Assert.Equal(queries, OpenGlStateCache.Current.BoundaryQueries);
#endif
            storage.Pool.indicesStartsByte[1] = 1;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexedBatch(geometry.Inputs, geometry.CaptureDraws()));
            storage.Pool.indicesStartsByte[1] = 0; storage.Pool.indicesSizes[0] = 6;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexedBatch(geometry.Inputs, geometry.CaptureDraws()));
            storage.Pool.indicesSizes[0] = 3; storage.Location.VerticesEnd = 4;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexedBatch(geometry.Inputs, geometry.CaptureDraws()));
            storage.Location.VerticesEnd = 3;
#if DEBUG
            Assert.Equal(draws + 1, OpenGlStateCache.Current.DrawSubmissions);
#endif
        });
}
        Assert.InRange(target[0].ReadTexel()[0], .1999f, .2001f);
        OpenTK.Graphics.OpenGL4.GL.GetVertexArray(storage.Mesh.VaoId,
            (OpenTK.Graphics.OpenGL4.VertexArrayParameter)OpenTK.Graphics.OpenGL4.All.ElementArrayBufferBinding, out int nativeElements);
        Assert.Equal(detachedIndices ? 0 : storage.Mesh.vboIdIndex, nativeElements);
        // Real observed configuration withdrawal replaces the old test's unsignaled managed-ID edit.
        OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(storage.Mesh.VaoId, 0, storage.Mesh.xyzVboId, IntPtr.Zero, 12);
        Assert.False(geometry.IsPrepared); Assert.Throws<InvalidOperationException>(() => geometry.Inputs);
        geometry.Dispose(); Assert.True(GL.IsVertexArray(storage.Mesh.VaoId));
        Assert.False(geometry.IsPrepared); Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The installed allocator publishes the complete pool contract without reproducing its GL calls in the fixture.</summary>
    [Fact]
    public void InstalledEngineAllocationPublishesLiquidGeometry()
    {
        // Arrange: Capture the installed liquid allocator and its published pool contract.
        // Act: Invoke the installed allocation path through the engine fixture.
        // Assert: The complete liquid geometry is published without reimplementing its GL allocation calls.
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
            OpenGlStateCache.Current.InvalidateAll();
            using (OpenGlStateCache.Current.BindVertexArrayScope(mesh.VaoId))
                foreach (int location in new[] { 3, 5, 6 })
                {
                    GL.GetVertexAttrib(location, VertexAttribParameter.ArrayType, out int storage);
                    Assert.Equal((int)VertexAttribIntegerType.UnsignedInt, storage);
                }
            var pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            pool.VerticesPoolSize = 3; pool.IndicesPoolSize = 3;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(pool, mesh);
            using var geometry = new EngineLiquidPoolGeometry(pool);
            Assert.True(geometry.IsPrepared);
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
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Native signed interpretation does not override the authored unsigned pool declaration.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void NativeIntegerInterpretationDoesNotOverrideDeclaration(int location)
    {
        // Arrange: Author an unsigned pool declaration at the selected vertex attribute location.
        // Act: Read the native integer interpretation through the driver.
        // Assert: Native signed behavior does not rewrite the authored unsigned contract.
        EnsureContextValid(); using var storage = new LiquidPoolStorage();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, storage.Mesh.VaoId);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, location == 3 ? storage.Mesh.flagsVboId : storage.Mesh.customDataIntVboId);
        GL.VertexAttribIPointer(location, 1, VertexAttribIntegerType.Int, location == 3 ? 4 : 8, (IntPtr)(location == 6 ? 4 : 0));
        using var reads = new GeometryDescriptorCapture();
        using var geometry = new EngineLiquidPoolGeometry(storage.Pool);
        Assert.True(geometry.IsPrepared); Assert.Equal(0, reads.Reads);
    }

    /// <summary>Private realization uses authored eight-byte integer strides without scanning the engine array.</summary>
    [Fact]
    public void NativeIntegerStrideDoesNotOverrideDeclaration()
    {
        // Arrange: Declare the engine's eight-byte integer vertex stride for pool realization.
        // Act: Submit private realization without scanning the engine's complete array.
        // Assert: The native attribute layout uses the authored stride and declaration.
        EnsureContextValid(); using var storage = new LiquidPoolStorage();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, storage.Mesh.VaoId);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, storage.Mesh.customDataIntVboId);
        GL.VertexAttribIPointer(5, 1, VertexAttribIntegerType.UnsignedInt, 0, IntPtr.Zero);
        using var reads = new GeometryDescriptorCapture();
        using var geometry = new EngineLiquidPoolGeometry(storage.Pool);
        Assert.True(geometry.IsPrepared); Assert.Equal(0, reads.Reads);
    }
    #endregion
}
