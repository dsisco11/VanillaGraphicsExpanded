using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external selectors without managed suppression or native success probes.</summary>
internal static class OpenGLImageBindingCalls
{
    #region Internal API
    /// <summary>Propagates a legal established image assignment without inspecting texture contents or acquiring descriptors.</summary>
    internal static void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool represented = unit >= 0 && unit < GpuSupport.MaxImageUnits;
        bool arguments = represented && level >= 0 && layer >= 0
            && access is TextureAccess.ReadOnly or TextureAccess.WriteOnly or TextureAccess.ReadWrite
            && IsImageFormat(format);
        bool established = arguments && cache.HasEstablishedNativeIdentity(GpuResourceKind.Texture, texture);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (arguments) cache.ForgetImageBinding(unit);
        try { GL.BindImageTexture(unit, texture, level, layered, layer, access, format); }
        catch { if (represented && cache.OwnsKnowledgeFor(origin)) cache.ForgetImageBinding(unit); throw; }
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishImageBinding(unit, new(texture, level, layered, layer, access, format));
    }

    /// <summary>Propagates a legal active-unit selection and preserves known state for native rejection.</summary>
    internal static void ActiveTexture(TextureUnit texture)
    {
        var cache = EngineStateCalls.CurrentCache;
        int unit = (int)texture - (int)TextureUnit.Texture0;
        bool valid = unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.ActiveTextureKnowledge = null;
        GL.ActiveTexture(texture);
        if (valid && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.ActiveTextureKnowledge = unit;
    }

    /// <summary>Publishes established target/name pairs while leaving genuinely unseen object identity initially unknown.</summary>
    internal static void BindTexture(TextureTarget target, int texture)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool targetValid = OpenGlStateCache.RepresentsTextureBinding(target);
        int unit = targetValid ? cache.GetActiveTextureUnit() : -1;
        bool established = texture == 0;
        if (targetValid && texture != 0)
        {
            established = cache.TryGetCachedBoundTexture(target, unit, out int prior) && prior == texture;
            var manager = GpuResourceManagerSystem.CurrentManager;
            if (!established && manager?.CanObserveNativeMetadata == true
                && manager.FindObservedResource(GpuResourceKind.Texture, texture) is GpuResourceMetadataEntry<GpuTextureMetadata> { IsUsable: true } entry)
                established = OpenGLImageValues.Dimension(target) == entry.Metadata.Target;
        }
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (targetValid) cache.ForgetImageBinding(target, unit);
        GL.BindTexture(target, texture);
        cache.RecordTextureBind();
        if (targetValid && established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.ImageBindings(unit)[target] = texture;
    }

    /// <summary>Preserves exact native execution and commits realized sampler assignments to the existing slot cache.</summary>
    internal static void BindSampler(int unit, int sampler)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool valid = unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits;
        bool established = valid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Sampler, sampler);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.SetSamplerKnowledge(unit, null);
        GL.BindSampler(unit, sampler);
        cache.RecordSamplerBind();
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.SetSamplerKnowledge(unit, sampler);
    }

    /// <summary>Propagates a realized renderbuffer selection without converting native errors to managed validation errors.</summary>
    internal static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool valid = target == RenderbufferTarget.Renderbuffer;
        bool established = valid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Renderbuffer, renderbuffer);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.RenderbufferKnowledge = null;
        GL.BindRenderbuffer(target, renderbuffer);
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.RenderbufferKnowledge = renderbuffer;
    }
    #endregion

    #region Private
    /// <summary>Checks the core image-format domain through existing semantic format classification.</summary>
    private static bool IsImageFormat(SizedInternalFormat format)
    {
        try
        {
            var semantic = OpenGLImageValues.Format((PixelInternalFormat)format);
            return ImageFormatCompatibility.Matches(semantic, semantic);
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    #endregion
}
