using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Provides signature-compatible replacements for engine OpenGL state calls.</summary>
/// <remarks>Only engine callers are transpiled; these adapters and cache native calls are never patched.</remarks>
internal static partial class EngineStateCalls
{
    #region Public API
    #region Fixed function
    /// <summary>Preserves the exact native ClipControl overload and reconciles affected knowledge.</summary>
    public static void ClipControl(ClipOrigin origin, ClipDepthMode depth)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
        bool accepted = Enum.IsDefined(origin) && Enum.IsDefined(depth);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
        try { GL.ClipControl(origin, depth); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ClipControl); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalClipControl(origin, depth);
    }
    /// <summary>Preserves the exact native PointParameter overload and reconciles affected knowledge.</summary>
    public static void PointParameter(PointParameterName parameter, int value)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin);
        bool accepted = parameter == PointParameterName.PointSpriteCoordOrigin && Enum.IsDefined((PointSpriteCoordOriginParameter)value);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin);
        try { GL.PointParameter(parameter, value); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPointParameter(parameter, value);
    }

    /// <summary>Preserves the exact native Enable overload and reconciles affected knowledge.</summary>
    public static void Enable(EnableCap cap)
    {
        var cache = CurrentCache;
        bool accepted = OpenGlStateCache.AcceptsExternalCapability(cap);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        if (accepted) AdmitCapability(cache, cap);
        try { GL.Enable(cap); }
        catch { if (accepted && cache.OwnsKnowledgeFor(operationContext)) AdmitCapability(cache, cap); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCapability(cap, true);
    }
    /// <summary>Preserves the exact native Disable overload and reconciles affected knowledge.</summary>
    public static void Disable(EnableCap cap)
    {
        var cache = CurrentCache;
        bool accepted = OpenGlStateCache.AcceptsExternalCapability(cap);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        if (accepted) AdmitCapability(cache, cap);
        try { GL.Disable(cap); }
        catch { if (accepted && cache.OwnsKnowledgeFor(operationContext)) AdmitCapability(cache, cap); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCapability(cap, false);
    }
    /// <summary>Preserves the exact native Enable overload and reconciles affected knowledge.</summary>
    public static void Enable(IndexedEnableCap cap, int index)
    {
        var cache = CurrentCache;
        bool scissor = (int)cap == (int)EnableCap.ScissorTest && index == 0;
        if (cap == IndexedEnableCap.Blend) cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Enabled, index: index);
        else if (scissor) cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ScissorEnabled);
        else cache.RejectUnsupportedBoundaryMutation();
        bool accepted = scissor || cap == IndexedEnableCap.Blend && index >= 0 && index < cache.MaxDrawBuffers;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        if (accepted) AdmitCapability(cache, cap, index);
        try { GL.Enable(cap, index); }
        catch { if (accepted && cache.OwnsKnowledgeFor(operationContext)) AdmitCapability(cache, cap, index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCapability(cap, index, true);
    }
    /// <summary>Preserves the exact native Disable overload and reconciles affected knowledge.</summary>
    public static void Disable(IndexedEnableCap cap, int index)
    {
        var cache = CurrentCache;
        bool scissor = (int)cap == (int)EnableCap.ScissorTest && index == 0;
        if (cap == IndexedEnableCap.Blend) cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Enabled, index: index);
        else if (scissor) cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ScissorEnabled);
        else cache.RejectUnsupportedBoundaryMutation();
        bool accepted = scissor || cap == IndexedEnableCap.Blend && index >= 0 && index < cache.MaxDrawBuffers;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        if (accepted) AdmitCapability(cache, cap, index);
        try { GL.Disable(cap, index); }
        catch { if (accepted && cache.OwnsKnowledgeFor(operationContext)) AdmitCapability(cache, cap, index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCapability(cap, index, false);
    }
    /// <summary>Preserves the exact native DepthFunc overload and reconciles affected knowledge.</summary>
    public static void DepthFunc(DepthFunction function)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(depth: DepthStateKnowledge.Comparison);
        bool accepted = Enum.IsDefined(function);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.Comparison);
        try { GL.DepthFunc(function); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.Comparison); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalDepthFunc(function);
    }
    /// <summary>Preserves the exact native DepthMask overload and reconciles affected knowledge.</summary>
    public static void DepthMask(bool enabled)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(depth: DepthStateKnowledge.WriteEnabled);
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.WriteEnabled);
        try { GL.DepthMask(enabled); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.WriteEnabled); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalDepthMask(enabled);
    }
    /// <summary>Preserves the exact native BlendFunc overload and reconciles affected knowledge.</summary>
    public static void BlendFunc(BlendingFactor source, BlendingFactor destination)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors);
        bool accepted = OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc((BlendingFactorSrc)source, (BlendingFactorDest)destination, (BlendingFactorSrc)source, (BlendingFactorDest)destination));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
        try { GL.BlendFunc(source, destination); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendFunc(source, destination);
    }
    /// <summary>Preserves the exact native BlendFunc overload and reconciles affected knowledge.</summary>
    public static void BlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors, index: index);
        bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(source, destination, (BlendingFactorSrc)source, (BlendingFactorDest)destination)));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);
        try { GL.BlendFunc(index, source, destination); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendFunc(index, source, destination);
    }
    /// <summary>Preserves the exact native BlendFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors);
        bool accepted = OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
        try { GL.BlendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
    }
    /// <summary>Preserves the exact native BlendFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors, index: index);
        bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha)));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);
        try { GL.BlendFuncSeparate(index, sourceRgb, destinationRgb, sourceAlpha, destinationAlpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendFuncSeparate(index, sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
    }
    /// <summary>Preserves the exact native ColorMask overload and reconciles affected knowledge.</summary>
    public static void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.WriteMask);
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask);
        try { GL.ColorMask(red, green, blue, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalColorMask(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native ColorMask overload and reconciles affected knowledge.</summary>
    public static void ColorMask(int index, bool red, bool green, bool blue, bool alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.WriteMask, index: index);
        bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (true);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask, index: index);
        try { GL.ColorMask(index, red, green, blue, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask, index: index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalColorMask(index, red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native Viewport overload and reconciles affected knowledge.</summary>
    public static void Viewport(int x, int y, int width, int height)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(dynamic: DynamicDrawStateKnowledge.Viewport);
        bool accepted = width >= 0 && height >= 0;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Viewport);
        try { GL.Viewport(x, y, width, height); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Viewport); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalViewport(x, y, width, height);
    }
    /// <summary>Preserves the exact native ClearColor overload and reconciles affected knowledge.</summary>
    public static void ClearColor(float red, float green, float blue, float alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(clearColor: true);
        bool accepted = float.IsFinite(red) && float.IsFinite(green) && float.IsFinite(blue) && float.IsFinite(alpha);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue) || !float.IsFinite(alpha)) cache.ForgetExternalStateMutation(clearColor: true);
        try { GL.ClearColor(red, green, blue, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(clearColor: true); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalClearColor(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native LineWidth overload and reconciles affected knowledge.</summary>
    public static void LineWidth(float width)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
        bool accepted = float.IsFinite(width) && width > 0;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(width)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
        try { GL.LineWidth(width); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalLineWidth(width);
    }
    /// <summary>Preserves the exact native PointSize overload and reconciles affected knowledge.</summary>
    public static void PointSize(float size)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PointSize);
        bool accepted = float.IsFinite(size) && size > 0;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(size)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSize);
        try { GL.PointSize(size); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSize); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPointSize(size);
    }
    /// <summary>Preserves the exact native PixelStore overload and reconciles affected knowledge.</summary>
    public static void PixelStore(PixelStoreParameter parameter, int value) => OpenGLPixelStoreCalls.SetPixelStore(CurrentCache, parameter, value);
    /// <summary>Preserves the exact native PixelStore overload and reconciles affected knowledge.</summary>
    public static void PixelStore(PixelStoreParameter parameter, float value) => OpenGLPixelStoreCalls.SetPixelStore(CurrentCache, parameter, value);
    /// <summary>Preserves the exact native PatchParameter overload and reconciles affected knowledge.</summary>
    public static void PatchParameter(PatchParameterInt parameter, int value)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
        bool accepted = parameter == PatchParameterInt.PatchVertices && value > 0 && value <= GpuSupport.Graphics.MaxPatchVertices;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
        try { GL.PatchParameter(parameter, value); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPatchParameter(parameter, value);
    }
    /// <summary>Preserves the exact native ProvokingVertex overload and reconciles affected knowledge.</summary>
    public static void ProvokingVertex(ProvokingVertexMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex);
        bool accepted = Enum.IsDefined(mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex);
        try { GL.ProvokingVertex(mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalProvokingVertex(mode);
    }
    #endregion

    #region Bindings
    /// <summary>Preserves the exact native UseProgram overload and reconciles affected knowledge.</summary>
    public static void UseProgram(int program) => OpenGLProgramCalls.ExecuteCoreUseProgram(program);
    /// <summary>Preserves the exact native BindVertexArray overload and reconciles affected knowledge.</summary>
    public static void BindVertexArray(int array) => OpenGLVertexCalls.ExecuteCoreBindVertexArray(array);
    /// <summary>Preserves the exact native BindFramebuffer overload and reconciles affected knowledge.</summary>
    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => OpenGLFramebufferSelection.ExternalBind(target, framebuffer);
    /// <summary>Preserves the exact native BindRenderbuffer overload and reconciles affected knowledge.</summary>
    public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) => OpenGLImageBindingCalls.BindRenderbuffer(target, renderbuffer);
    /// <summary>Preserves the exact native ActiveTexture overload and reconciles affected knowledge.</summary>
    public static void ActiveTexture(TextureUnit texture) => OpenGLImageBindingCalls.ActiveTexture(texture);
    /// <summary>Preserves the exact native BindTexture overload and reconciles affected knowledge.</summary>
    public static void BindTexture(TextureTarget target, int texture) => OpenGLImageBindingCalls.BindTexture(target, texture);
    /// <summary>Preserves the exact native BindSampler overload and reconciles affected knowledge.</summary>
    public static void BindSampler(int unit, int sampler) => OpenGLImageBindingCalls.BindSampler(unit, sampler);
    /// <summary>Preserves the exact native BindBuffer overload and reconciles affected knowledge.</summary>
    public static void BindBuffer(BufferTarget target, int buffer) => OpenGLBufferBindingCalls.ExecuteCoreBindBuffer(target, buffer);
    /// <summary>Preserves the exact native BindBufferBase overload and reconciles affected knowledge.</summary>
    public static void BindBufferBase(BufferRangeTarget target, int index, int buffer) => OpenGLBufferBindingCalls.BindBufferBase(target, index, buffer);
    /// <summary>Preserves the exact native BindBufferRange overload and reconciles affected knowledge.</summary>
    public static void BindBufferRange(BufferRangeTarget target, int index, int buffer, IntPtr offset, IntPtr size) =>
        OpenGLBufferBindingCalls.BindBufferRange(target, index, buffer, offset, size);
    /// <summary>Preserves the exact native BindImageTexture overload and reconciles affected knowledge.</summary>
    public static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format) =>
        OpenGLImageBindingCalls.BindImageTexture(unit, texture, level, layered, layer, access, format);
    /// <summary>Preserves the exact native BindProgramPipeline overload and reconciles affected knowledge.</summary>
    public static void BindProgramPipeline(int pipeline) => OpenGLProgramCalls.ExecuteCoreBindProgramPipeline(pipeline);

    #endregion

    #region Resource retirement
    /// <summary>Preserves the exact native DeleteTexture overload and reconciles affected knowledge.</summary>
    public static void DeleteTexture(int texture) => OpenGLImageDeletionCalls.Core.DeleteTexture(texture);
    /// <summary>Preserves the exact native DeleteBuffer overload and reconciles affected knowledge.</summary>
    public static void DeleteBuffer(int buffer) => OpenGLBufferDeletionCalls.Core.DeleteBuffer(buffer);
    /// <summary>Preserves the exact native DeleteBuffers overload and reconciles affected knowledge.</summary>
    public static void DeleteBuffers(int count, ref int buffers) => OpenGLBufferDeletionCalls.Core.DeleteBuffers(count, ref buffers);
    /// <summary>Preserves the exact native DeleteSampler overload and reconciles affected knowledge.</summary>
    public static void DeleteSampler(int sampler) => OpenGLImageDeletionCalls.Core.DeleteSampler(sampler);
    /// <summary>Preserves the exact native DeleteRenderbuffer overload and reconciles affected knowledge.</summary>
    public static void DeleteRenderbuffer(int renderbuffer) => OpenGLImageDeletionCalls.Core.DeleteRenderbuffer(renderbuffer);
    #endregion
    #endregion
    #region Private
    /// <summary>Establishes engine context authority before hooks installed during early startup use the cache.</summary>
    internal static OpenGlStateCache CurrentCache
    {
        get
        {
            // StartPre installs hooks before StartClientSide; menu rendering already has a native window.
            // Existing registrations retain their generation, including explicitly owned headless contexts.
            if (Integration.RenderContextRegistry.Current().Generation == 0)
                Integration.EngineRenderContext.RegisterCurrent();
            // Static limit accessors also participate in argument-derived effects. Refresh
            // their existing capability snapshot once when the registered lifetime changes.
            GpuSupport.EnsureCurrentContext();
            var identity = Integration.RenderContextRegistry.CaptureResourceContext();
            var cache = OpenGlStateCache.Current;
            cache.SelectRHIContext(identity.Handle, identity.Generation == 0 ? default : identity);
            return cache;
        }
    }
    #endregion
}
