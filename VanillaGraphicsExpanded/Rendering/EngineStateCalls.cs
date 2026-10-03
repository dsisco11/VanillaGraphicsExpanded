using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides signature-compatible replacements for engine OpenGL state calls.</summary>
/// <remarks>Only engine callers are transpiled; these adapters and cache native calls are never patched.</remarks>
internal static class EngineStateCalls
{
    #region Public API
    #region Fixed function
    /// <summary>Enables a cached capability or forwards an unsupported capability unchanged.</summary>
    public static void Enable(EnableCap cap) => GlStateCache.Current.SetCapability(cap, true);
    /// <summary>Disables a cached capability or forwards an unsupported capability unchanged.</summary>
    public static void Disable(EnableCap cap) => GlStateCache.Current.SetCapability(cap, false);
    /// <summary>Enables an indexed capability using its exact output index.</summary>
    public static void Enable(IndexedEnableCap cap, int index) => GlStateCache.Current.SetIndexedCapability(cap, index, true);
    /// <summary>Disables an indexed capability using its exact output index.</summary>
    public static void Disable(IndexedEnableCap cap, int index) => GlStateCache.Current.SetIndexedCapability(cap, index, false);
    /// <summary>Publishes the depth comparison.</summary>
    public static void DepthFunc(DepthFunction function) => GlStateCache.Current.SetDepthFunc(function);
    /// <summary>Publishes the depth write mask.</summary>
    public static void DepthMask(bool enabled) => GlStateCache.Current.SetDepthWriteMask(enabled);
    /// <summary>Adapts the engine's combined-factor overload without changing enum values.</summary>
    public static void BlendFunc(BlendingFactor source, BlendingFactor destination) =>
        GlStateCache.Current.SetBlendFunc(new((BlendingFactorSrc)source, (BlendingFactorDest)destination,
            (BlendingFactorSrc)source, (BlendingFactorDest)destination));
    /// <summary>Applies one output's factors to both RGB and alpha.</summary>
    public static void BlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination) =>
        GlStateCache.Current.SetBlendFuncIndexed(index, new(source, destination, source, destination));
    /// <summary>Publishes independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        GlStateCache.Current.SetBlendFunc(new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes one output's independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        GlStateCache.Current.SetBlendFuncIndexed(index, new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes the global color write mask.</summary>
    public static void ColorMask(bool red, bool green, bool blue, bool alpha) =>
        GlStateCache.Current.SetColorMask(GlColorMask.FromRgba(red, green, blue, alpha));
    /// <summary>Publishes line width without approximate-value suppression.</summary>
    public static void LineWidth(float width) => GlStateCache.Current.SetLineWidth(width);
    /// <summary>Publishes point size without approximate-value suppression.</summary>
    public static void PointSize(float size) => GlStateCache.Current.SetPointSize(size);
    /// <summary>Publishes one integer pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, int value) => GlStateCache.Current.SetPixelStore(parameter, value);
    /// <summary>Publishes one floating-point pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, float value) => GlStateCache.Current.SetPixelStore(parameter, value);
    /// <summary>Publishes patch vertex count while forwarding any unknown enum unchanged.</summary>
    public static void PatchParameter(PatchParameterInt parameter, int value)
    {
        if (parameter == PatchParameterInt.PatchVertices) GlStateCache.Current.SetPatchVertices(value);
        else GL.PatchParameter(parameter, value);
    }
    /// <summary>Updates the provoking convention used by cached tessellation checks.</summary>
    public static void ProvokingVertex(ProvokingVertexMode mode) => GlStateCache.Current.SetProvokingVertex(mode);
    #endregion

    #region Bindings
    /// <summary>Records a native executable switch while retaining the surrounding engine shader lifecycle.</summary>
    public static void UseProgram(int program) => GlStateCache.Current.UseProgram(program);
    /// <summary>Publishes the selected vertex array and its element-buffer association.</summary>
    public static void BindVertexArray(int array) => GlStateCache.Current.BindVertexArray(array);
    /// <summary>Preserves combined and independent read/draw framebuffer targets.</summary>
    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => GlStateCache.Current.BindFramebuffer(target, framebuffer);
    /// <summary>Publishes a renderbuffer binding, preserving unsupported enum behavior.</summary>
    public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
    {
        if (target == RenderbufferTarget.Renderbuffer) GlStateCache.Current.BindRenderbuffer(renderbuffer);
        else GL.BindRenderbuffer(target, renderbuffer);
    }
    /// <summary>Converts the native texture-unit enum to the cache's zero-based unit.</summary>
    public static void ActiveTexture(TextureUnit texture) => GlStateCache.Current.ActiveTexture((int)texture - (int)TextureUnit.Texture0);
    /// <summary>Binds on the active unit without altering sampler ownership.</summary>
    public static void BindTexture(TextureTarget target, int texture)
    {
        var cache = GlStateCache.Current;
        cache.BindTexture(target, cache.GetActiveTextureUnit(), texture);
    }
    /// <summary>Publishes a sampler independently of texture binding.</summary>
    public static void BindSampler(int unit, int sampler) => GlStateCache.Current.BindSampler(unit, sampler);
    /// <summary>Publishes generic and vertex-array-owned buffer bindings.</summary>
    public static void BindBuffer(BufferTarget target, int buffer) => GlStateCache.Current.BindBuffer(target, buffer);
    /// <summary>Publishes an indexed whole-buffer binding and its generic binding side effect.</summary>
    public static void BindBufferBase(BufferRangeTarget target, int index, int buffer) => GlStateCache.Current.BindBufferBase(target, index, buffer);
    /// <summary>Publishes an indexed range without truncating native offsets or sizes.</summary>
    public static void BindBufferRange(BufferRangeTarget target, int index, int buffer, IntPtr offset, IntPtr size) =>
        GlStateCache.Current.BindBufferRange(target, index, buffer, offset, size);
    /// <summary>Publishes an image unit including the complete image-view selection.</summary>
    public static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format) =>
        GlStateCache.Current.BindImageTexture(unit, texture, level, layered, layer, access, format);
    /// <summary>Publishes a separable shader pipeline binding.</summary>
    public static void BindProgramPipeline(int pipeline) => GlStateCache.Current.BindProgramPipeline(pipeline);
    /// <summary>Publishes transform-feedback binding without changing unsupported enum behavior.</summary>
    public static void BindTransformFeedback(TransformFeedbackTarget target, int feedback)
    {
        if (target == TransformFeedbackTarget.TransformFeedback) GlStateCache.Current.BindTransformFeedback(feedback);
        else GL.BindTransformFeedback(target, feedback);
    }
    #endregion

    #region Resource retirement
    /// <summary>Preserves texture deletion's implicit unbinding in cached snapshots.</summary>
    public static void DeleteTexture(int texture) => GlStateCache.Current.DeleteTexture(texture);
    /// <summary>Preserves buffer deletion's effect on generic and indexed bindings.</summary>
    public static void DeleteBuffer(int buffer) => GlStateCache.Current.DeleteBuffer(buffer);
    /// <summary>Preserves bulk buffer deletion and its implicit binding changes.</summary>
    public static void DeleteBuffers(int count, ref int buffers) => GlStateCache.Current.DeleteBuffers(count, ref buffers);
    /// <summary>Invalidates sampler bindings after engine retirement.</summary>
    public static void DeleteSampler(int sampler) => GlStateCache.Current.DeleteSampler(sampler);
    /// <summary>Invalidates read/draw framebuffer bindings after engine retirement.</summary>
    public static void DeleteFramebuffer(int framebuffer) => GlStateCache.Current.DeleteFramebuffer(framebuffer);
    /// <summary>Invalidates vertex-array-owned associations after engine retirement.</summary>
    public static void DeleteVertexArray(int array) => GlStateCache.Current.DeleteVertexArray(array);
    /// <summary>Preserves deferred native program deletion and invalidates executable knowledge.</summary>
    public static void DeleteProgram(int program) => GlStateCache.Current.DeleteProgram(program);
    #endregion
    #endregion
}
