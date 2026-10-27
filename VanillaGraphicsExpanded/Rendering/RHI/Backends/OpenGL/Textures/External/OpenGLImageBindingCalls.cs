using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external selectors without managed suppression or native success probes.</summary>
internal static class OpenGLImageBindingCalls
{
    #region Internal API
    /// <summary>Forwards the native image selector exactly once and retains no unproven acceptance in the cache.</summary>
    internal static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool represented = unit >= 0 && unit < GpuSupport.MaxImageUnits;
        if (represented) cache.ForgetImageBinding(unit);
        try { GL.BindImageTexture(unit, texture, level, layered, layer, access, format); }
        finally { if (represented) cache.ForgetImageBinding(unit); }
    }

    /// <summary>Executes the original active-unit overload and withdraws uncertain selection before dispatch.</summary>
    internal static void ActiveTexture(TextureUnit texture)
    {
        var cache = EngineStateCalls.CurrentCache;
        cache.ActiveTextureKnowledge = null;
        GL.ActiveTexture(texture);
        int unit = (int)texture - (int)TextureUnit.Texture0;
        if (unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits) cache.ActiveTextureKnowledge = unit;
    }
    /// <summary>Executes even a duplicate external binding, leaving uncertain object acceptance for lazy acquisition.</summary>
    internal static void BindTexture(TextureTarget target, int texture)
    {
        var cache = EngineStateCalls.CurrentCache;
        cache.ForgetImageBinding(target, cache.ActiveTextureKnowledge);
        GL.BindTexture(target, texture);
        cache.RecordTextureBind();
    }
    /// <summary>Preserves native unit/name errors instead of applying semantic resource validation.</summary>
    internal static void BindSampler(int unit, int sampler)
    {
        var cache = EngineStateCalls.CurrentCache;
        if (unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits) cache.SetSamplerKnowledge(unit, null);
        GL.BindSampler(unit, sampler);
        cache.RecordSamplerBind();
    }
    /// <summary>Preserves the supplied target and native errors while withdrawing only applicable selector knowledge.</summary>
    internal static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
    {
        var cache = EngineStateCalls.CurrentCache;
        if (target == RenderbufferTarget.Renderbuffer) cache.RenderbufferKnowledge = null;
        GL.BindRenderbuffer(target, renderbuffer);
    }
    #endregion
}
