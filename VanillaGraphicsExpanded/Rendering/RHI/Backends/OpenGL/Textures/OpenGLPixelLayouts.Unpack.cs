using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes pixel layouts against the existing single selector cache.</summary>
internal static partial class OpenGLPixelLayouts
{
    #region Internal API
    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    internal static OpenGlStateCache.PixelUnpackState GetUnpack(OpenGlStateCache cache)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        // External rendering boundaries must invalidate the cache before borrowing state.
        return cache.PixelUnpackKnowledge ??= new OpenGlStateCache.PixelUnpackState(
            ReadLayoutValue(cache, backend, GetPName.UnpackAlignment),
            ReadLayoutValue(cache, backend, GetPName.UnpackRowLength),
            ReadLayoutValue(cache, backend, GetPName.UnpackSkipRows),
            ReadLayoutValue(cache, backend, GetPName.UnpackSkipPixels),
            ReadLayoutValue(cache, backend, GetPName.UnpackSwapBytes) != 0,
            ReadLayoutValue(cache, backend, GetPName.UnpackLsbFirst) != 0,
            ReadLayoutValue(cache, backend, GetPName.UnpackImageHeight),
            ReadLayoutValue(cache, backend, GetPName.UnpackSkipImages));
    }

    /// <summary>Applies an unpack layout, omitting driver calls for known unchanged values.</summary>
    internal static void SetUnpack(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0 || state.ImageHeight < 0 || state.SkipImages < 0) throw new ArgumentOutOfRangeException(nameof(state));
        // A known identical layout is a pure cache hit, including its error state.
        if (cache.PixelUnpackKnowledge == state) return;
        var previous = cache.PixelUnpackKnowledge;
        // Leave the cache unknown if a driver call throws partway through the update.
        cache.PixelUnpackKnowledge = null;
        // Optional diagnostics share the ordinary transition policy; boundary restoration owns its checks.
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        if (previous?.Alignment != state.Alignment) { GL.PixelStore(PixelStoreParameter.UnpackAlignment, state.Alignment); backend.RequireContext(); }
        if (previous?.RowLength != state.RowLength) { GL.PixelStore(PixelStoreParameter.UnpackRowLength, state.RowLength); backend.RequireContext(); }
        if (previous?.SkipRows != state.SkipRows) { GL.PixelStore(PixelStoreParameter.UnpackSkipRows, state.SkipRows); backend.RequireContext(); }
        if (previous?.SkipPixels != state.SkipPixels) { GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, state.SkipPixels); backend.RequireContext(); }
        if (previous?.SwapBytes != state.SwapBytes) { GL.PixelStore(PixelStoreParameter.UnpackSwapBytes, state.SwapBytes ? 1 : 0); backend.RequireContext(); }
        if (previous?.LsbFirst != state.LsbFirst) { GL.PixelStore(PixelStoreParameter.UnpackLsbFirst, state.LsbFirst ? 1 : 0); backend.RequireContext(); }
        if (previous?.ImageHeight != state.ImageHeight) { GL.PixelStore(PixelStoreParameter.UnpackImageHeight, state.ImageHeight); backend.RequireContext(); }
        if (previous?.SkipImages != state.SkipImages) { GL.PixelStore(PixelStoreParameter.UnpackSkipImages, state.SkipImages); backend.RequireContext(); }
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        backend.RequireContext();
        cache.PixelUnpackKnowledge = state;
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    internal static PixelUnpackScope UnpackScope(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        var previous = GetUnpack(cache);
        try { SetUnpack(cache, state); }
        catch (Exception operation)
        {
            try { if (backend.IsCurrentContext) SetUnpack(cache, previous); else cache.DirtyPixelUnpackState(); }
            catch (Exception cleanup) { throw new AggregateException(operation, cleanup); }
            throw;
        }
        return new PixelUnpackScope(cache, previous);
    }
    #endregion

    /// <summary>Restores the original context layout without donating state to a replacement context.</summary>
    internal readonly struct PixelUnpackScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly OpenGlStateCache.PixelUnpackState previous;
        private readonly RHICommandContext backend;
        #region Internal API
        /// <summary>Captures original context and selector facts after the temporary transition succeeds.</summary>
        internal PixelUnpackScope(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState previous)
        {
            this.cache = cache; this.previous = previous; backend = RHICommandContext.Current;
        }
        #endregion
        #region Public API
        /// <summary>Restores only in the original live namespace, withdrawing knowledge otherwise.</summary>
        public void Dispose()
        {
            if (!backend.IsCurrentContext) { cache.DirtyPixelUnpackState(); return; }
            SetUnpack(cache, previous);
        }
        #endregion
    }
}
