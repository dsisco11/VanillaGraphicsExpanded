using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Verifies helper scopes recover unknown native bindings before temporary buffer operations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferUnknownBindingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Unknown VAO and element selection survive a temporary EBO wrapper scope.</summary>
    [Fact]
    public void ElementScopeRestoresUnknownNativeSelection()
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var sentinel = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using var target = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, sentinel.BufferId);
            OpenGlStateCache.Current.InvalidateAll();
            using (OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(target.Role), target.BufferId)) Assert.Equal(target.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Named mapping, flushing and unmapping each restore unknown native selections and retain written data.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedMappingPreservesUnknownBindings(bool element)
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        try
        {
            using var copySentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using var vao = NativeResourceSetup.CreateVertexArray();
            using GpuBufferObject sentinel = element ? RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource() : RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using GpuBufferObject target = element ? RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource() : RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            target.Allocate(16);
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, copySentinel.BufferId);
            var query = element ? GetPName.ElementArrayBufferBinding : GetPName.ArrayBufferBinding;
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(target.Role), sentinel.BufferId);
            OpenGlStateCache.Current.InvalidateAll();
            using (var mapped = target.MapRange<int>(0, 4, BufferMapAccess.Write | BufferMapAccess.ExplicitFlush))
            {
                Assert.True(mapped.IsMapped);
                Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
                Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
                mapped.Span[0] = 12345;
                OpenGlStateCache.Current.InvalidateAll();
                mapped.Flush();
                Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
                Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
                OpenGlStateCache.Current.InvalidateAll();
            }
            Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
            Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            using (var read = target.MapRange<int>(0, 4, BufferMapAccess.Read)) Assert.Equal(12345, read.Span[0]);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
            Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Named managed array allocation preserves unknown bindings and publishes its uploaded capacity.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedArrayUploadPreservesUnknownBindings(bool element)
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            using GpuBufferObject sentinel = element ? RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource() : RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using GpuBufferObject target = element ? RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource() : RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(target.Role), sentinel.BufferId);
            OpenGlStateCache.Current.InvalidateAll();
            GpuBufferUploader.Allocate(target, 16, new[] { 12, 34, 56, 78 }, BufferUsage.DynamicDraw);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(element ? GetPName.ElementArrayBufferBinding : GetPName.ArrayBufferBinding));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(16, target.SizeBytes);
            using (var read = target.MapRange<int>(0, 4, BufferMapAccess.Read))
            Assert.Equal(new[] { 12, 34, 56, 78 }, read.Span.ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>A failed native named mapping preserves the unknown copy-write selection.</summary>
    [Fact]
    public void FailedNativeMappingPreservesUnknownSelection()
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        try
        {
            using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using var target = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            target.Allocate(16);
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, sentinel.BufferId);
            OpenGlStateCache.Current.InvalidateAll();
            using var native = new NativeDriverCapture();
            int calls = 0;
            native.Add("glMapNamedBufferRange", new MapRange((_, _, _, _) => { calls++; return 0; }));
            native.Install();
            Assert.Throws<InvalidOperationException>(() => target.MapRange<int>(0, 4, BufferMapAccess.Read));
            Assert.Equal(1, calls);
            Assert.Equal(sentinel.BufferId, GL.GetInteger((GetPName)All.CopyWriteBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Matches the native mapping result ABI for a deterministic null outcome.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint MapRange(uint buffer, nint offset, nint length, uint access);
    #endregion
}
