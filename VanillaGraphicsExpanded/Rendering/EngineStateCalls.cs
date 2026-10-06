using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides signature-compatible replacements for engine OpenGL state calls.</summary>
/// <remarks>Only engine callers are transpiled; these adapters and cache native calls are never patched.</remarks>
internal static class EngineStateCalls
{
    #region Public API
    #region Fixed function
    /// <summary>Observes native alpha comparison, including native reference clamping.</summary>
    public static void AlphaFunc(AlphaFunction function, float reference) => CurrentCache.SetAlphaFunction(function, reference);
    /// <summary>Observes clip coordinate conventions.</summary>
    public static void ClipControl(ClipOrigin origin, ClipDepthMode depth) => CurrentCache.SetClipControl(origin, depth);
    /// <summary>Observes the unsigned native stipple pattern.</summary>
    public static void LineStipple(int factor, ushort pattern) => CurrentCache.SetLineStipple(factor, pattern);
    /// <summary>Preserves the signed native overload's 16-bit pattern.</summary>
    public static void LineStipple(int factor, short pattern) => CurrentCache.SetLineStipple(factor, unchecked((ushort)pattern));
    /// <summary>Observes point coordinate orientation while forwarding unrelated point parameters.</summary>
    public static void PointParameter(PointParameterName parameter, int value)
    {
        if (parameter == PointParameterName.PointSpriteCoordOrigin) CurrentCache.SetPointSpriteOrigin((PointSpriteCoordOriginParameter)value);
        else { CurrentCache.RejectUnsupportedBoundaryMutation(); GL.PointParameter(parameter, value); }
    }
    /// <summary>Preserves engine pixel unpack semantics and withdraws only the changed bitmap's knowledge.</summary>
    public static void PolygonStipple(byte[] pattern)
    {
        CurrentCache.UploadEnginePolygonStipple(pattern);
    }

