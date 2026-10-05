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
    public static void Enable(EnableCap cap) => StateCache.Current.SetCapability(cap, true);
    /// <summary>Disables a cached capability or forwards an unsupported capability unchanged.</summary>
    public static void Disable(EnableCap cap) => StateCache.Current.SetCapability(cap, false);
    /// <summary>Enables an indexed capability using its exact output index.</summary>
    public static void Enable(IndexedEnableCap cap, int index) => StateCache.Current.SetIndexedCapability(cap, index, true);
    /// <summary>Disables an indexed capability using its exact output index.</summary>
    public static void Disable(IndexedEnableCap cap, int index) => StateCache.Current.SetIndexedCapability(cap, index, false);
    /// <summary>Publishes the depth comparison.</summary>
    public static void DepthFunc(DepthFunction function) => StateCache.Current.SetDepthFunc(function);
    /// <summary>Publishes the depth write mask.</summary>
    public static void DepthMask(bool enabled) => StateCache.Current.SetDepthWriteMask(enabled);
    /// <summary>Adapts the engine's combined-factor overload without changing enum values.</summary>
    public static void BlendFunc(BlendingFactor source, BlendingFactor destination) =>
        StateCache.Current.SetBlendFunc(new((BlendingFactorSrc)source, (BlendingFactorDest)destination,
            (BlendingFactorSrc)source, (BlendingFactorDest)destination));
    /// <summary>Applies one output's factors to both RGB and alpha.</summary>
    public static void BlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination) =>
        StateCache.Current.SetBlendFuncIndexed(index, new(source, destination, source, destination));
    /// <summary>Publishes independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        StateCache.Current.SetBlendFunc(new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes one output's independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        StateCache.Current.SetBlendFuncIndexed(index, new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes the global color write mask.</summary>
    public static void ColorMask(bool red, bool green, bool blue, bool alpha) =>
        StateCache.Current.SetColorMask(GlColorMask.FromRgba(red, green, blue, alpha));
    /// <summary>Publishes one draw output's write mask.</summary>
    public static void ColorMask(int index, bool red, bool green, bool blue, bool alpha) =>
        StateCache.Current.SetColorMaskIndexed(index, GlColorMask.FromRgba(red, green, blue, alpha));
    /// <summary>Adapts the engine integer viewport signature to declared dynamic state.</summary>
    public static void Viewport(int x, int y, int width, int height) =>
        StateCache.Current.ApplyDynamic(new Pipeline.State.DynamicDrawState { X = x, Y = y, Width = width, Height = height });
    /// <summary>Publishes the clear-operation color without treating it as pipeline state.</summary>
    public static void ClearColor(float red, float green, float blue, float alpha) =>
        StateCache.Current.SetClearColor(red, green, blue, alpha);
    /// <summary>Publishes line width without approximate-value suppression.</summary>
    public static void LineWidth(float width) => StateCache.Current.SetLineWidth(width);
    /// <summary>Publishes point size without approximate-value suppression.</summary>
    public static void PointSize(float size) => StateCache.Current.SetPointSize(size);
    /// <summary>Publishes one integer pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, int value) => StateCache.Current.SetPixelStore(parameter, value);
    /// <summary>Publishes one floating-point pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, float value) => StateCache.Current.SetPixelStore(parameter, value);
    /// <summary>Publishes patch vertex count while forwarding any unknown enum unchanged.</summary>
    public static void PatchParameter(PatchParameterInt parameter, int value)
    {
        if (parameter == PatchParameterInt.PatchVertices) StateCache.Current.SetPatchVertices(value);
        else GL.PatchParameter(parameter, value);
    }
    /// <summary>Updates the provoking convention used by cached tessellation checks.</summary>
    public static void ProvokingVertex(ProvokingVertexMode mode) => StateCache.Current.SetProvokingVertex(mode);
    #endregion

    #region Bindings
    /// <summary>Records a native executable switch while retaining the surrounding engine shader lifecycle.</summary>
    public static void UseProgram(int program) => StateCache.Current.UseProgram(program);
    /// <summary>Publishes the selected vertex array and its element-buffer association.</summary>
    public static void BindVertexArray(int array) => StateCache.Current.BindVertexArray(array);
    /// <summary>Preserves combined and independent read/draw framebuffer targets.</summary>
    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => StateCache.Current.BindFramebuffer(target, framebuffer);
    /// <summary>Publishes a renderbuffer binding, preserving unsupported enum behavior.</summary>
    public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
    {
        if (target == RenderbufferTarget.Renderbuffer) StateCache.Current.BindRenderbuffer(renderbuffer);
        else GL.BindRenderbuffer(target, renderbuffer);
    }
    /// <summary>Converts the native texture-unit enum to the cache's zero-based unit.</summary>
    public static void ActiveTexture(TextureUnit texture) => StateCache.Current.ActiveTexture((int)texture - (int)TextureUnit.Texture0);
    /// <summary>Binds on the native active unit without querying, reselecting it, or altering sampler ownership.</summary>
    public static void BindTexture(TextureTarget target, int texture) =>
        StateCache.Current.BindTextureOnActiveUnit(target, texture);
    /// <summary>Publishes a sampler independently of texture binding.</summary>
    public static void BindSampler(int unit, int sampler) => StateCache.Current.BindSampler(unit, sampler);
    /// <summary>Publishes generic and vertex-array-owned buffer bindings.</summary>
    public static void BindBuffer(BufferTarget target, int buffer) => StateCache.Current.BindBuffer(target, buffer);
    /// <summary>Publishes an indexed whole-buffer binding and its generic binding side effect.</summary>
    public static void BindBufferBase(BufferRangeTarget target, int index, int buffer) => StateCache.Current.BindBufferBase(target, index, buffer);
    /// <summary>Publishes an indexed range without truncating native offsets or sizes.</summary>
    public static void BindBufferRange(BufferRangeTarget target, int index, int buffer, IntPtr offset, IntPtr size) =>
        StateCache.Current.BindBufferRange(target, index, buffer, offset, size);
    /// <summary>Publishes an image unit including the complete image-view selection.</summary>
    public static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format) =>
        StateCache.Current.BindImageTexture(unit, texture, level, layered, layer, access, format);
    /// <summary>Publishes a separable shader pipeline binding.</summary>
    public static void BindProgramPipeline(int pipeline) => StateCache.Current.BindProgramPipeline(pipeline);
    /// <summary>Publishes transform-feedback binding without changing unsupported enum behavior.</summary>
    public static void BindTransformFeedback(TransformFeedbackTarget target, int feedback)
    {
        if (target == TransformFeedbackTarget.TransformFeedback) StateCache.Current.BindTransformFeedback(feedback);
        else GL.BindTransformFeedback(target, feedback);
    }
    #endregion

    #region Resource retirement
    /// <summary>Preserves texture deletion's implicit unbinding in cached snapshots.</summary>
    public static void DeleteTexture(int texture) => StateCache.Current.DeleteTexture(texture);
    /// <summary>Preserves buffer deletion's effect on generic and indexed bindings.</summary>
    public static void DeleteBuffer(int buffer) => StateCache.Current.DeleteBuffer(buffer);
    /// <summary>Preserves bulk buffer deletion and its implicit binding changes.</summary>
    public static void DeleteBuffers(int count, ref int buffers) => StateCache.Current.DeleteBuffers(count, ref buffers);
    /// <summary>Invalidates sampler bindings after engine retirement.</summary>
    public static void DeleteSampler(int sampler) => StateCache.Current.DeleteSampler(sampler);
    /// <summary>Invalidates read/draw framebuffer bindings after engine retirement.</summary>
    public static void DeleteFramebuffer(int framebuffer) => StateCache.Current.DeleteFramebuffer(framebuffer);
    /// <summary>Invalidates vertex-array-owned associations after engine retirement.</summary>
    public static void DeleteVertexArray(int array) => StateCache.Current.DeleteVertexArray(array);
    /// <summary>Preserves deferred native program deletion and invalidates executable knowledge.</summary>
    public static void DeleteProgram(int program) => StateCache.Current.DeleteProgram(program);
    #endregion
    #endregion
}
