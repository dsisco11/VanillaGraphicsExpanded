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
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks direct RHI drawing with controlled imported engine geometry upload and deletion.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineGraphicsGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Manager certification reuses completed facts and rejects changed or retired original registrations.</summary>
    [Fact]
    public void ManagerCertificationRejectsChangedAndRetiredArrays()
    {
        // Arrange: Configure an external VAO/EBO association and acquire a manager certification receipt for it.
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
            var receipt = RHIResourceManager.AcquireEngineGeometryConfiguration(array, layout, [], indices);
            Assert.True(receipt.IsPrepared);
            using (var unrelated = NativeResourceSetup.CreateVertexArray())
            {
                OpenGlStateCache.ObserveVertexMetadata(unrelated.VertexArrayId, named: true, elementBinding: false);
                Assert.True(receipt.IsPrepared);
            }

            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuVertexArrayMetadata>>(manager.GetResourceRegistration(array));
            manager.PublishMetadata(entry, new GpuVertexArrayMetadata(), GpuMetadataChange.Metadata);
            Assert.True(receipt.IsPrepared);
            // Act: Change the certified attribute configuration, reacquire the receipt, then retire its VAO owner.
            OpenTK.Graphics.OpenGL4.GL.VertexArrayAttribFormat(array.VertexArrayId, 0, 2, OpenTK.Graphics.OpenGL4.VertexAttribType.Float, false, 0);
            // Assert: Unrelated changes preserve certification; changed configuration and owner retirement invalidate it.
            Assert.False(receipt.IsPrepared);
            receipt = RHIResourceManager.AcquireEngineGeometryConfiguration(array, layout, [], indices);
            Assert.True(receipt.IsPrepared);
            array.Dispose();
            Assert.False(receipt.IsPrepared);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Changing a certified original index association invalidates its receipt; matching reacquisition is prepared.</summary>
    [Fact]
    public void ChangedImportedIndexAssociationRejectsReuse()
    {
        // Arrange: Upload imported engine geometry and retain its prepared index-association receipt.
        EnsureContextValid();
        using var native = new NativeMesh();
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        var packet = geometry.Inputs;
        var receipt = packet.ImportedArray!;
        Assert.True(receipt.IsPrepared);
        using var replacement = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
            { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 12, Usage = BufferUsage.StaticDraw }).RequireResource();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, native.Mesh.VaoId);
        // Act: Replace the original VAO's element buffer and reacquire against the same captured packet.
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ElementArrayBuffer, replacement.BufferId);
        // Assert: The old receipt is rejected while matching reacquisition is prepared for the new association.
        Assert.False(receipt.IsPrepared);
        var updated = RHIResourceManager.AcquireEngineGeometryConfiguration(
            receipt.Array, packet.Layout, packet.Streams, packet.Indices!.Value);
        Assert.True(updated.IsPrepared);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Imported engine geometry remains query-free on repeated RHI draws and changed metadata rejects reuse.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedEngineGeometrySubmissionPreservesContract(bool incomingSameMesh)
    {
        // Arrange: Import an engine mesh and choose either its selectors or distinct caller-owned VAO/EBO state.
        EnsureContextValid();
        using var native = new NativeMesh();
        // The first variant starts from the imported mesh; the other uses a distinct caller-owned
        // VAO/EBO so the draw path must restore whichever selectors were active on entry.
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.R32f)]), DynamicPipelineState.Viewport)).RequireResource();
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        var certification = Assert.IsType<RHIGeometryImport>(geometry.Inputs.ImportedArray);
        Assert.True(certification.IsPrepared);
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
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
            // Act: Draw twice with the certified packet, mutate its source index count, then attempt reuse and failure cleanup.
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            // The second draw reuses the certified import; changing its index count then tests
            // that the next submission rejects the mutated original mesh before a native draw.
            commands.DrawIndexed(geometry.Inputs, new(0, 3));
#if DEBUG
            long queries = OpenGlStateCache.Current.BoundaryQueries;
#endif
            Assert.True(certification.IsPrepared);
            commands.DrawIndexed(geometry.Inputs, new(0, 3));
#if DEBUG
            Assert.Equal(queries, OpenGlStateCache.Current.BoundaryQueries);