    /// <summary>Enables a cached capability or forwards an unsupported capability unchanged.</summary>
    public static void Enable(EnableCap cap) => CurrentCache.SetCapability(cap, true);
    /// <summary>Disables a cached capability or forwards an unsupported capability unchanged.</summary>
    public static void Disable(EnableCap cap) => CurrentCache.SetCapability(cap, false);
    /// <summary>Enables an indexed capability using its exact output index.</summary>
    public static void Enable(IndexedEnableCap cap, int index) => CurrentCache.SetIndexedCapability(cap, index, true);
    /// <summary>Disables an indexed capability using its exact output index.</summary>
    public static void Disable(IndexedEnableCap cap, int index) => CurrentCache.SetIndexedCapability(cap, index, false);
    /// <summary>Publishes the depth comparison.</summary>
    public static void DepthFunc(DepthFunction function) => CurrentCache.SetDepthFunc(function);
    /// <summary>Publishes the depth write mask.</summary>
    public static void DepthMask(bool enabled) => CurrentCache.SetDepthWriteMask(enabled);
    /// <summary>Adapts the engine's combined-factor overload without changing enum values.</summary>
    public static void BlendFunc(BlendingFactor source, BlendingFactor destination) =>
        CurrentCache.SetBlendFunc(new((BlendingFactorSrc)source, (BlendingFactorDest)destination,
            (BlendingFactorSrc)source, (BlendingFactorDest)destination));
    /// <summary>Applies one output's factors to both RGB and alpha.</summary>
    public static void BlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination) =>
        CurrentCache.SetBlendFuncIndexed(index, new(source, destination, source, destination));
    /// <summary>Publishes independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        CurrentCache.SetBlendFunc(new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes one output's independent RGB and alpha blend factors.</summary>
    public static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) =>
        CurrentCache.SetBlendFuncIndexed(index, new(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
    /// <summary>Publishes the global color write mask.</summary>
    public static void ColorMask(bool red, bool green, bool blue, bool alpha) =>
        CurrentCache.SetColorMask(GlColorMask.FromRgba(red, green, blue, alpha));
    /// <summary>Publishes one draw output's write mask.</summary>
    public static void ColorMask(int index, bool red, bool green, bool blue, bool alpha) =>
        CurrentCache.SetColorMaskIndexed(index, GlColorMask.FromRgba(red, green, blue, alpha));
    /// <summary>Adapts the engine integer viewport signature to declared dynamic state.</summary>
    public static void Viewport(int x, int y, int width, int height) =>
        CurrentCache.ApplyDynamic(new Pipeline.State.DynamicDrawState { X = x, Y = y, Width = width, Height = height });
    /// <summary>Publishes the clear-operation color without treating it as pipeline state.</summary>
    public static void ClearColor(float red, float green, float blue, float alpha) =>
        CurrentCache.SetClearColor(red, green, blue, alpha);
    /// <summary>Publishes line width without approximate-value suppression.</summary>
    public static void LineWidth(float width) => CurrentCache.SetLineWidth(width);
    /// <summary>Publishes point size without approximate-value suppression.</summary>
    public static void PointSize(float size) => CurrentCache.SetPointSize(size);
    /// <summary>Publishes one integer pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, int value) => CurrentCache.SetPixelStore(parameter, value);
    /// <summary>Publishes one floating-point pixel-store field.</summary>
    public static void PixelStore(PixelStoreParameter parameter, float value) => CurrentCache.SetPixelStore(parameter, value);
    /// <summary>Publishes patch vertex count while forwarding any unknown enum unchanged.</summary>
    public static void PatchParameter(PatchParameterInt parameter, int value)
    {
        if (parameter == PatchParameterInt.PatchVertices) CurrentCache.SetPatchVertices(value);
        else
        {
            CurrentCache.RejectUnsupportedBoundaryMutation();
            GL.PatchParameter(parameter, value);
        }
    }
    /// <summary>Updates the provoking convention used by cached tessellation checks.</summary>
    public static void ProvokingVertex(ProvokingVertexMode mode) => CurrentCache.SetProvokingVertex(mode);
    #endregion

    #region Bindings
    /// <summary>Records a native executable switch while retaining the surrounding engine shader lifecycle.</summary>
    public static void UseProgram(int program) => CurrentCache.UseProgram(program);
    /// <summary>Publishes the selected vertex array and its element-buffer association.</summary>
    public static void BindVertexArray(int array) => CurrentCache.BindVertexArray(array);
    /// <summary>Preserves combined and independent read/draw framebuffer targets.</summary>
    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => CurrentCache.BindFramebuffer(target, framebuffer);
    /// <summary>Publishes a renderbuffer binding, preserving unsupported enum behavior.</summary>
    public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
    {
        if (target == RenderbufferTarget.Renderbuffer) CurrentCache.BindRenderbuffer(renderbuffer);
        else GL.BindRenderbuffer(target, renderbuffer);
    }
    /// <summary>Converts the native texture-unit enum to the cache's zero-based unit.</summary>
    public static void ActiveTexture(TextureUnit texture) => CurrentCache.ActiveTexture((int)texture - (int)TextureUnit.Texture0);
    /// <summary>Binds on the native active unit without querying, reselecting it, or altering sampler ownership.</summary>
    public static void BindTexture(TextureTarget target, int texture) =>
        CurrentCache.BindTextureOnActiveUnit(target, texture);
    /// <summary>Publishes a sampler independently of texture binding.</summary>
    public static void BindSampler(int unit, int sampler) => CurrentCache.BindSampler(unit, sampler);
    /// <summary>Publishes generic and vertex-array-owned buffer bindings.</summary>
    public static void BindBuffer(BufferTarget target, int buffer) => CurrentCache.BindBuffer(target, buffer);
    /// <summary>Publishes an indexed whole-buffer binding and its generic binding side effect.</summary>
    public static void BindBufferBase(BufferRangeTarget target, int index, int buffer) => CurrentCache.BindBufferBase(target, index, buffer);
    /// <summary>Publishes an indexed range without truncating native offsets or sizes.</summary>
    public static void BindBufferRange(BufferRangeTarget target, int index, int buffer, IntPtr offset, IntPtr size) =>
        CurrentCache.BindBufferRange(target, index, buffer, offset, size);
    /// <summary>Publishes an image unit including the complete image-view selection.</summary>
    public static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format) =>
        CurrentCache.BindImageTexture(unit, texture, level, layered, layer, access, format);
    /// <summary>Publishes a separable shader pipeline binding.</summary>
    public static void BindProgramPipeline(int pipeline) => CurrentCache.BindProgramPipeline(pipeline);
    /// <summary>Publishes transform-feedback binding without changing unsupported enum behavior.</summary>
    public static void BindTransformFeedback(TransformFeedbackTarget target, int feedback)
    {
        if (target == TransformFeedbackTarget.TransformFeedback) CurrentCache.BindTransformFeedback(feedback);
        else GL.BindTransformFeedback(target, feedback);
    }
    #endregion

    #region Resource retirement
    /// <summary>Preserves texture deletion's implicit unbinding in cached snapshots.</summary>
    public static void DeleteTexture(int texture) => CurrentCache.DeleteTexture(texture);
    /// <summary>Preserves buffer deletion's effect on generic and indexed bindings.</summary>
    public static void DeleteBuffer(int buffer) => CurrentCache.DeleteBuffer(buffer);
    /// <summary>Preserves bulk buffer deletion and its implicit binding changes.</summary>
    public static void DeleteBuffers(int count, ref int buffers) => CurrentCache.DeleteBuffers(count, ref buffers);
    /// <summary>Invalidates sampler bindings after engine retirement.</summary>
    public static void DeleteSampler(int sampler) => CurrentCache.DeleteSampler(sampler);
    /// <summary>Invalidates read/draw framebuffer bindings after engine retirement.</summary>
    public static void DeleteFramebuffer(int framebuffer) => CurrentCache.DeleteFramebuffer(framebuffer);
    /// <summary>Invalidates vertex-array-owned associations after engine retirement.</summary>
    public static void DeleteVertexArray(int array) => CurrentCache.DeleteVertexArray(array);
    /// <summary>Preserves deferred native program deletion and invalidates executable knowledge.</summary>
    public static void DeleteProgram(int program) => CurrentCache.DeleteProgram(program);
    #endregion
    #endregion
    #region Private
    /// <summary>Establishes engine context authority before hooks installed during early startup use the cache.</summary>
    private static StateCache CurrentCache
    {
        get
        {
            // StartPre installs hooks before StartClientSide; menu rendering already has a native window.
            // Existing registrations retain their generation, including explicitly owned headless contexts.
            if (Integration.RenderContextRegistry.Current().Generation == 0)
                Integration.EngineRenderContext.RegisterCurrent();
            return StateCache.Current;
        }
    }
    #endregion
}
