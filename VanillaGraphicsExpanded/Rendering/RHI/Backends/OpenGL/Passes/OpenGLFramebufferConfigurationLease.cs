using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Withdraws one immutable declaration on owner release or original-context retirement without native work.</summary>
internal sealed class OpenGLFramebufferConfigurationLease : IDisposable
{
    private readonly OpenGlStateCache cache;
    private readonly GpuFramebuffer owner;
    private readonly int id;
    private readonly (nint Handle, long Generation) context;
    private int disposed;

    #region Public API
    /// <summary>Releases only this declaration; worker disposal never selects a context or issues GL calls.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        RenderContextRegistry.Retiring -= OnContextRetiring;
        cache.UnregisterImmutableFramebuffer(id, owner);
    }
    #endregion

    #region Internal API
    /// <summary>Captures the admitted original identity and registers its completed configuration once.</summary>
    internal OpenGLFramebufferConfigurationLease(OpenGlStateCache cache, GpuFramebuffer owner)
    {
        this.cache = cache;
        this.owner = owner;
        id = owner.FboId;
        context = owner.OriginalContext;
        cache.RegisterImmutableFramebuffer(owner);
        RenderContextRegistry.Retiring += OnContextRetiring;
    }
    #endregion

    #region Private
    /// <summary>Removes authority only when this lease's original native namespace retires.</summary>
    private void OnContextRetiring((nint Handle, long Generation) retiring)
    {
        if (retiring == context) Dispose();
    }
    #endregion
}
