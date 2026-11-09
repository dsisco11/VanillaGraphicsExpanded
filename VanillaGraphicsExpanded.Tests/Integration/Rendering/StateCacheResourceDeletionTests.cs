using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks native deletion effects without discarding unrelated binding knowledge.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class StateCacheResourceDeletionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Sampler and framebuffer bindings
    /// <summary>Deleting a sampler clears its slots while preserving other and unknown slots.</summary>
    [Fact]
    public void SamplerDeletionPreservesSurvivingAndUnknownSlots()
    {
        // Arrange: Bind several samplers, including an unknown slot, before deleting one native object.
        // Act: Delete the selected sampler through the observed GL path.
        // Assert: Its slots clear while surviving and unknown slots retain their knowledge.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int retired = GL.GenSampler(), survivor = GL.GenSampler();
        try
        {
            cache.InvalidateAll();
            OpenGLImageBindings.BindSampler(cache, 0, retired);
            OpenGLImageBindings.BindSampler(cache, 1, survivor);
            OpenGLImageBindings.BindSampler(cache, 2, retired);
            GL.DeleteSampler(retired);
            Assert.True(cache.TryGetCachedBoundSampler(0, out int first));
            Assert.Equal(0, first);
            Assert.True(cache.TryGetCachedBoundSampler(2, out int second));
            Assert.Equal(0, second);
            Assert.True(cache.TryGetCachedBoundSampler(1, out int remaining));
            Assert.Equal(survivor, remaining);
            Assert.False(cache.TryGetCachedBoundSampler(3, out _));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(0, GL.GetInteger(GetPName.SamplerBinding));
            GL.ActiveTexture(TextureUnit.Texture1);
            Assert.Equal(survivor, GL.GetInteger(GetPName.SamplerBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteSampler(retired); GL.DeleteSampler(survivor); GL.ActiveTexture(TextureUnit.Texture0); cache.InvalidateAll(); }
    }

    /// <summary>Deleting one framebuffer direction retains the other direction's snapshot.</summary>
    [Fact]
    public void FramebufferDeletionPreservesSurvivingDirection()
    {
        // Arrange: Bind distinct draw and read framebuffers and capture both directions.
        // Act: Delete the framebuffer selected in only one direction.
        // Assert: The unaffected direction retains its snapshot.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int retired = GL.GenFramebuffer(), survivor = GL.GenFramebuffer();
        try
        {
            cache.InvalidateAll();
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, retired);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, survivor);
            GL.DeleteFramebuffer(retired);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.ReadFramebuffer, out int read));
            Assert.Equal(0, read);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int draw));
            Assert.Equal(survivor, draw);
            Assert.Equal(0, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(survivor, GL.GetInteger(GetPName.DrawFramebufferBinding));
            cache.InvalidateAll();
            GL.DeleteFramebuffer(survivor);
            Assert.False(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.ReadFramebuffer, out _));
            Assert.False(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteFramebuffer(retired); GL.DeleteFramebuffer(survivor); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); cache.InvalidateAll(); }
    }
    #endregion

    #region Vertex array and buffer bindings
    /// <summary>Deleting another VAO retains the selected VAO and its element buffer.</summary>
    [Fact]
    public void VertexArrayDeletionPreservesSurvivingElementAssociation()
    {
        // Arrange: Bind multiple vertex arrays with distinct element-buffer associations.
        // Act: Delete an unselected vertex array.
        // Assert: The selected VAO and its element buffer remain known.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int retired = GL.GenVertexArray(), survivor = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            OpenGLVertexSelection.Bind(cache, retired);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, buffer);
            OpenGLVertexSelection.Bind(cache, survivor);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, buffer);
            GL.DeleteVertexArray(retired);
            Assert.True(cache.TryGetCachedCurrentVao(out int selected));
            Assert.Equal(survivor, selected);
            Assert.True(cache.TryGetCachedBoundBuffer(BufferTarget.ElementArrayBuffer, out int element));
            Assert.Equal(buffer, element);
            Assert.Equal(buffer, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            GL.DeleteVertexArray(survivor);
            Assert.True(cache.TryGetCachedCurrentVao(out selected));
            Assert.Equal(0, selected);
            Assert.Equal(0, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(retired); GL.DeleteVertexArray(survivor); GL.DeleteBuffer(buffer); cache.InvalidateAll(); }
    }

    /// <summary>Bulk deletion processes every supplied name and leaves unrelated buffer targets known.</summary>
    [Fact]
    public void BulkBufferDeletionPreservesSurvivor()
    {
        // Arrange: Bind multiple buffers across targets and retain one unrelated survivor.
        // Act: Delete several supplied names in one call.
        // Assert: Every deleted name is processed while unrelated target bindings remain known.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int[] retired = [GL.GenBuffer(), GL.GenBuffer()];
        int survivor = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ArrayBuffer, retired[0]);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.CopyReadBuffer, retired[1]);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.CopyWriteBuffer, survivor);
            GL.DeleteBuffers(retired.Length, ref retired[0]);
            Assert.False(cache.TryGetCachedBoundBuffer(BufferTarget.ArrayBuffer, out _));
            Assert.False(cache.TryGetCachedBoundBuffer(BufferTarget.CopyReadBuffer, out _));
            Assert.True(cache.TryGetCachedBoundBuffer(BufferTarget.CopyWriteBuffer, out int remaining));
            Assert.Equal(survivor, remaining);
            Assert.Equal(0, cache.GetBoundBuffer(BufferTarget.ArrayBuffer));
            Assert.Equal(0, cache.GetBoundBuffer(BufferTarget.CopyReadBuffer));
            Assert.False(GL.IsBuffer(retired[0]));
            Assert.False(GL.IsBuffer(retired[1]));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteBuffers(retired.Length, retired); GL.DeleteBuffer(survivor); cache.InvalidateAll(); }
    }

    /// <summary>Deletion forgets an unbound VAO's retained storage instead of falsely recording zero.</summary>
    [Fact]
    public void BufferDeletionRequeriesUnboundVertexArrayStorage()
    {
        // Arrange: Retain VAO storage knowledge for an unbound array before deleting its backing buffer.
        // Act: Delete the buffer and query that VAO storage again.
        // Assert: Stale storage knowledge is forgotten rather than reported as zero.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int array = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            OpenGLVertexSelection.Bind(cache, array);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, buffer);
            OpenGLVertexSelection.Bind(cache, 0);
            GL.DeleteBuffers(1, ref buffer);
            // GL keeps storage referenced by unbound containers until their attachment is released.
            OpenGLVertexSelection.Bind(cache, array);
            Assert.False(cache.TryGetCachedBoundBuffer(BufferTarget.ElementArrayBuffer, out _));
            int native = GL.GetInteger(GetPName.ElementArrayBufferBinding);
            Assert.Equal(buffer, native);
            Assert.Equal(native, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(array); GL.DeleteBuffer(buffer); cache.InvalidateAll(); }
    }
    #endregion

    #region Program bindings
    /// <summary>A current program remains executable after deletion until it is unbound.</summary>
    [Fact]
    public void ProgramDeletionPreservesDeferredCurrentProgram()
    {
        // Arrange: Select a current program and mark it for deferred native deletion.
        // Act: Delete the selected program before unbinding it.
        // Assert: It remains executable until the native binding is released.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var programOwner = NativeResourceSetup.CreateProgram();
        int program = programOwner.ProgramId;
        using var shaderOwner = VanillaGraphicsExpanded.Tests.Support.GPU.Shaders.BuiltShaderFixture.LoadFixture("tests/deletion.vsh", ShaderType.VertexShader);
        int shader = shaderOwner.ShaderId;
        try
        {
            // Build a minimal executable so the test exercises deferred native deletion.
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            OpenGLProgramSelection.Select(cache, program);
            int nativeProgramId = (int)programOwner.Detach();
            Assert.Equal(program, nativeProgramId);
            GL.DeleteProgram(nativeProgramId);
            Assert.True(cache.TryGetCachedCurrentProgram(out int selected));
            Assert.Equal(program, selected);
            Assert.Equal(program, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(GL.IsProgram(program));
            OpenGLProgramSelection.Select(cache, 0);
            Assert.False(GL.IsProgram(program));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.UseProgram(0);
            if (GL.IsProgram(program)) GL.DeleteProgram(program);
            shaderOwner.Dispose();
            cache.InvalidateAll();
        }
    }
    #endregion
    #endregion
}
