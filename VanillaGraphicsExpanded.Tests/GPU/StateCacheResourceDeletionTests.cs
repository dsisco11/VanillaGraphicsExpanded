using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native deletion effects without discarding unrelated binding knowledge.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class StateCacheResourceDeletionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Sampler and framebuffer bindings
    /// <summary>Deleting a sampler clears its slots while preserving other and unknown slots.</summary>
    [Fact]
    public void SamplerDeletionPreservesSurvivingAndUnknownSlots()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        int retired = GL.GenSampler(), survivor = GL.GenSampler();
        try
        {
            cache.InvalidateAll();
            cache.BindSampler(0, retired);
            cache.BindSampler(1, survivor);
            cache.BindSampler(2, retired);
            EngineStateCalls.DeleteSampler(retired);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int retired = GL.GenFramebuffer(), survivor = GL.GenFramebuffer();
        try
        {
            cache.InvalidateAll();
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, retired);
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer, survivor);
            EngineStateCalls.DeleteFramebuffer(retired);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.ReadFramebuffer, out int read));
            Assert.Equal(0, read);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int draw));
            Assert.Equal(survivor, draw);
            Assert.Equal(0, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(survivor, GL.GetInteger(GetPName.DrawFramebufferBinding));
            cache.InvalidateAll();
            EngineStateCalls.DeleteFramebuffer(survivor);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int retired = GL.GenVertexArray(), survivor = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            cache.BindVertexArray(retired);
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            cache.BindVertexArray(survivor);
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            EngineStateCalls.DeleteVertexArray(retired);
            Assert.True(cache.TryGetCachedCurrentVao(out int selected));
            Assert.Equal(survivor, selected);
            Assert.True(cache.TryGetCachedBoundBuffer(BufferTarget.ElementArrayBuffer, out int element));
            Assert.Equal(buffer, element);
            Assert.Equal(buffer, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            EngineStateCalls.DeleteVertexArray(survivor);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int[] retired = [GL.GenBuffer(), GL.GenBuffer()];
        int survivor = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            cache.BindBuffer(BufferTarget.ArrayBuffer, retired[0]);
            cache.BindBuffer(BufferTarget.CopyReadBuffer, retired[1]);
            cache.BindBuffer(BufferTarget.CopyWriteBuffer, survivor);
            EngineStateCalls.DeleteBuffers(retired.Length, ref retired[0]);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int array = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            cache.InvalidateAll();
            cache.BindVertexArray(array);
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            cache.BindVertexArray(0);
            EngineStateCalls.DeleteBuffers(1, ref buffer);
            // GL keeps storage referenced by unbound containers until their attachment is released.
            cache.BindVertexArray(array);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int program = GL.CreateProgram(), shader = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/deletion.vsh", ShaderType.VertexShader);
        try
        {
            // Build a minimal executable so the test exercises deferred native deletion.
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            cache.UseProgram(program);
            EngineStateCalls.DeleteProgram(program);
            Assert.True(cache.TryGetCachedCurrentProgram(out int selected));
            Assert.Equal(program, selected);
            Assert.Equal(program, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(GL.IsProgram(program));
            cache.UseProgram(0);
            Assert.False(GL.IsProgram(program));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.UseProgram(0); if (GL.IsProgram(program)) GL.DeleteProgram(program); GL.DeleteShader(shader); cache.InvalidateAll(); }
    }
    #endregion
    #endregion
}
