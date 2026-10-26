using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the installed ordinary engine draw helper through the owned fullscreen geometry adapter.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineGraphicsGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The actual engine helper remains query-free on repeated draws and mutated mesh metadata rejects.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledEngineDrawPreservesSubmissionContract(bool incomingSameMesh)
    {
        EnsureContextValid();
        using var native = new NativeMesh();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, EngineFullscreenGeometry.Layout,
            new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport), shader);
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 2, Height = 2, Format = RHIFormat.R32f, MipLevels = 1, Storage = RHIStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        using var incomingVao = GpuVao.Create(); using var incomingIndices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Index, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        incomingVao.Bind(); incomingIndices.UploadIndices(new uint[] { 0, 1, 2 }); incomingVao.BindElementBuffer(incomingIndices);
        int priorVao = incomingSameMesh ? native.Mesh.VaoId : incomingVao.VertexArrayId;
        StateCache.Current.BindVertexArray(priorVao); OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ElementArrayBuffer, incomingIndices.BufferId);
        shader.HzbDepth = source; shader.SrcMip = 0;
        Assert.True(GraphicsCommandContext.TryRun("EngineGeometry", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            native.DuringDraw = () =>
            {
                Assert.Throws<InvalidOperationException>(() => commands.Draw(geometry, new(0, 3)));
                Assert.Throws<InvalidOperationException>(() => commands.SetDynamicState(new()));
                Assert.Throws<InvalidOperationException>(commands.EndPass);
            };
            commands.Draw(geometry, new(0, 3));
#if DEBUG
            long queries = StateCache.Current.BoundaryQueries;
#endif
            commands.Draw(geometry, new(0, 3));
#if DEBUG
            Assert.Equal(queries, StateCache.Current.BoundaryQueries);
#endif
            native.Mesh.IndicesCount = 2;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 3)));
            native.Mesh.IndicesCount = 3;
        }));
        Assert.Equal(2, native.Draws);
        Assert.Equal(priorVao, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(incomingIndices.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
        // The engine cleared its EBO association; a managed DSA rebind must update the same cache owner.
        if (incomingSameMesh)
        {
            OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ElementArrayBuffer, 0);
            native.RebindElements(incomingIndices);
        }
        native.FailDraw = true;
        Assert.Throws<ArithmeticException>(() => GraphicsCommandContext.TryRun("EngineGeometryFailure", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
        }));
        Assert.Equal(priorVao, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(incomingIndices.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        geometry.Dispose(); Assert.Equal(1, native.Deletes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Owned engine mesh cleanup defers live origins and abandons retired names before native engine deletion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void MeshRetirementUsesOriginatingContext(bool retired)
    {
        EnsureContextValid();
        using var native = new NativeMesh();
        var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        object replacement = new();
        EngineFullscreenGeometry? geometry = null;
        try
        {
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
            native.Render.Setup(render => render.DeleteMesh(native.Mesh)).Callback(() =>
            {
                native.Deletes++;
                DeleteNativeMesh(native.Mesh);
            });
            if (retired)
            {
                Rendering.Integration.RenderContextRegistry.Retire(fixture);
                Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            }
            else OpenTK.Windowing.GraphicsLibraryFramework.GLFW.MakeContextCurrent(null);
            geometry.Dispose(); geometry.Dispose();
            Assert.Equal(0, native.Deletes);
            fixture.MakeCurrent();
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.Equal(retired ? 0 : 1, native.Deletes);
            Assert.Equal(retired, GL.IsVertexArray(native.Mesh.VaoId));
            Assert.Equal(retired, GL.IsBuffer(native.Mesh.xyzVboId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.MakeCurrent();
            if (retired)
            {
                Rendering.Integration.RenderContextRegistry.Retire(replacement);
                Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            }
            geometry?.Dispose();
            DeleteNativeMesh(native.Mesh);
            manager.Dispose(); GpuResourceManagerSystem.Shutdown();
        }
    }

    /// <summary>Native attribute mismatches are rejected before the adapter can publish and release the upload.</summary>
    [Fact]
    public void InvalidNativeLayoutDeletesRejectedUpload()
    {
        EnsureContextValid();
        using var native = new NativeMesh();
        StateCache.Current.BindVertexArray(native.Mesh.VaoId); GL.DisableVertexAttribArray(1);
        Assert.ThrowsAny<Exception>(() => EngineFullscreenGeometry.Upload(native.Render.Object, native.Data));
        Assert.Equal(1, native.Deletes); Assert.Equal(0, native.Draws);
    }

    /// <summary>A partially failed engine upload restores the caller's actual vertex and array bindings.</summary>
    [Fact]
    public void FailedUploadRestoresCallerBindings()
    {
        EnsureContextValid();
        using var native = new NativeMesh(); using var previous = GpuVao.Create(); using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        previous.Bind(); OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ArrayBuffer, buffer.BufferId);
        native.Render.Setup(render => render.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
        {
            GL.BindVertexArray(native.Mesh.VaoId); GL.BindBuffer(BufferTarget.ArrayBuffer, native.Mesh.xyzVboId);
            throw new ArithmeticException("Fixture upload failure.");
        });
        Assert.Throws<ArithmeticException>(() => EngineFullscreenGeometry.Upload(native.Render.Object, native.Data));
        Assert.Equal(previous.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(buffer.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        Assert.Equal(0, native.Deletes);
    }
    #endregion

    #region Private
    /// <summary>Releases fixture native names after the test restores its actual namespace.</summary>
    private static void DeleteNativeMesh(VAO mesh)
    {
        StateCache.Current.DeleteVertexArray(mesh.VaoId);
        RHICommandContext.Current.OpenGL.DeleteBuffer(mesh.xyzVboId);
        RHICommandContext.Current.OpenGL.DeleteBuffer(mesh.uvVboId);
        RHICommandContext.Current.OpenGL.DeleteBuffer(mesh.vboIdIndex);
    }

    /// <summary>Owns native engine-layout storage while invoking the installed no-window RenderMesh implementation.</summary>
    private sealed class NativeMesh : IDisposable
    {
        internal readonly Mock<IRenderAPI> Render = new(MockBehavior.Strict);
        internal readonly MeshData Data = new() { xyz = [-1, -1, 0, 3, -1, 0, -1, 3, 0], Uv = [0, 0, 2, 0, 0, 2],
            Indices = [0, 1, 2], VerticesCount = 3, IndicesCount = 3, Rgba = null };
        internal readonly VAO Mesh;
        internal int Draws, Deletes;
        internal bool FailDraw;
        internal Action? DuringDraw;
        private readonly GpuVao vao = GpuVao.Create();
        private readonly GpuVbo positions = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(), uv = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Index, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();

        /// <summary>Constructs exactly the position/UV native layout accepted at the engine upload boundary.</summary>
        internal NativeMesh()
        {
            vao.Bind();
            positions.UploadData(Data.xyz); uv.UploadData(Data.Uv); indices.UploadIndices(new uint[] { 0, 1, 2 });
            OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ArrayBuffer, positions.BufferId);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(0);
            OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ArrayBuffer, uv.BufferId);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(1);
            vao.BindElementBuffer(indices);
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO)); GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.xyzVboId = positions.BufferId; Mesh.uvVboId = uv.BufferId;
            Mesh.vboIdIndex = indices.BufferId; Mesh.IndicesCount = 3; Mesh.drawMode = PrimitiveType.Triangles;
            var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows)); GC.SuppressFinalize(platform);
            Render.Setup(render => render.UploadMesh(It.IsAny<MeshData>())).Returns(Mesh);
            Render.Setup(render => render.RenderMesh(Mesh)).Callback(() =>
            {
                if (FailDraw) { GL.BindVertexArray(Mesh.VaoId); throw new ArithmeticException("Fixture engine failure."); }
                DuringDraw?.Invoke();
                platform.RenderMesh(Mesh); Draws++;
            });
            Render.Setup(render => render.DeleteMesh(Mesh)).Callback(() => Deletes++);
        }

        /// <summary>Releases native fixture storage without invoking an uninitialized engine owner finalizer.</summary>
        public void Dispose() { vao.Dispose(); indices.Dispose(); uv.Dispose(); positions.Dispose(); }

        /// <summary>Reestablishes the owned native VAO association through the managed DSA path.</summary>
        internal void RebindElements(GpuEbo buffer) => vao.BindElementBuffer(buffer);
    }
    #endregion
}
