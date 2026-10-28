using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks binding suppression against native state and implicit binding side effects.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class BindingSuppressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Repeated resource assignments are suppressed while explicit unit selection remains observable.</summary>
    [Fact]
    public void TextureAndSamplerRepeatsPreserveUnitSelectionAndInvalidation()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        try
        {
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 1, texture);
            OpenGLImageBindings.BindSampler(cache, 1, sampler);
#if DEBUG
            long textures = cache.TextureBindCount, samplers = cache.SamplerBindCount;
#endif
            OpenGLImageBindings.ActiveTexture(cache, 0);
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 1, texture);
            OpenGLImageBindings.BindTextureOnActiveUnit(cache, TextureTarget.Texture2D, texture);
            OpenGLImageBindings.BindSampler(cache, 1, sampler);
#if DEBUG
            Assert.Equal(textures, cache.TextureBindCount);
            Assert.Equal(samplers, cache.SamplerBindCount);
#endif
            Assert.Equal((int)TextureUnit.Texture1, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));

            // Intercepted external unbinding updates knowledge; the managed restore must reach GL.
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindSampler(1, 0);
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 1, texture);
            OpenGLImageBindings.BindSampler(cache, 1, sampler);
#if DEBUG
            Assert.Equal(textures + 2, cache.TextureBindCount);
            Assert.Equal(samplers + 2, cache.SamplerBindCount);
#endif
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));
            // An unknown active unit cannot authorize suppression or a guessed per-unit update.
            GL.ActiveTexture(TextureUnit.Texture2);
            cache.Invalidate(EPipelineState.ActiveTextureUnit);
            OpenGLImageBindings.BindTextureOnActiveUnit(cache, TextureTarget.Texture2D, texture);
#if DEBUG
            Assert.Equal(textures + 3, cache.TextureBindCount);
#endif
            Assert.Equal((int)TextureUnit.Texture2, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.False(cache.TryGetCachedActiveTextureUnit(out _));
            Assert.False(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 1, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindSampler(1, 0);
            GL.DeleteSampler(sampler);
            GL.DeleteTexture(texture);
            GL.ActiveTexture(TextureUnit.Texture0);
            cache.InvalidateAll();
        }
    }

    /// <summary>A combined framebuffer bind must restore both directions even if the draw alias already matches.</summary>
    [Fact]
    public void CombinedFramebufferBindingRepairsDistinctReadTarget()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        int read = GL.GenFramebuffer(), draw = GL.GenFramebuffer();
        try
        {
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, read);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, draw);
            Assert.Equal(draw, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(read, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, draw);
            Assert.Equal(draw, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(read);
            GL.DeleteFramebuffer(draw);
            cache.InvalidateAll();
        }
    }

    /// <summary>Borrowed VAOs retain native element-buffer state rather than acquiring an assumed zero binding.</summary>
    [Fact]
    public void BorrowedVaoElementBindingIsNotAssumedToBeZero()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        int vao = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            GL.BindVertexArray(0);
            cache.InvalidateAll();
            OpenGLVertexSelection.Bind(cache, vao);
            Assert.Equal(buffer, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, 0);
            Assert.Equal(0, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindVertexArray(0);
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(buffer);
            cache.InvalidateAll();
        }
    }

    /// <summary>Repeating an indexed bind repairs its generic binding after another buffer was selected.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedBindingPreservesGenericBindingSideEffect(bool range)
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        int first = GL.GenBuffer(), second = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.UniformBuffer, first);
            GL.BufferData(BufferTarget.UniformBuffer, 256, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            cache.InvalidateAll();
            /// <summary>Applies the indexed binding variant exercised by this case.</summary>
            void Bind()
            {
                if (range) OpenGLBufferBindings.BindBufferRange(cache, BufferRangeTarget.UniformBuffer, 0, first, 0, 16);
                else OpenGLBufferBindings.BindBufferBase(cache, BufferRangeTarget.UniformBuffer, 0, first);
            }
            Bind();
#if DEBUG
            long calls = cache.ResourceSlotBindCount;
#endif
            Bind();
#if DEBUG
            Assert.Equal(calls, cache.ResourceSlotBindCount);
#endif
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.UniformBuffer, second);
            Bind();
#if DEBUG
            Assert.Equal(calls + 1, cache.ResourceSlotBindCount);
#endif
            Assert.Equal(first, GL.GetInteger(GetPName.UniformBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, 0);
            GL.DeleteBuffer(first);
            GL.DeleteBuffer(second);
            cache.InvalidateAll();
        }
    }
    #endregion
}

