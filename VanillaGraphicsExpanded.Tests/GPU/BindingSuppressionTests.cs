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
        var cache = StateCache.Current;
        cache.InvalidateAll();
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        try
        {
            cache.BindTexture(TextureTarget.Texture2D, 1, texture);
            cache.BindSampler(1, sampler);
            long textures = cache.TextureBindCount, samplers = cache.SamplerBindCount;
            cache.ActiveTexture(0);
            cache.BindTexture(TextureTarget.Texture2D, 1, texture);
            cache.BindTextureOnActiveUnit(TextureTarget.Texture2D, texture);
            cache.BindSampler(1, sampler);
            Assert.Equal(textures, cache.TextureBindCount);
            Assert.Equal(samplers, cache.SamplerBindCount);
            Assert.Equal((int)TextureUnit.Texture1, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));

            // An external mutation requires invalidation; the same request must then reach GL again.
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindSampler(1, 0);
            cache.Invalidate(EPipelineState.Bindings);
            cache.BindTexture(TextureTarget.Texture2D, 1, texture);
            cache.BindSampler(1, sampler);
            Assert.Equal(textures + 1, cache.TextureBindCount);
            Assert.Equal(samplers + 1, cache.SamplerBindCount);
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));
            // An unknown active unit cannot authorize suppression or a guessed per-unit update.
            GL.ActiveTexture(TextureUnit.Texture2);
            cache.Invalidate(EPipelineState.ActiveTextureUnit);
            cache.BindTextureOnActiveUnit(TextureTarget.Texture2D, texture);
            Assert.Equal(textures + 2, cache.TextureBindCount);
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
        var cache = StateCache.Current;
        cache.InvalidateAll();
        int read = GL.GenFramebuffer(), draw = GL.GenFramebuffer();
        try
        {
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            Assert.Equal(draw, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(read, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            cache.BindFramebuffer(FramebufferTarget.Framebuffer, draw);
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
        var cache = StateCache.Current;
        int vao = GL.GenVertexArray(), buffer = GL.GenBuffer();
        try
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            GL.BindVertexArray(0);
            cache.InvalidateAll();
            cache.BindVertexArray(vao);
            Assert.Equal(buffer, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
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
        var cache = StateCache.Current;
        int first = GL.GenBuffer(), second = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.UniformBuffer, first);
            GL.BufferData(BufferTarget.UniformBuffer, 256, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            cache.InvalidateAll();
            /// <summary>Applies the indexed binding variant exercised by this case.</summary>
            void Bind()
            {
                if (range) cache.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, first, 0, 16);
                else cache.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, first);
            }
            Bind();
            long calls = cache.ResourceSlotBindCount;
            Bind();
            Assert.Equal(calls, cache.ResourceSlotBindCount);
            cache.BindBuffer(BufferTarget.UniformBuffer, second);
            Bind();
            Assert.Equal(calls + 1, cache.ResourceSlotBindCount);
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

