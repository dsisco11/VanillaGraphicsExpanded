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
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
        GL.ClipControl(origin, depth);
    }
    /// <summary>Preserves the exact native PointParameter overload and reconciles affected knowledge.</summary>
    public static void PointParameter(PointParameterName parameter, int value)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin);
        GL.PointParameter(parameter, value);
    }

    /// <summary>Preserves the exact native Enable overload and reconciles affected knowledge.</summary>
    public static void Enable(EnableCap cap)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        AdmitCapability(cache, cap);
        GL.Enable(cap);
    }
    /// <summary>Preserves the exact native Disable overload and reconciles affected knowledge.</summary>
    public static void Disable(EnableCap cap)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        AdmitCapability(cache, cap);
        GL.Disable(cap);
    }
    /// <summary>Preserves the exact native Enable overload and reconciles affected knowledge.</summary>
    public static void Enable(IndexedEnableCap cap, int index)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        AdmitCapability(cache, cap, index);
        GL.Enable(cap, index);
    }
    /// <summary>Preserves the exact native Disable overload and reconciles affected knowledge.</summary>
    public static void Disable(IndexedEnableCap cap, int index)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        AdmitCapability(cache, cap, index);
        GL.Disable(cap, index);
    }
    /// <summary>Preserves the exact native DepthFunc overload and reconciles affected knowledge.</summary>
    public static void DepthFunc(DepthFunction function)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.Comparison);
        GL.DepthFunc(function);
    }
    /// <summary>Preserves the exact native DepthMask overload and reconciles affected knowledge.</summary>
    public static void DepthMask(bool enabled)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.WriteEnabled);
        GL.DepthMask(enabled);
    }
    /// <summary>Preserves the exact native BlendFunc overload and reconciles affected knowledge.</summary>
    public static void BlendFunc(BlendingFactor source, BlendingFactor destination)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
        GL.BlendFunc(source, destination);
    }
    /// <summary>Preserves the exact native BlendFunc overload and reconciles affected knowledge.</summary>
    public static void BlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);
        GL.BlendFunc(index, source, destination);
    }
    /// <summary>Preserves the exact native BlendFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
        GL.BlendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
    }
    /// <summary>Preserves the exact native BlendFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);
        GL.BlendFuncSeparate(index, sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
    }
    /// <summary>Preserves the exact native ColorMask overload and reconciles affected knowledge.</summary>
    public static void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask);
        GL.ColorMask(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native ColorMask overload and reconciles affected knowledge.</summary>
    public static void ColorMask(int index, bool red, bool green, bool blue, bool alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask, index: index);
        GL.ColorMask(index, red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native Viewport overload and reconciles affected knowledge.</summary>
    public static void Viewport(int x, int y, int width, int height)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Viewport);
        GL.Viewport(x, y, width, height);
    }
    /// <summary>Preserves the exact native ClearColor overload and reconciles affected knowledge.</summary>
    public static void ClearColor(float red, float green, float blue, float alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(clearColor: true);
        GL.ClearColor(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native LineWidth overload and reconciles affected knowledge.</summary>
    public static void LineWidth(float width)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
        GL.LineWidth(width);
    }
    /// <summary>Preserves the exact native PointSize overload and reconciles affected knowledge.</summary>
    public static void PointSize(float size)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSize);
        GL.PointSize(size);
    }
    /// <summary>Preserves the exact native PixelStore overload and reconciles affected knowledge.</summary>
    public static void PixelStore(PixelStoreParameter parameter, int value) => OpenGLPixelStoreCalls.SetPixelStore(CurrentCache, parameter, value);
    /// <summary>Preserves the exact native PixelStore overload and reconciles affected knowledge.</summary>
    public static void PixelStore(PixelStoreParameter parameter, float value) => OpenGLPixelStoreCalls.SetPixelStore(CurrentCache, parameter, value);
    /// <summary>Preserves the exact native PatchParameter overload and reconciles affected knowledge.</summary>
    public static void PatchParameter(PatchParameterInt parameter, int value)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
        GL.PatchParameter(parameter, value);
    }
    /// <summary>Preserves the exact native ProvokingVertex overload and reconciles affected knowledge.</summary>
    public static void ProvokingVertex(ProvokingVertexMode mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex);
        GL.ProvokingVertex(mode);
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
            return OpenGlStateCache.Current;
        }
    }
    #endregion
}
