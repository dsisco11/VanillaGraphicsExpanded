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
    internal static void ActiveTexture(StateCache cache, int unit)
    {
        var backend = RHICommandContext.Current;
        if (unit < 0 || unit >= backend.Capabilities.MaxCombinedTextureImageUnits) throw new ArgumentOutOfRangeException(nameof(unit));
        if (cache.ActiveTextureKnowledge == unit) return;
        cache.ActiveTextureKnowledge = null;
        GL.ActiveTexture(Native.TextureUnit.Texture0 + unit);
        backend.RequireContext();
        cache.ActiveTextureKnowledge = unit;
    }
    /// <summary>Changes a target on the active unit without guessing an unknown selector.</summary>
    internal static void BindTextureOnActiveUnit(StateCache cache, TextureTarget target, int texture)
    {
        var backend = RHICommandContext.Current;
        int? unit = cache.ActiveTextureKnowledge;
        if (unit is int known && cache.TryGetCachedBoundTexture(target, known, out int previous) && previous == texture) return;
        cache.ForgetImageBinding(target, unit);
        GL.BindTexture((Native.TextureTarget)target, texture);
        cache.RecordTextureBind();
        backend.RequireContext();
        if (unit is int selected) cache.ImageBindings(selected)[target] = texture;
    }
    /// <summary>Selects the requested unit and suppresses only an established equal target binding.</summary>
    internal static void BindTexture(StateCache cache, TextureTarget target, int unit, int texture)
    {
        ActiveTexture(cache, unit);
        BindTextureOnActiveUnit(cache, target, texture);
    }
    /// <summary>Applies texture and optional sampler policy using existing selector ownership.</summary>
    internal static void BindTexture(StateCache cache, TextureTarget target, int unit, int texture, GpuSampler? sampler)
    {
        BindTexture(cache, target, unit, texture);
        if (texture != 0 && sampler is not null && sampler.TryBind(unit)) return;
        BindSampler(cache, unit, 0);
    }
    /// <summary>Clears only the selected target and its sampler policy.</summary>
    internal static void UnbindTexture(StateCache cache, TextureTarget target, int unit) => BindTexture(cache, target, unit, 0, null);
    #endregion
    #region Sampler and renderbuffer selectors
    /// <summary>Binds a certified sampler name while retaining slot suppression.</summary>
    internal static void BindSampler(StateCache cache, int unit, int sampler)
    {
        var backend = RHICommandContext.Current;
        if (unit < 0 || unit >= backend.Capabilities.MaxCombinedTextureImageUnits) throw new ArgumentOutOfRangeException(nameof(unit));
        if (cache.SamplerKnowledge(unit) == sampler) return;
        cache.SetSamplerKnowledge(unit, null);
        GL.BindSampler(unit, sampler);
        cache.RecordSamplerBind();
        backend.RequireContext();
        cache.SetSamplerKnowledge(unit, sampler);
    }
    /// <summary>Validates the owner before using managed equality for suppression.</summary>
    internal static void BindSampler(StateCache cache, int unit, GpuSampler sampler)
    {
        _ = GpuResourceManager.RequireSamplerReferenceMetadata(sampler);
        BindSampler(cache, unit, sampler.SamplerId);
    }
    /// <summary>Clears an existing sampler slot.</summary>
    internal static void UnbindSampler(StateCache cache, int unit) => BindSampler(cache, unit, 0);
    /// <summary>Changes renderbuffer selection after existing boundary admission.</summary>
    internal static void BindRenderbuffer(StateCache cache, int renderbuffer)
    {
        var backend = RHICommandContext.Current;
        if (renderbuffer < 0) throw new ArgumentOutOfRangeException(nameof(renderbuffer));
        cache.RequireBoundaryResource(EPipelineState.RenderbufferBinding, renderbuffer);
        if (cache.RenderbufferKnowledge == renderbuffer) return;
        cache.RenderbufferKnowledge = null;
        GL.BindRenderbuffer(Native.RenderbufferTarget.Renderbuffer, renderbuffer);
        backend.RequireContext();
        cache.RenderbufferKnowledge = renderbuffer;
    }
    /// <summary>Clears renderbuffer selection through the same boundary admission.</summary>
    internal static void UnbindRenderbuffer(StateCache cache) => BindRenderbuffer(cache, 0);
    #endregion
    #endregion
}
