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
    /// <summary>Manager certification reuses completed facts and rejects changed or retired original registrations.</summary>
    [Fact]
    public void ManagerCertificationRejectsChangedAndRetiredArrays()
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var array = NativeResourceSetup.CreateVertexArray();
            using var elements = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
                { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 4, Usage = BufferUsage.StaticDraw }).RequireResource();
            // This is externally configured fixture state; only the manager may acquire and certify it.
            OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(array.VertexArrayId, elements.BufferId);
            var layout = new RHIVertexLayout([]);
            var indices = new RHIIndexStream(new(elements, 0, 4));
            var receipt = GpuResourceManager.AcquireEngineGeometryConfiguration(array, layout, [], indices);
            Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            using (var unrelated = NativeResourceSetup.CreateVertexArray())
            {
                var mutation = OpenGlStateCache.BeginNativeVertexMetadata(unrelated.VertexArrayId, named: true, elementBinding: false);
                mutation.Complete(true);
                Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            }

            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuVertexArrayMetadata>>(manager.GetResourceRegistration(array));
            manager.PublishMetadata(entry, new GpuVertexArrayMetadata(), GpuMetadataChange.Metadata);
            Assert.False(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            receipt = GpuResourceManager.AcquireEngineGeometryConfiguration(array, layout, [], indices);
            Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            array.Dispose();
            Assert.False(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Changing a certified original index association rejects both its receipt and a mismatched reacquisition.</summary>
    [Fact]
    public void ChangedImportedIndexAssociationRejectsReuse()
    {
        EnsureContextValid();
        using var native = new NativeMesh();
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        var packet = geometry.Inputs;
        var receipt = packet.ImportedArray!;
        Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
        using var replacement = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
            { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 12, Usage = BufferUsage.StaticDraw }).RequireResource();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, native.Mesh.VaoId);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ElementArrayBuffer, replacement.BufferId);
        Assert.False(GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
        Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireEngineGeometryConfiguration(
            receipt.Array, packet.Layout, packet.Streams, packet.Indices!.Value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

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
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.R32f)]), DynamicPipelineState.Viewport)).RequireResource();
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        var certification = Assert.IsType<RHIGeometryImport>(geometry.Inputs.ImportedArray);
        Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(certification));
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        using var incomingVao = NativeResourceSetup.CreateVertexArray(); using var incomingIndices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, incomingVao.VertexArrayId); incomingIndices.UploadIndices(new uint[] { 0, 1, 2 }); OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(incomingVao.VertexArrayId, incomingIndices.BufferId);
        int priorVao = incomingSameMesh ? native.Mesh.VaoId : incomingVao.VertexArrayId;
        int priorElements = incomingSameMesh ? native.Mesh.vboIdIndex : incomingIndices.BufferId;
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, priorVao);
        // The imported array already has its certified EBO. Only configure the independent sentinel;
        // changing an imported array after certification must invalidate it, even for an unknown equal bind.
        if (!incomingSameMesh) OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ElementArrayBuffer, priorElements);
        shader.HzbDepth = source; shader.SrcMip = 0;
        {
    var commands=RHICommandContext.Current;
    using var scope28=commands.EnterBoundary(new("EngineGeometry",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope28.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            native.DuringDraw = () =>
            {
                Assert.Throws<InvalidOperationException>(() => commands.DrawIndexed(geometry.Inputs, new(0, 3)));
                Assert.Throws<InvalidOperationException>(() => commands.SetDynamicState(new()));
                Assert.Throws<InvalidOperationException>(commands.EndRenderPass);
            };
            commands.DrawIndexed(geometry.Inputs, new(0, 3));
#if DEBUG
            long queries = OpenGlStateCache.Current.BoundaryQueries;
#endif
            Assert.True(GpuResourceManager.IsEngineGeometryConfigurationCurrent(certification));
            commands.DrawIndexed(geometry.Inputs, new(0, 3));
#if DEBUG
            Assert.Equal(queries, OpenGlStateCache.Current.BoundaryQueries);
#endif
            native.Mesh.IndicesCount = 2;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexed(geometry.Inputs, new(0, 3)));
            native.Mesh.IndicesCount = 3;
        });
}
        Assert.Equal(0, native.Draws);
        Assert.Equal(priorVao, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(priorElements, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
        var failure = new ArithmeticException("Fixture operation failure.");
        Assert.Same(failure, Assert.Throws<ArithmeticException>(() =>
        {
            var commands = RHICommandContext.Current;
            using var failed = commands.EnterBoundary(new("EngineGeometryFailure", true, graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            failed.Run(() =>
            {
                commands.BeginRenderPass(target.PostprocessPass()); commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(shader.CaptureBindings()); commands.DrawIndexed(geometry.Inputs, new(0,3));
                throw failure;
            });
        }));
        Assert.Equal(priorVao, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(priorElements, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        geometry.Dispose(); Assert.Equal(1, native.Deletes);
        Assert.False(GpuResourceManager.IsEngineGeometryConfigurationCurrent(certification));
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
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, native.Mesh.VaoId); GL.DisableVertexAttribArray(1);
        Assert.ThrowsAny<Exception>(() => EngineFullscreenGeometry.Upload(native.Render.Object, native.Data));
        Assert.Equal(1, native.Deletes); Assert.Equal(0, native.Draws);
    }

    /// <summary>A partially failed engine upload restores the caller's actual vertex and array bindings.</summary>
    [Fact]
    public void FailedUploadRestoresCallerBindings()
    {
        EnsureContextValid();
        using var native = new NativeMesh(); using var previous = NativeResourceSetup.CreateVertexArray(); using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, previous.VertexArrayId); OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer.BufferId);
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
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.VertexArray, mesh.VaoId));
        RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, mesh.xyzVboId));
        RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, mesh.uvVboId));
        RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, mesh.vboIdIndex));
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
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(), uv = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        /// <summary>Constructs exactly the position/UV native layout accepted at the engine upload boundary.</summary>
        internal NativeMesh()
        {
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            positions.UploadData(Data.xyz); uv.UploadData(Data.Uv); indices.UploadIndices(new uint[] { 0, 1, 2 });
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, positions.BufferId);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(0);
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, uv.BufferId);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 0, IntPtr.Zero); GL.EnableVertexAttribArray(1);
            OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
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
        internal void RebindElements(GpuEbo buffer) => OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, buffer.BufferId);
    }
    #endregion
}
