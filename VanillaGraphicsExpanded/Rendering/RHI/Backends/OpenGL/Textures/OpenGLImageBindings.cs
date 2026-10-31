using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes existing selector/restoration contracts using the cache's single knowledge store.</summary>
internal static class OpenGLImageBindings
{
    #region Internal API
    #region Texture selectors
    /// <summary>Selects an active unit, retaining the existing observable selector contract.</summary>
    internal static void ActiveTexture(OpenGlStateCache cache, int unit)
    {
        var backend = RHICommandContext.Current;
        if (unit < 0 || unit >= backend.Capabilities.MaxCombinedTextureImageUnits) throw new ArgumentOutOfRangeException(nameof(unit));
        if (cache.ActiveTextureKnowledge == unit) return;
        cache.ActiveTextureKnowledge = null;
        ulong stamp = cache.ActiveTextureWriteStamp;
        OpenGLDriverCalls.ActiveTexture(Native.TextureUnit.Texture0 + unit);
        backend.RequireContext();
        if (stamp != cache.ActiveTextureWriteStamp)
        {
            cache.ActiveTextureKnowledge = null;
            throw new InvalidOperationException("Active texture selection was interrupted by an overlapping native callback.");
        }
        cache.ActiveTextureKnowledge = unit;
    }
    /// <summary>Changes a target on the active unit without guessing an unknown selector.</summary>
    internal static void BindTextureOnActiveUnit(OpenGlStateCache cache, TextureTarget target, int texture)
    {
        var backend = RHICommandContext.Current;
        int? unit = cache.ActiveTextureKnowledge;
        if (unit is int known && cache.TryGetCachedBoundTexture(target, known, out int previous) && previous == texture) return;
        cache.ForgetImageBinding(target, unit);
        ulong stamp = unit is { } selectedUnit ? cache.TextureWriteStamp(selectedUnit) : 0;
        ulong selectorStamp = cache.ActiveTextureWriteStamp;
        OpenGLDriverCalls.BindTexture((Native.TextureTarget)target, texture);
        cache.RecordTextureBind();
        backend.RequireContext();
        if (selectorStamp != cache.ActiveTextureWriteStamp)
        {
            // The native write may have used an intermediate unit, not merely either observed endpoint.
            // Withdraw this target domain only; other texture targets and binding domains remain known.
            cache.ForgetImageBinding(target, null);
            throw new InvalidOperationException("Bound texture publication was interrupted by an active-unit callback.");
        }
        // A backend selector can realize an externally generated reservation. Private
        // creation candidates have no registration until their single factory handoff.
        if (texture != 0 && RHIResourceManagerSystem.CurrentManager is { } manager)
            manager.RealizeObservedImage(GpuResourceKind.Texture, texture, OpenGLImageValues.Dimension(target), false);
        if (unit is int selected)
        {
            if (stamp != cache.TextureWriteStamp(selected))
            {
                cache.ForgetImageBinding(target, selected);
                throw new InvalidOperationException("Texture publication was interrupted by an overlapping native callback.");
            }
            cache.ImageBindings(selected)[target] = texture;
        }
    }
    /// <summary>Selects the requested unit and suppresses only an established equal target binding.</summary>
    internal static void BindTexture(OpenGlStateCache cache, TextureTarget target, int unit, int texture)
    {
        ActiveTexture(cache, unit);
        BindTextureOnActiveUnit(cache, target, texture);
    }
    /// <summary>Applies texture and optional sampler policy using existing selector ownership.</summary>
    internal static void BindTexture(OpenGlStateCache cache, TextureTarget target, int unit, int texture, GpuSampler? sampler)
    {
        BindTexture(cache, target, unit, texture);
        if (texture != 0 && sampler is not null)
        {
            _ = RHIResourceManager.RequireSamplerReferenceMetadata(sampler);
            BindSampler(cache, unit, sampler.SamplerId);
            return;
        }
        BindSampler(cache, unit, 0);
    }
    /// <summary>Clears only the selected target and its sampler policy.</summary>
    internal static void UnbindTexture(OpenGlStateCache cache, TextureTarget target, int unit) => BindTexture(cache, target, unit, 0, null);
    #endregion
    #region Sampler and renderbuffer selectors
    /// <summary>Binds a certified sampler name while retaining slot suppression.</summary>
    internal static void BindSampler(OpenGlStateCache cache, int unit, int sampler)
    {
        var backend = RHICommandContext.Current;
        if (unit < 0 || unit >= backend.Capabilities.MaxCombinedTextureImageUnits) throw new ArgumentOutOfRangeException(nameof(unit));
        if (cache.SamplerKnowledge(unit) == sampler) return;
        cache.SetSamplerKnowledge(unit, null);
        ulong stamp = cache.SamplerWriteStamp(unit);
        OpenGLDriverCalls.BindSampler(unit, sampler);
        cache.RecordSamplerBind();
        backend.RequireContext();
        if (stamp != cache.SamplerWriteStamp(unit))
        {
            cache.SetSamplerKnowledge(unit, null);
            throw new InvalidOperationException("Sampler publication was interrupted by an overlapping native callback.");
        }
        cache.SetSamplerKnowledge(unit, sampler);
    }
    /// <summary>Validates the owner before using managed equality for suppression.</summary>
    internal static void BindSampler(OpenGlStateCache cache, int unit, GpuSampler sampler)
    {
        _ = RHIResourceManager.RequireSamplerReferenceMetadata(sampler);
        BindSampler(cache, unit, sampler.SamplerId);
    }
    /// <summary>Clears an existing sampler slot.</summary>
    internal static void UnbindSampler(OpenGlStateCache cache, int unit) => BindSampler(cache, unit, 0);
    /// <summary>Changes renderbuffer selection after existing boundary admission.</summary>
    internal static void BindRenderbuffer(OpenGlStateCache cache, int renderbuffer)
    {
        var backend = RHICommandContext.Current;
        if (renderbuffer < 0) throw new ArgumentOutOfRangeException(nameof(renderbuffer));
        cache.RequireBoundaryResource(EPipelineState.RenderbufferBinding, renderbuffer);
        if (cache.RenderbufferKnowledge == renderbuffer) return;
        cache.RenderbufferKnowledge = null;
        OpenGLDriverCalls.BindRenderbuffer(Native.RenderbufferTarget.Renderbuffer, renderbuffer);
        backend.RequireContext();
        RHIResourceManagerSystem.CurrentManager?.RealizeObservedImage(GpuResourceKind.Renderbuffer, renderbuffer, implicitCreation: false);
        cache.RenderbufferKnowledge = renderbuffer;
    }
    /// <summary>Clears renderbuffer selection through the same boundary admission.</summary>
    internal static void UnbindRenderbuffer(OpenGlStateCache cache) => BindRenderbuffer(cache, 0);
    #endregion
    #endregion
}