#endif
            native.Mesh.IndicesCount = 2;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexed(geometry.Inputs, new(0, 3)));
            native.Mesh.IndicesCount = 3;
        });
}
        // Assert: Caller selectors and rendered depth survive normal and exceptional boundary exits.
        Assert.Equal(priorVao, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(priorElements, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        Assert.InRange(target[0].ReadTexel()[0], .1999f, .2001f);
        // A failure after native submission must still restore the caller's VAO and element buffer.
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
        Assert.False(certification.IsPrepared);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Owned engine mesh cleanup defers live origins and abandons retired names before native engine deletion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void MeshRetirementUsesOriginatingContext(bool retired)
    {
        // Arrange: Upload one engine mesh and select either a live origin or retired context before owner disposal.
        EnsureContextValid();
        using var native = new NativeMesh();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        object replacement = new();
        EngineFullscreenGeometry? geometry = null;
        try
        {
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
            native.Render.Setup(render => render.DeleteMesh(native.Mesh)).Callback(() =>
            {
                native.Deletes++;
                DeleteNativeMesh(native.Mesh);
            });
            // Act: Retire the context or detach it, dispose geometry twice, then drain cleanup after restoring the fixture.
            if (retired)
            {
                global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
                global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            }
            else OpenTK.Windowing.GraphicsLibraryFramework.GLFW.MakeContextCurrent(null);
            geometry.Dispose(); geometry.Dispose();
            // Assert: Disposal defers engine deletion until a live originating context can process the queued owner.
            Assert.Equal(0, native.Deletes);
            fixture.MakeCurrent();
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
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
                global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(replacement);
                fixture.RestoreContextRegistration();
            }
            geometry?.Dispose();
            DeleteNativeMesh(native.Mesh);
            manager.Dispose(); RHIResourceManagerSystem.Shutdown();
        }
    }

    /// <summary>Authored geometry admission avoids scanning or repairing external attribute state.</summary>
    [Fact]
    public void ExternalLayoutDoesNotOverrideAuthoredGeometry()
    {
        // Arrange: Disable one external VAO attribute before importing the engine layout.
        EnsureContextValid();
        using var native = new NativeMesh();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, native.Mesh.VaoId); GL.DisableVertexAttribArray(1);
        using var reads = new GeometryDescriptorCapture();
        // Act: Admit authored geometry while observing whether descriptor discovery reads or repairs the external VAO.
        using var geometry = EngineFullscreenGeometry.Upload(native.Render.Object, native.Data);
        // Assert: Admission performs no descriptor reads or mesh deletion and leaves the disabled attribute unchanged.
        Assert.Equal(0, reads.Reads); Assert.Equal(0, native.Deletes);
        GL.GetVertexAttrib(1, VertexAttribParameter.ArrayEnabled, out int enabled);
        Assert.Equal(0, enabled);
    }

    /// <summary>A partially failed engine upload restores the caller's actual vertex and array bindings.</summary>
    [Fact]
    public void FailedUploadRestoresCallerBindings()
    {
        // Arrange: Bind caller-owned VAO/buffer sentinels and inject failure after the engine changes native selectors.
        EnsureContextValid();
        using var native = new NativeMesh(); using var previous = NativeResourceSetup.CreateVertexArray(); using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, previous.VertexArrayId); OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer.BufferId);
        native.Render.Setup(render => render.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
        {
            GL.BindVertexArray(native.Mesh.VaoId); GL.BindBuffer(BufferTarget.ArrayBuffer, native.Mesh.xyzVboId);
            throw new ArithmeticException("Fixture upload failure.");
        });
        using var calls = new BindingCallCapture();
        // Act: Fail the engine upload, then repeat equal managed binding requests to observe restored cache knowledge.
        Assert.Throws<ArithmeticException>(() => EngineFullscreenGeometry.Upload(native.Render.Object, native.Data));
        // Assert: No extra query or equal native bind occurs, and driver selectors still match the caller's sentinels.
        calls.Reset();
        // The backend's restoration already establishes these selectors. Failure must not
        // discard their knowledge and force another query or an equal native bind.
        Assert.Equal(previous.VertexArrayId, OpenGlStateCache.Current.GetCurrentVao());
        Assert.Equal(buffer.BufferId, OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, previous.VertexArrayId);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer.BufferId);
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
        Assert.Equal(0, calls.Calls(nameof(GL.BindVertexArray)));
        Assert.Equal(0, calls.Calls(nameof(GL.BindBuffer)));
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

    /// <summary>Owns native engine-layout storage and supplies controlled engine upload/deletion calls.</summary>
    private sealed class NativeMesh : IDisposable
    {
        internal readonly Mock<IRenderAPI> Render = new(MockBehavior.Strict);
        internal readonly MeshData Data = new() { xyz = [-1, -1, 0, 3, -1, 0, -1, 3, 0], Uv = [0, 0, 2, 0, 0, 2],
            Indices = [0, 1, 2], VerticesCount = 3, IndicesCount = 3, Rgba = null };
        internal readonly VAO Mesh;
        internal int Deletes;
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(), uv = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        #region Public API
        /// <summary>Releases native fixture storage without invoking an uninitialized engine owner finalizer.</summary>
        public void Dispose() { vao.Dispose(); indices.Dispose(); uv.Dispose(); positions.Dispose(); }
        #endregion

        #region Internal API
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
            Render.Setup(render => render.UploadMesh(It.IsAny<MeshData>())).Returns(Mesh);
            Render.Setup(render => render.DeleteMesh(Mesh)).Callback(() => Deletes++);
        }

        /// <summary>Reestablishes the owned native VAO association through the managed DSA path.</summary>
        internal void RebindElements(GpuEbo buffer) => OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, buffer.BufferId);
        #endregion
    }
    #endregion
}
